using System.ComponentModel.DataAnnotations;
using Hodrac_Backend_MVP2.Models;

namespace Hodrac_Backend_MVP2.DTOs.AdminDtos;

/// <summary>Row in GET /api/admin/newsletter/issues. Status: Draft | Approved | Sending | Sent | Cancelled.</summary>
public record AdminNewsletterIssueSummaryDto(
    Guid Id,
    DateOnly IssueDate,
    string Subject,
    string PreviewText,
    string Status,
    DateTime ScheduledFor,
    DateTime GeneratedAt,
    DateTime? ApprovedAt,
    DateTime? SendStartedAt,
    DateTime? SentAt,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Live delivery counts for an issue.</summary>
public record AdminNewsletterDeliveryCountsDto(int Pending, int Sent, int Failed, int Skipped);

/// <summary>GET /api/admin/newsletter/issues/{id}: everything plus the rendered email (unsubscribe link is a placeholder).</summary>
public record AdminNewsletterIssueDto(
    Guid Id,
    DateOnly IssueDate,
    string Subject,
    string PreviewText,
    string BodyMarkdown,
    string Status,
    DateTime ScheduledFor,
    DateTime GeneratedAt,
    string? ApprovedByUserId,
    DateTime? ApprovedAt,
    DateTime? SendStartedAt,
    DateTime? SentAt,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    AdminNewsletterDeliveryCountsDto Deliveries,
    string PreviewHtml);

/// <summary>Row in GET /api/admin/newsletter/issues/{id}/deliveries. Status: Pending | Sent | Failed | Skipped.</summary>
public record AdminNewsletterDeliveryDto(
    Guid Id,
    string Email,
    string? UserId,
    string Status,
    int Attempts,
    string? LastError,
    DateTime? SentAt);

/// <summary>GET /api/admin/newsletter/audience.</summary>
public record AdminNewsletterAudienceDto(
    int ActiveSubscribers,
    int UnsubscribedSubscribers,
    int OptedInUsers,
    int OptedOutUsers,
    int TotalUniqueRecipients);

/// <summary>POST /api/admin/newsletter/issues/generate. IssueDate defaults to the next send day (Friday).</summary>
public record GenerateNewsletterIssueRequest(DateOnly? IssueDate);

/// <summary>PATCH /api/admin/newsletter/issues/{id}. Omitted fields are unchanged.</summary>
public record UpdateNewsletterIssueRequest(
    [MaxLength(NewsletterIssue.MaxSubjectLength)] string? Subject,
    [MaxLength(NewsletterIssue.MaxPreviewTextLength)] string? PreviewText,
    string? BodyMarkdown);

/// <summary>POST /api/admin/newsletter/issues/{id}/approve. ScheduledFor (UTC, future) overrides IssueDate 10:00 WAT.</summary>
public record ApproveNewsletterIssueRequest(DateTime? ScheduledFor);

/// <summary>POST /api/admin/newsletter/issues/{id}/test.</summary>
public record NewsletterTestSendResultDto(string SentTo);
