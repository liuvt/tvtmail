using System.ComponentModel.DataAnnotations;

namespace TvtMail.Models;

public sealed class MailAccount
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string EmailAddress { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string ImapHost { get; set; } = string.Empty;

    public int ImapPort { get; set; } = 993;

    public ImapSecurityMode SecurityMode { get; set; } = ImapSecurityMode.SslOnConnect;

    [Required]
    public string EncryptedPassword { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastSyncedAtUtc { get; set; }

    public long? InboxUidValidity { get; set; }

    [MaxLength(1200)]
    public string? LastError { get; set; }

    public ICollection<MailMessage> Messages { get; set; } = new List<MailMessage>();
}
