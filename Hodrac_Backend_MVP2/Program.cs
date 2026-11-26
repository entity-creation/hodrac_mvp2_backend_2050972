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
using Hodrac_Backend_MVP2.Services.Payments;
using Hodrac_Backend_MVP2.Services.Images;
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


// One-off command flags (e.g. --seed-solohouse) are stripped before the args reach
// configuration: the command-line provider would otherwise treat a bare flag as a
// key and swallow the next argument (such as a --ConnectionStrings override) as its value.
var commandFlags = new[] { "--seed-poc", "--seed-solohouse", "--remove-solohouse", "--seed-bali", "--seed-calabar", "--seed-kenya-north", "--seed-destinations", "--seed-events",
    "--seed-ghana", "--seed-benin-republic", "--seed-zanzibar", "--seed-rwanda-qatar", "--seed-mombasa", "--seed-diani", "--seed-blog",
    "--rewrite-image-host", "--apply" };
// Command options that take a value (`--from <url>`): both the flag and its value are stripped.
var commandValueFlags = new[] { "--from", "--to" };
string? CommandValue(string flag)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
var configArgs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (commandFlags.Contains(args[i])) continue;
    if (commandValueFlags.Contains(args[i])) { i++; continue; }
    configArgs.Add(args[i]);
}
var builder = WebApplication.CreateBuilder(configArgs.ToArray());

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
// Short timeouts: when Mongo is unreachable, requests that touch it fail in
// seconds instead of hanging for the driver's 30s default.
builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(builder.Configuration.GetConnectionString("MongoDB"));
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
    settings.ConnectTimeout = TimeSpan.FromSeconds(5);
    return new MongoClient(settings);
});
builder.Services.AddScoped<OnboardingProfileSync>();
builder.Services.AddHostedService<MongoIndexInitializer>();
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
builder.Services.AddScoped<AdminInsightsService>();
builder.Services.AddScoped<InsightsRebuilder>();
builder.Services.AddSingleton<InsightsRebuildJobManager>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<PopularWishlistService>();
builder.Services.AddScoped<FeaturedWishlistService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IPartnerService, PartnerService>();
builder.Services.AddScoped<IShareService, ShareService>();
builder.Services.AddScoped<ITripPageViewCounter, TripPageViewCounter>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IHeroWishlistService, HeroWishlistService>();

// ─── Hodrac Host (hosted trips + Paystack) ────────────────────────────────────
// Values come from env vars / user-secrets (Hodrac__FeePercent, Paystack__SecretKey, ...);
// the option classes carry the spec defaults. Never put the Paystack key in appsettings.
builder.Services.Configure<HodracHostOptions>(builder.Configuration.GetSection(HodracHostOptions.SectionName));
builder.Services.Configure<PaystackOptions>(builder.Configuration.GetSection(PaystackOptions.SectionName));

// ─── Image hosting (R2 primary, Vercel Blob fallback) ─────────────────────────
// Images__PublicBaseUrl / Images__FallbackBaseUrls__0; defaults keep the Vercel host.
builder.Services.AddOptions<ImagesOptions>()
    .Bind(builder.Configuration.GetSection(ImagesOptions.SectionName))
    .Validate(o => ImagesOptions.IsAbsoluteHttpUrl(o.PublicBaseUrl)
                && (o.FallbackBaseUrls ?? Array.Empty<string>()).All(u => string.IsNullOrWhiteSpace(u) || ImagesOptions.IsAbsoluteHttpUrl(u)),
        "Images:PublicBaseUrl and every Images:FallbackBaseUrls entry must be absolute http(s) URLs.")
    .ValidateOnStart();
builder.Services.AddScoped<ImageHostRewriter>();
// Swaps an old-host prefix for the primary host on every endpoint that saves an image URL.
builder.Services.AddSingleton<ImageUrlNormalizer>();
// Seeders build image URLs from the configured host (and recognise the old ones).
var imagesOptions = builder.Configuration.GetSection(ImagesOptions.SectionName).Get<ImagesOptions>() ?? new ImagesOptions();
Hodrac_Backend_MVP2.Infrastucture.Seeder.SeedImages.Configure(imagesOptions);
builder.Services.AddSingleton(sp => new FeeCalculator(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HodracHostOptions>>()));
// Paystack typed client: Bearer secret key per request, base URL from options, 15s timeout.
builder.Services.AddHttpClient<IPaystackClient, PaystackClient>((sp, client) =>
{
    var paystack = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaystackOptions>>().Value;
    client.BaseAddress = PaystackClient.NormalizeBaseUrl(paystack.BaseUrl);
    client.Timeout = PaystackClient.DefaultTimeout;
});
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Hosts.HostService>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Hosts.HostTripService>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Hosts.TravellerRemovalService>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Hosts.HostReportService>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Hosts.HostDashboardService>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Hosts.HostingInsightsService>();
builder.Services.AddSingleton<Hodrac_Backend_MVP2.Services.Hosts.HostDocumentLinkSigner>();
builder.Services.AddScoped<ITripThreadService, TripThreadService>();
// Trip creation shared by POST /api/trips and the admin create-for-host endpoint.
builder.Services.AddScoped<TripCreationService>();
// The only place money state changes (idempotent on the Paystack reference), and the webhook handler.
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<PaystackWebhookProcessor>();

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
// Hodrac Host jobs (hosted trips only; idempotent; see each job's summary).
builder.Services.AddScoped<InstalmentReminderJob>();
builder.Services.AddScoped<SeatHoldExpiryJob>();
builder.Services.AddScoped<PaymentReconcileJob>();
builder.Services.AddScoped<HostWeeklyReportJob>();
builder.Services.AddScoped<HostDocumentPurgeJob>();
builder.Services.AddHostedService<ScheduledJobsService>();
// Weekly newsletter: draft (Wed 09:00 WAT) and sender (approved issues, batched).
builder.Services.Configure<Hodrac_Backend_MVP2.Services.Newsletter.NewsletterOptions>(
    builder.Configuration.GetSection(Hodrac_Backend_MVP2.Services.Newsletter.NewsletterOptions.SectionName));
builder.Services.AddSingleton<Hodrac_Backend_MVP2.Services.Newsletter.NewsletterUnsubscribeTokens>();
builder.Services.AddSingleton<Hodrac_Backend_MVP2.Services.Newsletter.NewsletterRenderer>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Newsletter.NewsletterDraftService>();
builder.Services.AddScoped<Hodrac_Backend_MVP2.Services.Newsletter.NewsletterAudienceService>();
builder.Services.AddScoped<NewsletterDraftJob>();
builder.Services.AddScoped<NewsletterSendJob>();
// Hourly seat-hold expiry, 30-minute payment reconcile, newsletter draft check (15 min) and sender (5 min).
builder.Services.AddHostedService<IntervalJobsService>();
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

    // ── Payments (attached to endpoints in the payments phase) ──
    // Paystack webhook: Paystack retries and bursts, so keep it generous; per source IP.
    options.AddPolicy("paystack-webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Checkout: 10 a minute per signed-in user (falls back to IP).
    options.AddPolicy("payments-checkout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitUserKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Verify: 30 a minute per signed-in user (falls back to IP).
    options.AddPolicy("payments-verify", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitUserKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Host payout account (lookups, setup and password-checked changes): 10 per
    // 5 minutes per signed-in user, which also caps wrong-password guesses.
    options.AddPolicy("host-payout-account", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitUserKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5)
            }));

    // UseRateLimiter runs after UseAuthentication, so the JWT user is available here.
    static string RateLimitUserKey(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.User.FindFirst("sub")?.Value;
        return !string.IsNullOrEmpty(userId)
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
});

// ─── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── CORS ─────────────────────────────────────────────────────────────────────
// Extra origins (e.g. the admin dashboard) via Cors:AllowedOrigins, comma-separated.
var corsOrigins = new[] { builder.Configuration["Frontend:BaseUrl"] , "http://localhost:5173", "http://localhost:5174" }
    .Concat((builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .Select(o => o.TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
    )
);

// Every JSON error (bare-string / { error } bodies included) leaves as an RFC 7807
// ProblemDetails with an "error" extension; see ErrorProblemDetailsFilter.
builder.Services.AddControllers(o => o.Filters.Add<Hodrac_Backend_MVP2.Infrastucture.Http.ErrorProblemDetailsFilter>());
// ProblemDetails for unhandled exceptions (no stack traces outside Development)
// and for empty error responses written outside MVC (auth challenges, 429s, ...).
builder.Services.AddProblemDetails();
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

// ─── One-off: `dotnet run -- --rewrite-image-host [--apply] [--from <url>] [--to <url>]` ──
// Moves stored image URLs from each Images:FallbackBaseUrls entry (or --from) to
// Images:PublicBaseUrl (or --to). Dry run (counts + samples) unless --apply, which
// updates every image column in one transaction. Idempotent; swap the bases to undo.
// Run it right after switching hosts and BEFORE any further --seed-* command.
if (args.Contains("--rewrite-image-host"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    var apply = args.Contains("--apply");
    var fromArg = CommandValue("--from");
    var froms = fromArg is not null ? new List<string> { fromArg } : imagesOptions.NormalizedFallbackBaseUrls.ToList();
    var to = CommandValue("--to") ?? imagesOptions.NormalizedPublicBaseUrl;
    if (froms.Count == 0)
    {
        app.Logger.LogError("Nothing to rewrite: set Images:FallbackBaseUrls:0 to the old host or pass --from <url>.");
        Environment.ExitCode = 1;
        return;
    }

    app.Logger.LogInformation("Image host rewrite ({Mode}) on {Host}: {From} -> {To}",
        apply ? "APPLY" : "dry run", pgHost, string.Join(", ", froms), to);
    using var scope = app.Services.CreateScope();
    var rewriter = scope.ServiceProvider.GetRequiredService<ImageHostRewriter>();
    foreach (var from in froms)
    {
        try
        {
            var report = await rewriter.RunAsync(from, to, apply);
            foreach (var column in report.Columns.Where(c => c.Matched > 0))
                foreach (var sample in column.Samples)
                    app.Logger.LogInformation("  sample {Table}.{Column}: {Sample}", column.Table, column.Column, sample);
            if (apply)
                app.Logger.LogInformation("Rewrote {Updated} of {Matched} image URLs from {From} to {To}.",
                    report.TotalUpdated, report.TotalMatched, report.FromBase, report.ToBase);
            else
                app.Logger.LogInformation("Dry run: {Matched} image URLs start with {From} and would move to {To}. Nothing changed; re-run with --apply to rewrite.",
                    report.TotalMatched, report.FromBase, report.ToBase);
        }
        catch (ArgumentException ex)
        {
            app.Logger.LogError("Image host rewrite refused: {Reason}", ex.Message);
            Environment.ExitCode = 1;
        }
    }
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-bali` ─────────────────────────────
// Applies pending migrations, seeds the "Bali Island Essentials" template wishlist
// (idempotent — safe to re-run), then exits without serving.
if (args.Contains("--seed-bali"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    // The InitialMigration uses the pgvector type before a later migration enables
    // the extension, so a brand-new database needs it created up front.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    await Hodrac_Backend_MVP2.Infrastucture.Seeder.BaliSeeder.SeedAsync(db);
    app.Logger.LogInformation("Bali seed complete into {Host}", pgHost);
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-calabar` ──────────────────────────
// Applies pending migrations, seeds the "Carnival Calabar 2026" template wishlist
// (idempotent — safe to re-run), then exits without serving.
if (args.Contains("--seed-calabar"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    // The InitialMigration uses the pgvector type before a later migration enables
    // the extension, so a brand-new database needs it created up front.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    await Hodrac_Backend_MVP2.Infrastucture.Seeder.CalabarSeeder.SeedAsync(db);
    app.Logger.LogInformation("Calabar seed complete into {Host}. Images referenced: {Images}",
        pgHost, string.Join(", ", Hodrac_Backend_MVP2.Infrastucture.Seeder.CalabarSeeder.ExpectedImageFiles));
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-kenya-north` ──────────────────────
// Applies pending migrations, seeds the "Northern Kenya: Mountains, Elephants and the Ndotos"
// template wishlist (idempotent — safe to re-run), then exits without serving.
if (args.Contains("--seed-kenya-north"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    // The InitialMigration uses the pgvector type before a later migration enables
    // the extension, so a brand-new database needs it created up front.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    await Hodrac_Backend_MVP2.Infrastucture.Seeder.KenyaNorthSeeder.SeedAsync(db, logger: app.Logger);
    app.Logger.LogInformation("Northern Kenya seed complete into {Host}. Images referenced: {Images}",
        pgHost, string.Join(", ", Hodrac_Backend_MVP2.Infrastucture.Seeder.KenyaNorthSeeder.ExpectedImageFiles));
    return;
}

// ─── One-off seeding: country trip wishlists ──────────────────────────────────
// `dotnet run -- --seed-ghana | --seed-benin-republic | --seed-zanzibar | --seed-rwanda-qatar | --seed-mombasa | --seed-diani`
// Same steps as the Kenya seed: vector extension, pending migrations, idempotent seed, exit.
var tripSeeders = new (string Flag, string Label, Func<HodracDbContext, Task> Seed, IReadOnlyList<string> Images)[]
{
    ("--seed-ghana", "Ghana", db => Hodrac_Backend_MVP2.Infrastucture.Seeder.GhanaSeeder.SeedAsync(db),
        Hodrac_Backend_MVP2.Infrastucture.Seeder.GhanaSeeder.ExpectedImageFiles),
    ("--seed-benin-republic", "Benin Republic", db => Hodrac_Backend_MVP2.Infrastucture.Seeder.BeninRepublicSeeder.SeedAsync(db),
        Hodrac_Backend_MVP2.Infrastucture.Seeder.BeninRepublicSeeder.ExpectedImageFiles),
    ("--seed-zanzibar", "Zanzibar", db => Hodrac_Backend_MVP2.Infrastucture.Seeder.ZanzibarSeeder.SeedAsync(db),
        Hodrac_Backend_MVP2.Infrastucture.Seeder.ZanzibarSeeder.ExpectedImageFiles),
    ("--seed-rwanda-qatar", "Rwanda & Qatar", db => Hodrac_Backend_MVP2.Infrastucture.Seeder.RwandaQatarSeeder.SeedAsync(db),
        Hodrac_Backend_MVP2.Infrastucture.Seeder.RwandaQatarSeeder.ExpectedImageFiles),
    ("--seed-mombasa", "Mombasa", db => Hodrac_Backend_MVP2.Infrastucture.Seeder.MombasaSeeder.SeedAsync(db, logger: app.Logger),
        Hodrac_Backend_MVP2.Infrastucture.Seeder.MombasaSeeder.ExpectedImageFiles),
    ("--seed-diani", "Diani", db => Hodrac_Backend_MVP2.Infrastucture.Seeder.DianiSeeder.SeedAsync(db, logger: app.Logger),
        Hodrac_Backend_MVP2.Infrastucture.Seeder.DianiSeeder.ExpectedImageFiles),
};
var tripSeeder = tripSeeders.FirstOrDefault(s => args.Contains(s.Flag));
if (tripSeeder.Flag is not null)
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    await tripSeeder.Seed(db);
    app.Logger.LogInformation("{Label} seed complete into {Host}. Images referenced: {Images}",
        tripSeeder.Label, pgHost, string.Join(", ", tripSeeder.Images));
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-destinations` ─────────────────────
// Applies pending migrations, seeds the standalone destinations in DestinationsSeeder
// (no wishlist; idempotent — safe to re-run), then exits without serving.
if (args.Contains("--seed-destinations"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    // The InitialMigration uses the pgvector type before a later migration enables
    // the extension, so a brand-new database needs it created up front.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    await Hodrac_Backend_MVP2.Infrastucture.Seeder.DestinationsSeeder.SeedAsync(db);
    app.Logger.LogInformation("Standalone destinations seed complete into {Host}. Images referenced: {Images}",
        pgHost, string.Join(", ", Hodrac_Backend_MVP2.Infrastucture.Seeder.DestinationsSeeder.ExpectedImageFiles));
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-events` ───────────────────────────
// Applies pending migrations, seeds the Benin City events in EventsSeeder
// (idempotent by slug — safe to re-run), then exits without serving.
if (args.Contains("--seed-events"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    // The InitialMigration uses the pgvector type before a later migration enables
    // the extension, so a brand-new database needs it created up front.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    var createdEvents = await Hodrac_Backend_MVP2.Infrastucture.Seeder.EventsSeeder.SeedAsync(db, app.Logger);
    app.Logger.LogInformation("Events seed complete into {Host}: {Created} created.", pgHost, createdEvents);
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-blog` ─────────────────────────────
// Applies pending migrations, seeds the starter blog posts in BlogSeeder as Drafts
// (idempotent by slug — never overwrites an existing post — safe to re-run), then exits.
// Run after the destinations, trip wishlists and events it links to; missing links are logged and skipped.
if (args.Contains("--seed-blog"))
{
    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
    // The InitialMigration uses the pgvector type before a later migration enables
    // the extension, so a brand-new database needs it created up front.
    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
    await db.Database.MigrateAsync();
    var createdPosts = await Hodrac_Backend_MVP2.Infrastucture.Seeder.BlogSeeder.SeedAsync(db, app.Logger);
    app.Logger.LogInformation("Blog seed complete into {Host}: {Created} draft posts created.", pgHost, createdPosts);
    return;
}

// ─── One-off seeding: `dotnet run -- --seed-poc` ──────────────────────────────
// Applies pending migrations, seeds the Mauritius/Lagos/Abuja/Edo proof-of-
// concept content (idempotent — safe to re-run), then exits without serving.
//if (args.Contains("--seed-poc"))
//{
//    using var scope = app.Services.CreateScope();
//    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
//    await db.Database.MigrateAsync();
//    await Hodrac_Backend_MVP2.Infrastucture.Seeder.AfricaPocSeeder.SeedAsync(db);
//    app.Logger.LogInformation("Proof-of-concept seed complete.");
//    return;
//}

// ─── Sales mockup (dev only): `dotnet run -- --seed-solohouse` / `--remove-solohouse` ──
// Refuses to touch the Render (production) database so the mockup never goes live.
//if (args.Contains("--seed-solohouse") || args.Contains("--remove-solohouse"))
//{
//    var pgHost = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Postgres")).Host ?? string.Empty;
//    if (pgHost.Contains("render.com", StringComparison.OrdinalIgnoreCase) || pgHost.StartsWith("dpg-", StringComparison.OrdinalIgnoreCase))
//    {
//        app.Logger.LogError("Refusing to run the Solo House mockup against the Render database ({Host}). Point ConnectionStrings:Postgres at a local dev database first.", pgHost);
//        return;
//    }

//    using var scope = app.Services.CreateScope();
//    var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
//    // The InitialMigration uses the pgvector type before a later migration enables
//    // the extension, so a brand-new database needs it created up front.
//    await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
//    await db.Database.MigrateAsync();
//    if (args.Contains("--remove-solohouse"))
//    {
//        await Hodrac_Backend_MVP2.Infrastucture.Seeder.SoloHouseMockSeeder.RemoveAsync(db);
//        app.Logger.LogInformation("Solo House mockup removed from {Host}.", pgHost);
//    }
//    else
//    {
//        await Hodrac_Backend_MVP2.Infrastucture.Seeder.SoloHouseMockSeeder.SeedAsync(db);
//        app.Logger.LogInformation("Solo House mockup seeded into {Host}.", pgHost);
//    }
//    return;
//}

// ─── Startup initializers ─────────────────────────────────────────────────────
// MongoDB indexes are ensured in the background by MongoIndexInitializer so an
// unreachable Mongo doesn't stop the API from starting.

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

// Development keeps the developer exception page (added by WebApplication);
// everywhere else an unhandled exception is a 500 ProblemDetails.
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();
app.UseStatusCodePages();

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

// Exposed for WebApplicationFactory<Program> in Hodrac_Backend_MVP2.Tests.
public partial class Program { }
