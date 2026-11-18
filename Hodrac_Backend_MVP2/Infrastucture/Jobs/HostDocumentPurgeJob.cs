using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Daily. NDPA data minimisation: deletes a host's verification documents
/// (<see cref="HostDocument"/> rows, i.e. the files) 90 days after the
/// verification decision (Verified or Rejected; decision time = VerifiedAt,
/// set for both) and clears <see cref="HostProfile.IdDocumentStorageKey"/>.
/// Keeps the status, decision date and reviewer. A host who re-submitted
/// (status back to Pending) or whose verification was revoked (Unverified) is
/// not touched. Per-host failures are logged and skipped.
/// </summary>
public class HostDocumentPurgeJob
{
    public static readonly TimeSpan RetainFor = TimeSpan.FromDays(90);

    private static readonly HostVerificationStatus[] Decided =
    {
        HostVerificationStatus.Verified,
        HostVerificationStatus.Rejected
    };

    private readonly HodracDbContext _db;
    private readonly ILogger<HostDocumentPurgeJob> _logger;

    public HostDocumentPurgeJob(HodracDbContext db, ILogger<HostDocumentPurgeJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public record RunResult(int Hosts, int DocumentsDeleted);

    public Task<RunResult> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    public async Task<RunResult> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var cutoff = nowUtc - RetainFor;
        var hostIds = await _db.HostProfiles.AsNoTracking()
            .Where(h => Decided.Contains(h.VerificationStatus)
                && h.VerifiedAt != null && h.VerifiedAt <= cutoff
                && (h.IdDocumentStorageKey != null || _db.HostDocuments.Any(d => d.HostProfileId == h.Id)))
            .Select(h => h.Id)
            .ToListAsync(ct);

        int hosts = 0, documents = 0;
        foreach (var hostId in hostIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                // Re-check inside the transaction: the host may have re-submitted since.
                var cleared = await _db.HostProfiles
                    .Where(h => h.Id == hostId && Decided.Contains(h.VerificationStatus)
                        && h.VerifiedAt != null && h.VerifiedAt <= cutoff)
                    .ExecuteUpdateAsync(s => s.SetProperty(h => h.IdDocumentStorageKey, (string?)null), ct);
                if (cleared == 0)
                {
                    await tx.RollbackAsync(ct);
                    continue;
                }

                documents += await _db.HostDocuments.Where(d => d.HostProfileId == hostId).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
                hosts++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Document purge failed for host {HostProfileId}", hostId);
            }
        }

        _logger.LogInformation("Host document purge: {Documents} document(s) deleted for {Hosts} host(s)", documents, hosts);
        return new RunResult(hosts, documents);
    }
}
