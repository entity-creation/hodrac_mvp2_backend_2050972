using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Infrastructure.Jobs;
using Hodrac_Backend_MVP2.Infrastucture.Jobs;
using Hodrac_Backend_MVP2.Infrastructure.SignalR;
using Hodrac_Backend_MVP2.Infrastucture.SignalR;
using Hodrac_Backend_MVP2.Interfaces;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.NoSql.Interfaces;
using Hodrac_Backend_MVP2.NoSql.Models;
using Hodrac_Backend_MVP2.NoSql.Repositories;
using Hodrac_Backend_MVP2.Repositories;
using Hodrac_Backend_MVP2.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using StackExchange.Redis;
using System.Text;
using System.Threading.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

// ─── PostgreSQL + EF Core + pgvector ─────────────────────────────────────────
builder.Services.AddDbContext<HodracDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        npgsql => npgsql.UseVector()
    )
);

// ─── ASP.NET Core Identity ────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit           = true;
    options.Password.RequireLowercase       = true;
    options.Password.RequireUppercase       = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength         = 8;

    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers      = true;

    options.User.RequireUniqueEmail = true;

    options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider<ApplicationUser>.ProviderName;
})
.AddEntityFrameworkStores<HodracDbContext>()
.AddDefaultTokenProviders()
.AddTokenProvider<EmailConfirmationTokenProvider<ApplicationUser>>(EmailConfirmationTokenProvider<ApplicationUser>.ProviderName);

// Emailed link lifetimes. Password reset uses Identity's default provider;
// a reset link is also single-use (resetting changes the security stamp).
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
builder.Services.Configure<EmailConfirmationTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(3));

// Persist Data Protection keys in Postgres so emailed links (and anything else
// signed with these keys) survive restarts/redeploys on Render's ephemeral disk.
builder.Services.AddDataProtection()
    .SetApplicationName("Hodrac")
    .PersistKeysToDbContext<HodracDbContext>();

// ─── JWT Authentication ───────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret must be set in configuration.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.Zero,
        };

        // SignalR: accept token from query string (browsers can't set WS headers)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/wishlistHub") ||
                     path.StartsWithSegments("/tripPostHub") ||
                     path.StartsWithSegments("/tripThreadHub") ||
                     path.StartsWithSegments("/notificationHub")))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

// "Admin" = signed-in user whose email is listed in Admin:Emails (config/env).
var adminEmails = (builder.Configuration["Admin:Emails"] ?? string.Empty)
    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
        {
            var email = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                     ?? ctx.User.FindFirst("email")?.Value;
            return email is not null && adminEmails.Contains(email);
        })));

// Behind Render's proxy: trust X-Forwarded-For/Proto so RemoteIpAddress (rate
// limiting) and Request.Scheme (partner tracking URLs) reflect the real client.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─── MongoDB ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("MongoDB"))
);
builder.Services.AddSingleton<IMongoDatabase>(sp =>
    sp.GetRequiredService<IMongoClient>()
      .GetDatabase(builder.Configuration["MongoDB:DatabaseName"] ?? "hodrac_behavioral")
);

// ─── Redis ────────────────────────────────────────────────────────────────────
//builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
//    ConnectionMultiplexer.Connect(
//        builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"
//    )
//);

// ─── PostgreSQL Repositories ──────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDestinationRepository, DestinationRepository>();
builder.Services.AddScoped<IWishlistRepository, WishlistRepository>();
builder.Services.AddScoped<ICollaboratorRepository, CollaboratorRepository>();
builder.Services.AddScoped<ISavedContentRepository, SavedContentRepository>();
builder.Services.AddScoped<ITripsRepository, TripsRepository>();

// ─── MongoDB Repositories ─────────────────────────────────────────────────────
builder.Services.AddScoped<IProfileRepository, ProfileRepository>();
builder.Services.AddScoped<IInteractionEventRepository, InteractionEventRepository>();
builder.Services.AddScoped<ISegmentedPopularityRepository, SegmentedPopularityRepository>();

// ─── Auth Services ────────────────────────────────────────────────────────────
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// ─── Domain Services ──────────────────────────────────────────────────────────
builder.Services.AddScoped<TagInferenceService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<PopularWishlistService>();
builder.Services.AddScoped<FeaturedWishlistService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPartnerService, PartnerService>();

// ─── Currency rates (daily refresh, cached in memory + Postgres) ──────────────
builder.Services.AddHttpClient(CurrencyRateService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<ICurrencyRateService, CurrencyRateService>();
builder.Services.AddHostedService<CurrencyRateRefresher>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<EmailDigestService>();

// ─── Personalisation events (in-process queue → TagInferenceService) ──────────
builder.Services.AddSingleton<BehaviorEventQueue>();
builder.Services.AddSingleton<IBehaviorEventQueue>(sp => sp.GetRequiredService<BehaviorEventQueue>());
builder.Services.AddHostedService<BehaviorEventWorker>();

// ─── Scheduled jobs ───────────────────────────────────────────────────────────
builder.Services.AddScoped<ImpressionResetJob>();
builder.Services.AddScoped<ExpiredTokenCleanupJob>();
builder.Services.AddScoped<SemanticClusterMergeJob>();
builder.Services.AddHostedService<ScheduledJobsService>();
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();
builder.Services.AddHttpClient<IEmbeddingService, PythonEmbeddingService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Embedding:Railway:Endpoint"] ?? string.Empty);
    client.Timeout = TimeSpan.FromSeconds(10); // backstop; the service enforces 5s per call
});
//builder.Services.AddScoped<IEmbeddingService, PythonEmbeddingService>();

// ─── Rate limiting ────────────────────────────────────────────────────────────
// Public, unauthenticated form: cap submissions per client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("partner-leads", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10)
            }));

    // Endpoints that send email (forgot-password, resend-confirmation).
    options.AddPolicy("auth-email", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15)
            }));

    // Public (logged-out) read endpoints such as /api/trips/public.
    options.AddPolicy("public-browse", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// ─── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:5173", "http://localhost:5173"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
    )
);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description  = "Enter your JWT access token"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ─── Startup initializers ─────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
    await MongoIndexDefinitions.EnsureIndexesAsync(db);
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<HodracDbContext>().Database.MigrateAsync();
}

// ─── Middleware pipeline (order is critical) ──────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();

    // Run EF Core migrations (idempotent — safe to call on every startup)
    await db.Database.MigrateAsync();

    // Seed all reference data and migrate existing destinations + wishlists.
    // Every seed method is guarded by an AnyAsync check so re-running is safe.
    //await Hodrac_Backend_MVP2.Infrastucture.Seeder.HawaiiSeeder.SeedOahuUltimateIslandWishlist(db);
}

app.UseForwardedHeaders();
app.UseCors();
app.UseAuthentication();   // Validates JWT → populates HttpContext.User
app.UseAuthorization();    // Enforces [Authorize] attributes
app.UseRateLimiter();
app.MapControllers();
app.MapHub<WishlistHub>("/wishlistHub");
app.MapHub<TripPostHub>("/tripPostHub");
app.MapHub<TripThreadHub>("/tripThreadHub");
app.MapHub<NotificationHub>("/notificationHub");

app.Run();
