using System;

namespace Hodrac_Backend_MVP2.Models
{
    public enum TripInterestStatus
    {
        Requested,
        Approved,
        Declined,
        Withdrawn,
        // Hosted trips: the host removed an approved traveller who missed an
        // instalment deadline (append only: stored as int).
        Removed
    }

    // Created the moment a user taps "Interested" on a trip post.
    // Stays in "Requested" until the trip author approves or declines it.
    public class TripInterest
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TripPostId { get; set; }
        public TripPost? TripPost { get; set; }

        public Guid RequesterUserId { get; set; }

        public TripInterestStatus Status { get; set; } = TripInterestStatus.Requested;

        // Optional short note the requester can attach, e.g. "I've been to
        // Beijing before, happy to help plan!"
        public string? Message { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }

        // ---- Hodrac Host (hosted trips only; community trips keep the defaults) ----

        public const int TermsTextSha256Length = 64;
        public const int MaxTermsAcceptedIpLength = 45;
        public const int MaxTermsAcceptedUserAgentLength = 512;
        public const int MaxContactPhoneLength = 20;
        public const int MaxRemovedReasonLength = 450;

        public InterestPaymentStatus PaymentStatus { get; set; } = InterestPaymentStatus.NotRequired;

        // Set on approval of a hosted trip; cleared by the first payment.
        public DateTime? SeatHoldExpiresAt { get; set; }

        // Seat money only (no fees): through Hodrac plus host-recorded manual payments.
        public long AmountPaidKobo { get; set; }
        // Seat money through Hodrac (Paystack) only, for the "% through Hodrac" metric.
        public long AmountPaidViaHodracKobo { get; set; }

        // Evidence of the exact terms accepted before paying.
        public DateTime? TermsAcceptedAt { get; set; }
        public int? TermsVersionAccepted { get; set; }
        public string? TermsTextSha256 { get; set; }
        public string? TermsAcceptedIp { get; set; }
        public string? TermsAcceptedUserAgent { get; set; }

        // Optional at request time; the host needs it for WhatsApp.
        public string? ContactPhone { get; set; }

        // TravellerChooses trips: the dates the traveller agreed to at
        // accept-terms, as JSON [{"sequence":1,"dueDate":"2027-01-15"}, ...]
        // (evidence alongside the terms fields; later date changes don't touch it).
        public string? TermsInstalmentDatesJson { get; set; }

        // Set when the host removed the traveller (Status Removed) for a missed payment.
        public string? RemovedReason { get; set; }
        public DateTime? RemovedAt { get; set; }
    }

    public enum InterestPaymentStatus
    {
        NotRequired,
        AwaitingDeposit,
        DepositPaid,
        PartPaid,
        PaidInFull,
        Expired,
        Refunded
    }
}
