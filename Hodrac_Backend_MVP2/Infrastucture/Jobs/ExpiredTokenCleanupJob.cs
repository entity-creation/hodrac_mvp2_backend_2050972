using Microsoft.EntityFrameworkCore;
using Hodrac_Backend_MVP2.Data;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Runs nightly. Deletes UserRefreshToken rows that are either:
///   - Expired (ExpiresAt in the past), or
///   - Revoked more than 7 days ago (keep recent revocations briefly for audit)
///
/// Without this job, the UserRefreshTokens table grows unboundedly.
/// Register with Hangfire or Azure Functions TimerTrigger.
/// </summary>
public class ExpiredTokenCleanupJob
{
    private readonly HodracDbContext _db;

    public ExpiredTokenCleanupJob(HodracDbContext db) => _db = db;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now              = DateTimeOffset.UtcNow;
        var revocationCutoff = now.AddDays(-7);

        var deleted = await _db.UserRefreshTokens
            .Where(t => t.ExpiresAt < now ||
                       (t.IsRevoked && t.RevokedAt < revocationCutoff))
            .ExecuteDeleteAsync(ct);
    }
}
