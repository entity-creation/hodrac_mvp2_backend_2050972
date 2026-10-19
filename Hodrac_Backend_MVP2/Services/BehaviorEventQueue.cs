using System.Threading.Channels;
using Hodrac_Backend_MVP2.Events;

namespace Hodrac_Backend_MVP2.Services;

// ─── Behavior event queue ─────────────────────────────────────────────────────
// In-process stand-in for the Service Bus described in EventContracts.cs.
// Controllers call Publish(...) and return immediately; BehaviorEventWorker
// drains the queue and runs TagInferenceService against MongoDB, so a slow
// or unavailable Mongo never fails or delays the user's request.

public interface IBehaviorEventQueue
{
    void Publish(WishlistSavedEvent evt);
    void Publish(DestinationClickedEvent evt);
    void Publish(OnboardingCompletedEvent evt);
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var inference = scope.ServiceProvider.GetRequiredService<TagInferenceService>();

                switch (evt)
                {
                    case WishlistSavedEvent e: await inference.HandleWishlistSavedAsync(e); break;
                    case DestinationClickedEvent e: await inference.HandleDestinationClickedAsync(e); break;
                    case OnboardingCompletedEvent e: await inference.HandleOnboardingCompletedAsync(e); break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process behavior event {EventType}", evt.GetType().Name);
            }
        }
    }
}
