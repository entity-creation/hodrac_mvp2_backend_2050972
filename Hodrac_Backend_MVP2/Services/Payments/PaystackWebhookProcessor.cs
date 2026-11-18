using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hodrac_Backend_MVP2.Data;
using Hodrac_Backend_MVP2.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hodrac_Backend_MVP2.Services.Payments;

public enum WebhookOutcome
{
    /// <summary>Recorded (and processed, or its processing error recorded).</summary>
    Accepted,
    /// <summary>The exact same payload was already received: ignored.</summary>
    Duplicate,
    /// <summary>The (correctly signed) body isn't a Paystack event object.</summary>
    InvalidPayload
}

public enum ReprocessOutcome
{
    NotFound,
    /// <summary>Already processed without error (or being reprocessed right now): nothing re-run.</summary>
    AlreadyProcessed,
    Processed,
    /// <summary>Re-run, but processing failed again (the event's Error says why).</summary>
    Failed
}

/// <summary>
/// Handles a Paystack webhook whose signature the controller already checked.
/// Every event is stored in PaymentWebhookEvents (deduplicated on the SHA-256
/// of the raw body), then processed inline:
/// <list type="bullet">
/// <item><c>charge.success</c>: re-verify with Paystack, then PaymentService.ApplyAsync.</item>
/// <item><c>refund.processed</c>: Refunded / PartiallyRefunded by amount, then recompute.
///   <c>refund.pending</c>, <c>refund.processing</c>, <c>refund.failed</c> are recorded only.</item>
/// <item><c>charge.dispute.create</c>: Disputed, host + admins alerted.
///   <c>charge.dispute.remind</c>: admins reminded. <c>charge.dispute.resolve</c>: see ResolveDisputeAsync.</item>
/// </list>
/// Processing failures are written to the event's Error and the webhook still
/// gets a 200: a missed charge.success is caught by the reconcile job (which
/// verifies every Initialized payment older than 20 minutes) and failed events
/// are listed by the admin webhook audit.
/// </summary>
public class PaystackWebhookProcessor
{
    public const int MaxBodyBytes = 1024 * 1024;

    private readonly HodracDbContext _db;
    private readonly IPaystackClient _paystack;
    private readonly PaymentService _payments;
    private readonly ILogger<PaystackWebhookProcessor> _logger;

    public PaystackWebhookProcessor(
        HodracDbContext db,
        IPaystackClient paystack,
        PaymentService payments,
        ILogger<PaystackWebhookProcessor> logger)
    {
        _db = db;
        _paystack = paystack;
        _payments = payments;
        _logger = logger;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> body) =>
        Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    // The event type and data object of a Paystack event body, or false if it isn't one.
    private static bool TryParseEvent(ReadOnlyMemory<byte> body, out string eventType, out JsonElement data)
    {
        eventType = string.Empty;
        data = default;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(ev.GetString()))
                return false;
            eventType = ev.GetString()!;
            data = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? d.Clone() : default;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<WebhookOutcome> HandleAsync(byte[] rawBody, CancellationToken ct = default)
    {
        if (!TryParseEvent(rawBody, out var eventType, out var data))
            return WebhookOutcome.InvalidPayload;

        var reference = ReferenceOf(eventType, data);
        var evt = new PaymentWebhookEvent
        {
            Provider = PaymentWebhookEvent.PaystackProvider,
            EventType = Truncate(eventType, PaymentWebhookEvent.MaxEventTypeLength)!,
            Reference = Truncate(reference, SeatPayment.MaxReferenceLength),
            PayloadSha256 = Sha256Hex(rawBody),
            Payload = Encoding.UTF8.GetString(rawBody),
        };

        _db.ChangeTracker.Clear();
        _db.PaymentWebhookEvents.Add(evt);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            _logger.LogInformation("Duplicate Paystack webhook {EventType} {Reference} ignored", eventType, reference);
            return WebhookOutcome.Duplicate;
        }

        await ProcessAndRecordAsync(evt.Id, eventType, reference, data, ct);
        return WebhookOutcome.Accepted;
    }

    /// <summary>
    /// Admin "reprocess" of a stored event (GET /api/admin/payment-events?unprocessed=true).
    /// Only events that aren't processed yet (ProcessedAt null) are re-run; the
    /// event is claimed first (ProcessedAt set, conditional update) so two
    /// concurrent reprocesses can't both run it. A claim left behind by a crash
    /// (ProcessedAt and Error both set) can be retaken after 5 minutes.
    /// Money handlers are idempotent on the reference (ApplyAsync credits once).
    /// </summary>
    public async Task<ReprocessOutcome> ReprocessAsync(Guid eventId, CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        var evt = await _db.PaymentWebhookEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (evt is null) return ReprocessOutcome.NotFound;

        var claimedAt = DateTime.UtcNow;
        var staleClaim = claimedAt.AddMinutes(-5);
        var claimed = await _db.PaymentWebhookEvents
            .Where(e => e.Id == eventId
                && (e.ProcessedAt == null || (e.Error != null && e.ProcessedAt < staleClaim)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ProcessedAt, (DateTime?)claimedAt)
                // Keeps the event in the unprocessed audit list if this run dies mid-way.
                .SetProperty(e => e.Error, e => e.Error ?? "Reprocessing interrupted."), ct);
        if (claimed == 0) return ReprocessOutcome.AlreadyProcessed;

        if (!TryParseEvent(Encoding.UTF8.GetBytes(evt.Payload), out var eventType, out var data))
        {
            await RecordAsync(eventId, "Stored payload isn't a Paystack event.");
            return ReprocessOutcome.Failed;
        }

        var error = await ProcessAndRecordAsync(eventId, eventType, ReferenceOf(eventType, data), data, ct);
        return error is null ? ReprocessOutcome.Processed : ReprocessOutcome.Failed;
    }

    // Runs the handler and records the outcome on the event: ProcessedAt on
    // success, else ProcessedAt null and the Error. Returns the error (or null).
    private async Task<string?> ProcessAndRecordAsync(
        Guid eventId, string eventType, string? reference, JsonElement data, CancellationToken ct)
    {
        string? error = null;
        try
        {
            error = await ProcessAsync(eventType, reference, data, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Paystack webhook {EventType} {Reference} failed", eventType, reference);
            error = ex is PaystackException pe ? $"Paystack: {pe.Message}" : ex.Message;
        }

        await RecordAsync(eventId, error);
        return error;
    }

    private Task RecordAsync(Guid eventId, string? error)
    {
        var now = DateTime.UtcNow;
        return _db.PaymentWebhookEvents
            .Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ProcessedAt, error == null ? now : (DateTime?)null)
                .SetProperty(e => e.Error, error), CancellationToken.None);
    }

    /// <summary>Returns null when processed, else the reason it couldn't be.</summary>
    private async Task<string?> ProcessAsync(string eventType, string? reference, JsonElement data, CancellationToken ct)
    {
        switch (eventType)
        {
            case "charge.success":
            {
                if (reference is null) return "No reference in payload.";
                if (!await _db.SeatPayments.AnyAsync(p => p.Reference == reference, ct))
                    return null; // not a seat payment (nothing of ours to apply)
                // Never trust the webhook body for money: ask Paystack.
                var verified = await _paystack.VerifyAsync(reference, ct);
                var result = await _payments.ApplyAsync(verified, ct);
                return result.Outcome == ApplyOutcome.Mismatch ? "Mismatch: not credited (admins alerted)." : null;
            }

            case "refund.processed":
            {
                if (reference is null) return "No transaction reference in payload.";
                var amount = GetLong(data, "amount");
                if (amount is null or <= 0) return "No refund amount in payload.";
                var result = await _payments.ApplyRefundAsync(reference, amount.Value, ct);
                return result.Outcome == ApplyOutcome.NotPaid ? $"Refund ignored: payment is {result.Status}." : null;
            }

            case "refund.pending":
            case "refund.processing":
            case "refund.failed":
                // Nothing moves until refund.processed; recorded for the audit.
                if (eventType == "refund.failed")
                    _logger.LogWarning("Paystack refund failed for {Reference}", reference);
                return null;

            case "charge.dispute.create":
            {
                if (reference is null) return "No transaction reference in payload.";
                await _payments.MarkDisputedAsync(reference, ct);
                return null;
            }

            case "charge.dispute.remind":
                if (reference is not null)
                    await _payments.EmailAdminsAsync(
                        "Payment dispute reminder",
                        new List<string> { $"Paystack sent a reminder for the open dispute on payment {reference}. Respond before the due date." },
                        ct);
                return null;

            case "charge.dispute.resolve":
            {
                if (reference is null) return "No transaction reference in payload.";
                await _payments.ResolveDisputeAsync(reference, GetString(data, "resolution"), ct);
                return null;
            }

            default:
                return null; // other events (transfers, subscriptions, ...) are only recorded
        }
    }

    /// <summary>
    /// The transaction reference: <c>data.reference</c> for charge events,
    /// <c>data.transaction_reference</c> for refunds (confirmed in Paystack's
    /// refund docs), <c>data.transaction.reference</c> for disputes.
    /// </summary>
    public static string? ReferenceOf(string eventType, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return null;

        string? FromTransaction() =>
            data.TryGetProperty("transaction", out var t) && t.ValueKind == JsonValueKind.Object ? GetString(t, "reference") : null;

        if (eventType.StartsWith("refund.", StringComparison.Ordinal))
            return GetString(data, "transaction_reference") ?? FromTransaction();
        if (eventType.StartsWith("charge.dispute.", StringComparison.Ordinal))
            return FromTransaction() ?? GetString(data, "transaction_reference");
        return GetString(data, "reference");
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            }
            : null;

    private static long? GetLong(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
