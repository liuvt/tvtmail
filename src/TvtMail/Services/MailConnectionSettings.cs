using TvtMail.Models;

namespace TvtMail.Services;

public sealed record MailConnectionSettings(
    string EmailAddress,
    string Username,
    string Password,
    string ImapHost,
    int ImapPort,
    ImapSecurityMode SecurityMode);
