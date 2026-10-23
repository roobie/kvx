using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Kvx.Server.Cli;

/// <summary>
/// The certificate kvx's https listener will serve, found the way Kestrel finds it: the configured default
/// certificate if there is one, otherwise the user's ASP.NET Core development certificate.
/// </summary>
internal static class ServedCertificate
{
    /// <summary>The extension `dotnet dev-certs` marks its certificates with; Kestrel looks for the same one.</summary>
    private const string DevCertOid = "1.3.6.1.4.1.311.84.1.1";

    internal sealed record Located(X509Certificate2 Certificate, bool IsDevCert, string Description);

    internal sealed record Missing(string Problem, string Fix);

    public static (Located? Found, Missing? Missing) Find(IConfiguration config)
    {
        var section = config.GetSection("Kestrel:Certificates:Default");
        return section.Exists() ? Configured(section) : DevCert();
    }

    private static (Located?, Missing?) Configured(IConfigurationSection section)
    {
        var path = section["Path"];
        if (!string.IsNullOrEmpty(path))
        {
            if (!File.Exists(path))
                return (null, new($"Kestrel:Certificates:Default:Path {path} does not exist",
                    "point Kestrel:Certificates:Default:Path at the certificate kvx should serve"));
            try
            {
                var cert = Path.GetExtension(path).ToLowerInvariant() is ".pfx" or ".p12"
                    ? X509CertificateLoader.LoadPkcs12FromFile(path, section["Password"])
                    : X509CertificateLoader.LoadCertificateFromFile(path);
                return (new(cert, false, $"the certificate in {path}"), null);
            }
            catch (CryptographicException e)
            {
                return (null, new($"cannot read the certificate in {path}: {e.Message}",
                    "check Kestrel:Certificates:Default:Path and :Password"));
            }
        }

        var subject = section["Subject"];
        if (!string.IsNullOrEmpty(subject))
        {
            var location = Enum.TryParse<StoreLocation>(section["Location"], true, out var l) ? l : StoreLocation.CurrentUser;
            using var store = new X509Store(section["Store"] ?? "My", location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var cert = Newest(store.Certificates.Find(X509FindType.FindBySubjectName, subject, false));
            return cert is null
                ? (null, new($"no certificate for '{subject}' in the {location} {store.Name} store",
                    "install the certificate, or fix Kestrel:Certificates:Default:Subject"))
                : (new(cert, false, $"the certificate for '{subject}' in the {location} {store.Name} store"), null);
        }

        return (null, new("Kestrel:Certificates:Default sets neither Path nor Subject",
            "set Kestrel:Certificates:Default:Path (and :Password), or :Subject"));
    }

    private static (Located?, Missing?) DevCert()
    {
        var none = new Missing("there is no ASP.NET Core development certificate for this user",
            "run `dotnet dev-certs https --trust`");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        try { store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly); }
        catch (CryptographicException) { return (null, none); }

        var cert = Newest(store.Certificates.Where(c => c.Extensions[DevCertOid] is not null && c.HasPrivateKey));
        return cert is null ? (null, none) : (new(cert, true, $"the ASP.NET Core dev cert ({cert.Thumbprint[..8]}…)"), null);
    }

    /// <summary>A currently valid certificate if there is one, else the latest to expire, so an expired one is still reported.</summary>
    private static X509Certificate2? Newest(IEnumerable<X509Certificate2> certificates)
    {
        var now = DateTime.Now;
        return certificates
            .OrderByDescending(c => c.NotBefore <= now && now <= c.NotAfter)
            .ThenByDescending(c => c.NotAfter)
            .FirstOrDefault();
    }
}
