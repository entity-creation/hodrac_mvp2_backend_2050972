using Hodrac_Backend_MVP2.NoSql.Models;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services
{
    // Ensures the MongoDB indexes in the background instead of blocking startup.
    // If Mongo is unreachable (e.g. the Atlas IP allowlist rejects this host)
    // the API still starts and serves everything Postgres-backed; this keeps
    // retrying until the indexes are in place.
    public class MongoIndexInitializer : BackgroundService
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

        private readonly IMongoDatabase _db;
        private readonly ILogger<MongoIndexInitializer> _logger;

        public MongoIndexInitializer(IMongoDatabase db, ILogger<MongoIndexInitializer> logger)
        {
            _db = db;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MongoIndexDefinitions.EnsureIndexesAsync(_db);
                    _logger.LogInformation("MongoDB indexes ensured");
                    return;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "MongoDB unavailable; indexes not ensured. Profile/personalisation features will fail until it is reachable. Retrying in {Delay}",
                        RetryDelay);
                }

                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
