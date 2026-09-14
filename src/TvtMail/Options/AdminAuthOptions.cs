namespace TvtMail.Options;

public sealed class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public string Email { get; set; } = "admin@tvtmail.local";
    public string Password { get; set; } = "ChangeMe-Immediately!";
}
