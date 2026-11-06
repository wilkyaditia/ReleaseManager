using Microsoft.AspNetCore.DataProtection;

namespace ReleaseManager.Services;

public class SecretService
{
    private readonly IDataProtector _protector;
    public SecretService(IDataProtectionProvider provider) => _protector = provider.CreateProtector("ReleaseManager.Secrets.v1");
    public string Protect(string value) => string.IsNullOrEmpty(value) ? "" : _protector.Protect(value);
    public string Unprotect(string value) => string.IsNullOrEmpty(value) ? "" : _protector.Unprotect(value);
}
