namespace Hodrac_Backend_MVP2.Models;

// ─── Hodrac Host: payments ────────────────────────────────────────────────────
// All money is long kobo (₦1 = 100 kobo). Hodrac never holds traveller money:
// every SeatPayment is a Paystack transaction split at source (host share to
// the host's subaccount, Hodrac's fee as transaction_charge, bearer=subaccount).
// Enums are stored as strings.

public enum SeatPaymentKind
{
    Deposit,
    Instalment,
    Full
}

public enum SeatPaymentStatus
{
    Initialized,
    Success,
    Failed,
    Abandoned,
    Refunded,
    PartiallyRefunded,
    Disputed
}

/// <summary>One row per Paystack transaction attempt. Idempotent on <see cref="Reference"/>.</summary>
public class SeatPayment
{
    public const int MaxReferenceLength = 64;
    public const int MaxChannelLength = 32;
    public const int MaxAuthorizationUrlLength = 1024;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TripPostId { get; set; }
    public TripPost? TripPost { get; set; }

    public Guid TripInterestId { get; set; }
    public TripInterest? TripInterest { get; set; }

    public Guid PayerUserId { get; set; }

    /// <summary>Server-generated, unique (see PaymentReference.New()).</summary>
    public string Reference { get; set; } = string.Empty;

    public SeatPaymentKind Kind { get; set; }
    public int? InstalmentSequence { get; set; }

    /// <summary>Credited to the seat.</summary>
    public long SeatAmountKobo { get; set; }
    public long HodracFeeKobo { get; set; }
    /// <summary>Equals HodracFeeKobo when FeeMode is PassToTraveller, else 0.</summary>
    public long TravellerFeeKobo { get; set; }
    /// <summary>SeatAmountKobo + TravellerFeeKobo: the Paystack amount.</summary>
    public long TotalChargedKobo { get; set; }
    /// <summary>From the verify response.</summary>
    public long? PaystackFeeKobo { get; set; }
    /// <summary>
    /// Total refunded by Paystack (refund webhooks), in kobo of the amount charged.
    /// Refunds reduce the seat money first (Hodrac's fee is non-refundable).
    /// </summary>
    public long RefundedKobo { get; set; }

    // What we initialized with, so ApplyAsync can reject a verify response
    // whose currency or subaccount doesn't match.
    public string Currency { get; set; } = TripPost.DefaultCurrency;
    public string? SubaccountCode { get; set; }

    /// <summary>Paystack checkout page for this attempt, reused while the attempt is fresh.</summary>
    public string? AuthorizationUrl { get; set; }

    public SeatPaymentStatus Status { get; set; } = SeatPaymentStatus.Initialized;
    public string? Channel { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>Raw Paystack verify response (jsonb), for audit.</summary>
    public string? VerifyResponseJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ManualPaymentMethod
{
    Transfer,
    Cash,
    Other,
    // v1 refunds are paid by the host directly and recorded here with a
    // negative AmountKobo (only Refund may be negative; enforced in the service).
    Refund
}

/// <summary>Money the host received (or refunded) outside Hodrac, so the manifest stays complete. No fee.</summary>
public class ManualPayment
{
    public const int MaxNoteLength = 500;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TripInterestId { get; set; }
    public TripInterest? TripInterest { get; set; }

    public long AmountKobo { get; set; }
    public ManualPaymentMethod Method { get; set; } = ManualPaymentMethod.Transfer;
    public string? Note { get; set; }

    public Guid RecordedByUserId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Webhook audit and deduplication (unique on PayloadSha256).</summary>
public class PaymentWebhookEvent
{
    public const string PaystackProvider = "paystack";
    public const int MaxProviderLength = 16;
    public const int MaxEventTypeLength = 64;
    public const int PayloadSha256Length = 64;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Provider { get; set; } = PaystackProvider;
    public string EventType { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string PayloadSha256 { get; set; } = string.Empty;

    /// <summary>Raw webhook body (jsonb).</summary>
    public string Payload { get; set; } = "{}";

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
}

public enum PaymentReminderKind
{
    DueIn3Days,
    DueIn1Day,
    DueToday,
    Overdue2Days,
    SeatHoldExpiring
}

/// <summary>So reminders are never sent twice: unique on (TripInterestId, InstalmentSequence, Kind), nulls equal.</summary>
public class PaymentReminderLog
{
    public const string EmailChannel = "email";
    public const int MaxChannelLength = 16;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TripInterestId { get; set; }
    public TripInterest? TripInterest { get; set; }

    /// <summary>Null for reminders not tied to an instalment (deposit, seat hold).</summary>
    public int? InstalmentSequence { get; set; }

    public PaymentReminderKind Kind { get; set; }
    public string Channel { get; set; } = EmailChannel;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>The annual Operator plan, invoiced by hand in v1 and recorded by an admin.</summary>
public class OperatorPlanPayment
{
    public const int MaxReferenceLength = 100;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HostProfileId { get; set; }
    public HostProfile? HostProfile { get; set; }

    public string Reference { get; set; } = string.Empty;
    public long AmountKobo { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public Guid RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
