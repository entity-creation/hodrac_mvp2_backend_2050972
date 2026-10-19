using Hodrac_Backend_MVP2.Infrastructure.Jobs;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs the maintenance jobs on a daily UTC schedule. Each job gets its own
    /// DI scope (they depend on the scoped DbContext). A failing job is logged
    /// and retried at its next slot; it never stops the others.
    ///
    /// Single-instance assumption: if the API is ever scaled out, move these to
    /// one worker (or add a DB lock) so jobs don't run once per instance.
    /// </summary>
    public class ScheduledJobsService : BackgroundService
    {
        private static readonly (string Name, TimeSpan TimeOfDayUtc, Func<IServiceProvider, CancellationToken, Task> Run)[] Jobs =
        {
            ("ImpressionReset",       TimeSpan.Zero,          (sp, ct) => sp.GetRequiredService<ImpressionResetJob>().RunAsync(ct)),
            ("ExpiredTokenCleanup",   TimeSpan.FromHours(1),  (sp, ct) => sp.GetRequiredService<ExpiredTokenCleanupJob>().RunAsync(ct)),
            ("SemanticClusterMerge",  TimeSpan.FromHours(2),  (sp, ct) => sp.GetRequiredService<SemanticClusterMergeJob>().RunAsync(ct)),
        };

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ScheduledJobsService> _logger;

        public ScheduledJobsService(IServiceScopeFactory scopes, ILogger<ScheduledJobsService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var nextRun = Jobs.ToDictionary(j => j.Name, j => NextOccurrence(j.TimeOfDayUtc, DateTime.UtcNow));

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                foreach (var job in Jobs.Where(j => nextRun[j.Name] <= now))
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        await job.Run(scope.ServiceProvider, stoppingToken);
                        _logger.LogInformation("Scheduled job {Job} completed", job.Name);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Scheduled job {Job} failed", job.Name);
                    }

                    nextRun[job.Name] = NextOccurrence(job.TimeOfDayUtc, DateTime.UtcNow);
                }

                var wait = nextRun.Values.Min() - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, stoppingToken);
            }
        }

        private static DateTime NextOccurrence(TimeSpan timeOfDayUtc, DateTime fromUtc)
        {
            var candidate = fromUtc.Date + timeOfDayUtc;
            return candidate > fromUtc ? candidate : candidate.AddDays(1);
        }
    }
}
