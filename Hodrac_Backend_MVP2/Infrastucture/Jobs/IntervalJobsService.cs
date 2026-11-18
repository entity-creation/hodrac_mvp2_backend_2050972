using Hodrac_Backend_MVP2.Infrastructure.Jobs;

namespace Hodrac_Backend_MVP2.Infrastucture.Jobs
{
    /// <summary>
    /// Runs the short-interval jobs (Hodrac Host seat holds and payment
    /// reconcile). Like <see cref="ScheduledJobsService"/>: each run gets its
    /// own DI scope, and a failing run is logged and retried at its next slot
    /// without stopping the others. The first runs start shortly after boot.
    ///
    /// Single-instance assumption: the jobs are idempotent (row locks, unique
    /// reminder logs), so running on two instances is safe but wasteful.
    /// </summary>
    public class IntervalJobsService : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

        private static readonly (string Name, TimeSpan Interval, Func<IServiceProvider, CancellationToken, Task> Run)[] Jobs =
        {
            ("SeatHoldExpiry",    TimeSpan.FromHours(1),     (sp, ct) => sp.GetRequiredService<SeatHoldExpiryJob>().RunAsync(ct)),
            ("PaymentReconcile",  TimeSpan.FromMinutes(30),  (sp, ct) => sp.GetRequiredService<PaymentReconcileJob>().RunAsync(ct)),
        };

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<IntervalJobsService> _logger;

        public IntervalJobsService(IServiceScopeFactory scopes, ILogger<IntervalJobsService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var start = DateTime.UtcNow + InitialDelay;
            var nextRun = Jobs.ToDictionary(j => j.Name, _ => start);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;

                    foreach (var job in Jobs.Where(j => nextRun[j.Name] <= now))
                    {
                        try
                        {
                            using var scope = _scopes.CreateScope();
                            await job.Run(scope.ServiceProvider, stoppingToken);
                        }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogError(ex, "Interval job {Job} failed", job.Name);
                        }

                        nextRun[job.Name] = DateTime.UtcNow + job.Interval;
                    }

                    var wait = nextRun.Values.Min() - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutting down.
            }
        }
    }
}
