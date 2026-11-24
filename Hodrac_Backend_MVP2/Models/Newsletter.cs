using System.ComponentModel.DataAnnotations;

namespace Hodrac_Backend_MVP2.Models;

/// <summary>
/// Draft = generated (by the Wednesday job or an admin), editable, never sent.
/// Approved = will be sent at <see cref="NewsletterIssue.ScheduledFor"/>.
/// Sending = recipients snapshotted into <see cref="NewsletterDelivery"/>, batches going out.
/// Sent = no Pending deliveries left. Cancelled = will never send (stops a send in progress).
/// </summary>
public enum NewsletterIssueStatus
{
    Draft,
    Approved,
    Sending,
    Sent,
    Cancelled,
}

public enum NewsletterDeliveryStatus
{
    Pending,
    Sent,
    Failed,
    Skipped,
}

/// <summary>One weekly newsletter, keyed by the Friday (WAT) it is meant to go out.</summary>
public class NewsletterIssue
{
    public const int MaxSubjectLength = 150;
    public const int MaxPreviewTextLength = 200;

    public Guid Id { get; set; }

    /// <summary>The send day (Friday by default) this issue belongs to. Unique.</summary>
    public DateOnly IssueDate { get; set; }

    [Required, MaxLength(MaxSubjectLength)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(MaxPreviewTextLength)]
    public string PreviewText { get; set; } = string.Empty;

    /// <summary>Markdown body. Rendered with raw HTML disabled.</summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    public NewsletterIssueStatus Status { get; set; } = NewsletterIssueStatus.Draft;

    /// <summary>UTC. Defaults to IssueDate at the configured send time (10:00 WAT); set on approve.</summary>
    public DateTime ScheduledFor { get; set; }

    /// <summary>UTC. When the content was last auto-generated.</summary>
    public DateTime GeneratedAt { get; set; }

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public DateTime? SendStartedAt { get; set; }
    public DateTime? SentAt { get; set; }

    /// <summary>Recipients snapshotted when sending started.</summary>
    public int RecipientCount { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<NewsletterDelivery> Deliveries { get; set; } = new List<NewsletterDelivery>();
}

/// <summary>One recipient of one issue. Unique on (IssueId, Email), so a recipient is mailed at most once per issue.</summary>
public class NewsletterDelivery
{
    public const int MaxEmailLength = 320;
    public const int MaxLastErrorLength = 500;

    public Guid Id { get; set; }

    public Guid IssueId { get; set; }
    public NewsletterIssue Issue { get; set; } = null!;

    /// <summary>Trimmed, lower-case.</summary>
    [Required, MaxLength(MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    /// <summary>ApplicationUser.Id when the address belongs to an account.</summary>
    [MaxLength(450)]
    public string? UserId { get; set; }

    public NewsletterDeliveryStatus Status { get; set; } = NewsletterDeliveryStatus.Pending;

    public int Attempts { get; set; }

    [MaxLength(MaxLastErrorLength)]
    public string? LastError { get; set; }

    public DateTime? SentAt { get; set; }
}
