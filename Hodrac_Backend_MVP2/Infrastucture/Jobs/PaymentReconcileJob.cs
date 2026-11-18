using System.Net;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Hodrac_Backend_MVP2.Services.Payments;
using Microsoft.EntityFrameworkCore;

namespace Hodrac_Backend_MVP2.Infrastructure.Jobs;

/// <summary>
/// Every 30 minutes: catches missed Paystack webhooks. Hosted trips only.
/// Each <see cref="SeatPaymentStatus.Initialized"/> payment created more than
/// 20 minutes ago is verified with Paystack and handed to
/// <see cref="PaymentService.ApplyAsync"/> (the only place money state
/// changes; idempotent on the reference, so a webhook that already applied it
/// is a no-op): success is credited, an explicit "failed" marks it Failed.
/// An attempt still not paid 24 hours after it was created is marked
/// Abandoned (a later success on it is still credited by the webhook).
/// Paystack errors are logged per payment and the run continues.
/// </summary>
public class PaymentReconcileJob
{
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromHours(24);
    public const int BatchSize = 200;

    private readonly HodracDbContext _db;
    private readonly IPaystackClient _paystack;
    private readonly PaymentService _payments;
    private readonly ILogger<PaymentReconcileJob> _logger;

    public PaymentReconcileJob(HodracDbContext db, IPaystackClient paystack, PaymentService payments, ILogger<PaymentReconcileJob> logger)
    {
        _db = db;
        _paystack = paystack;
        _payments = payments;
        _logger = logger;
    }

    public record RunResult(int Checked, int Applied, int Failed, int Abandoned, int Errors);

    public Task<RunResult> RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    public async Task<RunResult> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var olderThan = nowUtc - MinAge;
        var pending = await _db.SeatPayments.AsNoTracking()
            .Where(p => p.Status == SeatPaymentStatus.Initialized
                && p.CreatedAt <= olderThan
                && p.TripPost!.IsHostedTrip)
            .OrderBy(p => p.CreatedAt)
            .Take(BatchSize)
            .Select(p => new { p.Reference, p.CreatedAt })
            .ToListAsync(ct);

        int applied = 0, failed = 0, abandoned = 0, errors = 0;
        foreach (var payment in pending)
        {
            ct.ThrowIfCancellationRequested();
            var stale = payment.CreatedAt <= nowUtc - AbandonAfter;
            try
            {
                PaystackTransaction verified;
                try
                {
                    verified = await _paystack.VerifyAsync(payment.Reference, ct);
                }
                catch (PaystackException ex) when (ex.StatusCode == HttpStatusCode.NotFound && stale)
                {
                    // Paystack never saw this attempt (checkout page never opened).
                    if (await AbandonAsync(payment.Reference, nowUtc, ct)) abandoned++;
                    continue;
                }

                var result = await _payments.ApplyAsync(verified, ct);
                switch (result.Outcome)
                {
                    case ApplyOutcome.Applied:
                        applied++;
                        _logger.LogInformation("Reconcile credited payment {Reference} (missed webhook)", payment.Reference);
                        break;
                    case ApplyOutcome.Mismatch:
                        failed++;
                        break;
                    case ApplyOutcome.NotPaid when result.Status == SeatPaymentStatus.Failed:
                        failed++;
                        break;
                    case ApplyOutcome.NotPaid when stale:
                        if (await AbandonAsync(payment.Reference, nowUtc, ct)) abandoned++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                _logger.LogWarning(ex, "Reconcile failed for payment {Reference}; will retry next run", payment.Reference);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (pending.Count > 0)
            _logger.LogInformation(
                "Payment reconcile: {Checked} checked, {Applied} credited, {Failed} failed, {Abandoned} abandoned, {Errors} errors",
                pending.Count, applied, failed, abandoned, errors);
        return new RunResult(pending.Count, applied, failed, abandoned, errors);
    }

    // Conditional update: only an attempt that is still Initialized (a webhook
    // may have credited it a moment ago) becomes Abandoned.
    private async Task<bool> AbandonAsync(string reference, DateTime nowUtc, CancellationToken ct) =>
        await _db.SeatPayments
            .Where(p => p.Reference == reference && p.Status == SeatPaymentStatus.Initialized)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, SeatPaymentStatus.Abandoned)
                .SetProperty(p => p.UpdatedAt, nowUtc), ct) > 0;
}
