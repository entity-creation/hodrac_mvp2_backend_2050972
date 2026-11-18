using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Services;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Hodrac_Backend_MVP2.Tests.Infrastructure;

/// <summary>
/// Boots the real API in-process against a throwaway Postgres database
/// (<c>hodrac_test</c> on the local Docker container, port 5434), recreated
/// once per test run. Environment is "Testing": no user-secrets (which point
/// at production), no dev-only migrate/seed blocks, no background jobs.
/// MongoDB is optional; the app starts without it.
/// </summary>
public class HodracApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestDatabase = "hodrac_test";

    public static readonly string PostgresConnectionString =
        Environment.GetEnvironmentVariable("HODRAC_TEST_POSTGRES")
        ?? $"Host=localhost;Port=5434;Database={TestDatabase};Username=postgres;Password=devpass";

    public static readonly string MongoConnectionString =
        Environment.GetEnvironmentVariable("HODRAC_TEST_MONGO")
        ?? "mongodb://localhost:27017";

    /// <summary>Webhook signing key in tests (never a real key; IPaystackClient is faked).</summary>
    public const string PaystackSecretKey = "sk_test_hodrac_integration_0123456789";

    public const string JwtSecret = "hodrac-integration-tests-secret-key-0123456789abcdef";

    /// <summary>Emails in Admin:Emails (the "Admin" policy). Register them with AuthHelper.GetOrRegisterAsync.</summary>
    public const string AdminEmail = "admin-1@hodrac.test";
    public const string SecondAdminEmail = "admin-2@hodrac.test";

    /// <summary>Small so the founding-host limit can be exercised.</summary>
    public const int FoundingHostLimit = 2;

    /// <summary>The fake Paystack used instead of the real client (never calls the network).</summary>
    public FakePaystackClient Paystack => Services.GetRequiredService<FakePaystackClient>();

    /// <summary>Emails "sent" by the app.</summary>
    public RecordingEmailSender Emails => Services.GetRequiredService<RecordingEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting (not ConfigureAppConfiguration) so values are visible to
        // Program.cs code that reads configuration before builder.Build().
        builder.UseSetting("ConnectionStrings:Postgres", PostgresConnectionString);
        builder.UseSetting("ConnectionStrings:MongoDB", MongoConnectionString);
        builder.UseSetting("MongoDB:DatabaseName", "hodrac_behavioral_test");
        builder.UseSetting("Jwt:Secret", JwtSecret);
        builder.UseSetting("Embedding:Railway:Endpoint", "http://localhost:1/");
        builder.UseSetting("Email:Smtp:Host", "");
        builder.UseSetting("Admin:Emails", $"{AdminEmail},{SecondAdminEmail}");
        builder.UseSetting("Hodrac:FoundingHostLimit", FoundingHostLimit.ToString());
        // Used only to sign/verify test webhooks: the Paystack client is faked.
        builder.UseSetting("Paystack:SecretKey", PaystackSecretKey);

        builder.ConfigureServices(services =>
        {
            // Drop the app's own background services (scheduled jobs, digests,
            // currency refresh against an external API, Mongo index init, ...).
            var appAssembly = typeof(Program).Assembly;
            var hosted = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                         && d.ImplementationType?.Assembly == appAssembly)
                .ToList();
            foreach (var d in hosted) services.Remove(d);

            // Never call real Paystack or SMTP from tests.
            services.RemoveAll<IPaystackClient>();
            services.AddSingleton<FakePaystackClient>();
            services.AddSingleton<IPaystackClient>(sp => sp.GetRequiredService<FakePaystackClient>());

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<RecordingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
        });
    }

    public async Task InitializeAsync()
    {
        AssertLocalTestDatabase(PostgresConnectionString);

        // Recreate the database so every run starts from a clean, fully-migrated schema.
        var csb = new NpgsqlConnectionStringBuilder(PostgresConnectionString);
        var database = csb.Database!;
        csb.Database = "postgres";
        await using (var conn = new NpgsqlConnection(csb.ConnectionString))
        {
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE);", conn);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\";", conn);
            await create.ExecuteNonQueryAsync();
        }

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HodracDbContext>();
        // Belt and braces: the context the app actually uses must be the test db.
        AssertLocalTestDatabase(db.Database.GetConnectionString()!);

        // The InitialMigration uses the pgvector type before a later migration
        // enables the extension, so a brand-new database needs it up front.
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
        await db.Database.MigrateAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => base.DisposeAsync().AsTask();

    private static void AssertLocalTestDatabase(string connectionString)
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        var host = csb.Host ?? string.Empty;
        var isLocal = host is "localhost" or "127.0.0.1" or "::1";
        if (!isLocal || !string.Equals(csb.Database, TestDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Refusing to run integration tests against '{host}/{csb.Database}'. " +
                $"Tests only run against a local '{TestDatabase}' database.");
    }
}

/// <summary>All integration tests share one app + database (created once per run).</summary>
[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<HodracApiFactory>
{
    public const string Name = "Integration";
}
