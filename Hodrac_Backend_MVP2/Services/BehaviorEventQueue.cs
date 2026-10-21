using System.Threading.Channels;
using Hodrac_Backend_MVP2.Events;
using MongoDB.Driver;

namespace Hodrac_Backend_MVP2.Services;

// ─── Behavior event queue ─────────────────────────────────────────────────────
// In-process stand-in for the Service Bus described in EventContracts.cs.
// Controllers call Publish(...) and return immediately; BehaviorEventWorker
// drains the queue and runs TagInferenceService against MongoDB, so a slow
// or unavailable Mongo never fails or delays the user's request.
// While Mongo is unreachable the worker keeps retrying the current event with
// backoff, so events buffer in the (bounded, in-memory) queue instead of being
// dropped. They are still lost on process restart — the admin rebuild
// (POST /api/admin/insights/rebuild) recomputes profiles from durable data.

public interface IBehaviorEventQueue
{
    void Publish(WishlistSavedEvent evt);
    void Publish(DestinationClickedEvent evt);
    void Publish(OnboardingCompletedEvent evt);
    void Publish(SearchExecutedEvent evt);
}

public class BehaviorEventQueue : IBehaviorEventQueue
{
    // Bounded so a Mongo outage can't grow memory without limit; when full the
    // oldest events are dropped (personalisation signals are best-effort).
    private readonly Channel<object> _channel = Channel.CreateBounded<object>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<object> Reader => _channel.Reader;

    public void Publish(WishlistSavedEvent evt) => _channel.Writer.TryWrite(evt);
    public void Publish(DestinationClickedEvent evt) => _channel.Writer.TryWrite(evt);
    public void Publish(OnboardingCompletedEvent evt) => _channel.Writer.TryWrite(evt);
    public void Publish(SearchExecutedEvent evt) => _channel.Writer.TryWrite(evt);
}

public class BehaviorEventWorker : BackgroundService
{
    private readonly BehaviorEventQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BehaviorEventWorker> _logger;

    public BehaviorEventWorker(BehaviorEventQueue queue, IServiceScopeFactory scopes, ILogger<BehaviorEventWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await DispatchAsync(evt);
                    if (attempt > 1)
                        _logger.LogInformation("Behavior event {EventType} processed after {Attempts} attempts", evt.GetType().Name, attempt);
                    break;
                }
                catch (Exception ex) when (IsTransient(ex) && !stoppingToken.IsCancellationRequested)
                {
                    // Mongo unreachable: wait and retry the same event (queue buffers the rest).
                    var delay = TimeSpan.FromSeconds(Math.Min(MaxRetryDelay.TotalSeconds, Math.Pow(2, Math.Min(attempt, 6))));
                    if (attempt == 1 || attempt % 10 == 0)
                        _logger.LogWarning(ex, "MongoDB unavailable processing {EventType} (attempt {Attempt}); retrying in {Delay}",
                            evt.GetType().Name, attempt, delay);
                    try { await Task.Delay(delay, stoppingToken); }
                    catch (OperationCanceledException) { return; }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process behavior event {EventType}", evt.GetType().Name);
                    break;
                }
            }
        }
    }

    private async Task DispatchAsync(object evt)
    {
        using var scope = _scopes.CreateScope();
        var inference = scope.ServiceProvider.GetRequiredService<TagInferenceService>();

        switch (evt)
        {
            case WishlistSavedEvent e: await inference.HandleWishlistSavedAsync(e); break;
            case DestinationClickedEvent e: await inference.HandleDestinationClickedAsync(e); break;
            case OnboardingCompletedEvent e: await inference.HandleOnboardingCompletedAsync(e); break;
            case SearchExecutedEvent e: await inference.HandleSearchExecutedAsync(e); break;
        }
    }

    /// <summary>Connectivity-type failures worth retrying (server selection timeout, socket errors).</summary>
    private static bool IsTransient(Exception ex) => ex switch
    {
        TimeoutException => true,
        MongoConnectionException => true,
        MongoException me when me.HasErrorLabel("RetryableWriteError") => true,
        _ => false
    };
}
