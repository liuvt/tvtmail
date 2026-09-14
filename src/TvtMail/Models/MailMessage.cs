using System.ComponentModel.DataAnnotations;

namespace TvtMail.Models;

public sealed class MailMessage
{
    public long Id { get; set; }

    public int MailAccountId { get; set; }
    public MailAccount MailAccount { get; set; } = null!;

    [MaxLength(255)]
    public string Folder { get; set; } = "INBOX";

    public long Uid { get; set; }

    [MaxLength(998)]
    public string? InternetMessageId { get; set; }

    [MaxLength(1200)]
    public string Subject { get; set; } = "(Không có tiêu đề)";

    [MaxLength(500)]
    public string FromName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string FromAddress { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string ToText { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }

    public bool IsSeen { get; set; }
    public bool HasAttachments { get; set; }

    [MaxLength(600)]
    public string Preview { get; set; } = string.Empty;

    public string? BodyText { get; set; }
    public string? BodyHtml { get; set; }
    public DateTime? BodyLoadedAtUtc { get; set; }
}
