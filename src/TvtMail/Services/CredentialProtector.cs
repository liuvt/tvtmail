using Microsoft.AspNetCore.DataProtection;

namespace TvtMail.Services;

public sealed class CredentialProtector
{
    private readonly IDataProtector _protector;

    public CredentialProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("TvtMail.MailAccount.Password.v1");
    }

    public string Protect(string value) => _protector.Protect(value);

    public string Unprotect(string value) => _protector.Unprotect(value);
}
