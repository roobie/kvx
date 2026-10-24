using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kvx.Server.Keys;
using Kvx.Server.Names;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx doctor [--setting=value…]`: checks what kvx needs from the machine and says how to fix each thing that is
/// wrong, because those failures otherwise surface as SDK errors that don't name the cause. It is a client like the
/// other commands: it never opens the store and changes nothing.
/// </summary>
public static class DoctorCommand
{
    private enum Status { Ok, Warn, Fail }

    private sealed record Finding(Status Status, string Check, string Detail, string? Fix = null);

    /// <summary>A host under the vault domain that stands in for any vault, so the checks need no real one.</summary>
    private const string ProbeLabel = "kvx-doctor";

    private static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(14);

    public static async Task<int> Execute(string[] args)
    {
        var config = KvxApp.Configuration(args);
        var options = new KvxOptions();
        config.GetSection("Kvx").Bind(options);
        var host = $"{ProbeLabel}.{options.Domain}";
        var serving = (await Serving(args, config["urls"], host)).ToList();

        List<Finding> findings =
        [
            await NameResolution(host, options, serving[0].Status == Status.Ok),
            Certificate(config, host, options.Domain),
            KeyProtector(options),
            .. serving,
        ];

        foreach (var f in findings)
        {
            var status = f.Status switch { Status.Ok => "ok", Status.Warn => "warn", _ => "FAIL" };
            Console.WriteLine($"{status,-5} {f.Check,-16} {f.Detail}");
            if (f.Fix is not null) Console.WriteLine($"{"",-22} fix: {f.Fix}");
        }
        return findings.Any(f => f.Status == Status.Fail) ? 1 : 0;
    }

    private static readonly string HostsFile = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts")
        : "/etc/hosts";

    private static async Task<Finding> NameResolution(string host, KvxOptions options, bool serving)
    {
        const string check = "name resolution";
        var domain = options.Domain;
        var perVault = $"add `127.0.0.1 <vault>.{domain}` to {HostsFile} for each vault";
        var dns = OperatingSystem.IsWindows() ? options.ResolveDns() : null;
        var responder = dns is not null && LoopbackDns.Covers(host);
        var nrpt = dns?.Port == 53
            ? $"once, in an elevated PowerShell: `Add-DnsClientNrptRule -Namespace '.localhost' -NameServers '{dns.Address}'`; " +
              $"kvx then answers vault names while it runs. Where that can't work (Group Policy NRPT rules), {perVault}"
            : perVault;
        var fix = OperatingSystem.IsLinux()
            ? "vault names need the `myhostname` NSS module or systemd-resolved; install it (Debian/Ubuntu: " +
              "`sudo apt install libnss-myhostname`) and put `myhostname` before `dns` on the `hosts:` line of " +
              $"/etc/nsswitch.conf; or {perVault}"
            : responder ? nrpt : perVault;

        var (resolved, detail) = await Resolve(host);
        if (resolved) return new(Status.Ok, check, detail);

        // A hosts-file user names each vault there; the probe name having no line of its own is no fault.
        var listed = HostsNames(domain);
        if (listed.Count > 0 && (await Task.WhenAll(listed.Select(Resolve))).All(r => r.Resolved))
            return new(Status.Warn, check, $"{detail}; the vaults in {HostsFile} resolve ({string.Join(", ", listed)}), a new one needs a line too", fix);

        if (!responder) return new(Status.Fail, check, detail, fix);
        if (await LoopbackDns.AnswersAt(dns!, host, TimeSpan.FromSeconds(2)))
            return new(Status.Fail, check, $"{detail}; kvx answers it on udp {dns}, but Windows doesn't ask kvx", nrpt);
        return serving
            ? new(Status.Fail, check, $"{detail}; kvx serves, but nothing answers DNS on udp {dns} (another program may hold the port)", perVault)
            : new(Status.Warn, check, $"{detail}; on Windows vault names resolve while kvx serves", $"start kvx; if it never resolves, {nrpt}");
    }

    private static async Task<(bool Resolved, string Detail)> Resolve(string host)
    {
        IPAddress[] addresses;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return (false, $"{host} does not resolve ({e.Message})");
        }
        var shown = string.Join(", ", addresses.Select(a => a.ToString()));
        return addresses.Length > 0 && addresses.All(IPAddress.IsLoopback)
            ? (true, $"{host} → {shown}")
            : (false, $"{host} resolves to {shown}, not to loopback");
    }

    /// <summary>The names under the vault domain that the OS hosts file lists.</summary>
    private static List<string> HostsNames(string domain)
    {
        string[] lines;
        try { lines = File.ReadAllLines(HostsFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        return lines
            .Select(l => l.Split('#')[0].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length > 1)
            .SelectMany(fields => fields.Skip(1))
            .Where(name => name.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Finding Certificate(IConfiguration config, string host, string domain)
    {
        const string check = "certificate";
        var (found, missing) = ServedCertificate.Find(config);
        if (found is null) return new(Status.Fail, check, missing!.Problem, missing.Fix);

        var (cert, isDevCert, what) = found;
        using var _ = cert;
        var renew = isDevCert
            ? "run `dotnet dev-certs https --clean`, then `dotnet dev-certs https --trust`"
            : "replace the certificate configured under Kestrel:Certificates:Default";
        var now = DateTime.Now;

        if (now < cert.NotBefore || now > cert.NotAfter)
            return new(Status.Fail, check, $"{what} is valid from {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}, not now", renew);

        if (!cert.MatchesHostname(host))
        {
            var names = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(e => e.EnumerateDnsNames());
            return new(Status.Fail, check, $"{what} does not cover *.{domain} (it covers {string.Join(", ", names)})",
                isDevCert
                    ? $"set Kvx:Domain to a domain the dev cert covers (dev.localhost), or configure a certificate for *.{domain} under Kestrel:Certificates:Default"
                    : $"configure a certificate that covers *.{domain}, or set Kvx:Domain to the domain it covers");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!chain.Build(cert))
        {
            var why = string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()).Distinct());
            return new(Status.Fail, check, $"{what} is not trusted by .NET apps of this user ({why})",
                isDevCert ? "run `dotnet dev-certs https --trust`" : $"trust its issuer ({cert.Issuer}) for your user");
        }

        return cert.NotAfter - now < ExpiryWarning
            ? new(Status.Warn, check, $"{what} expires on {cert.NotAfter:yyyy-MM-dd}", renew)
            : new(Status.Ok, check, $"{what} covers *.{domain}, is trusted, and is valid until {cert.NotAfter:yyyy-MM-dd}");
    }

    private static Finding KeyProtector(KvxOptions options)
    {
        const string check = "key protector";
        var name = KeyProtectors.Name(options);
        try
        {
            var protector = KeyProtectors.Create(options);
            var probe = RandomNumberGenerator.GetBytes(32);
            return protector.Unwrap(protector.Wrap(probe)).AsSpan().SequenceEqual(probe)
                ? new(Status.Ok, check, $"{name} wraps and unwraps a key")
                : new(Status.Fail, check, $"{name} unwrapped a different key than it wrapped");
        }
        catch (KeyProtectorException e)
        {
            var native = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();
            return new(Status.Fail, check, e.Message, name switch
            {
                KeyProtectors.SystemdCreds when OperatingSystem.IsLinux() =>
                    $"kvx needs `systemd-creds --user`, from systemd 256 or later; this machine has {SystemdVersion()}",
                KeyProtectors.Keychain when OperatingSystem.IsMacOS() =>
                    "unlock the login keychain (`security unlock-keychain`); a session without one (SSH, CI) must unlock it first",
                KeyProtectors.Dpapi when OperatingSystem.IsWindows() =>
                    "DPAPI needs a loaded user profile; a store wrapped before an admin reset of your password cannot be opened again",
                _ when native => "leave Kvx:KeyProtector empty to use this OS's protector",
                _ => "this OS has no key protector kvx can use",
            });
        }
    }

    private static string SystemdVersion()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("systemd-creds", "--version")
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            var first = p.StandardOutput.ReadLine();
            p.WaitForExit();
            return string.IsNullOrWhiteSpace(first) ? "an unknown version" : first.Trim();
        }
        catch (Win32Exception) { return "no systemd-creds"; }
    }

    private static async Task<IEnumerable<Finding>> Serving(string[] args, string? urls, string host)
    {
        var (client, error) = await KvxClient.Connect(args);
        if (client is null) return [new(Status.Warn, "serving", error!)];
        return [new(Status.Ok, "serving", $"kvx answers at {client.Endpoint}"), await Handshake(urls, host)];
    }

    /// <summary>
    /// A TLS handshake with the https listener under a vault's name, as an app would make it. The connection goes
    /// straight to the listener so that this checks what kvx serves, apart from name resolution.
    /// </summary>
    private static async Task<Finding> Handshake(string? urls, string host)
    {
        const string check = "https";
        var listener = KvxClient.ListenEndpoint(urls, "https");
        if (listener is null)
            return new(Status.Fail, check, $"urls '{urls}' has no https:// listener for vaults", "add an https:// url to urls");

        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(listener.Host, listener.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var url = $"https://{host}:{listener.Port}/";
        try
        {
            using var response = await http.GetAsync(url);
            return new(Status.Ok, check, $"{url} completes a trusted TLS handshake");
        }
        catch (HttpRequestException e) when (e.InnerException is AuthenticationException tls)
        {
            return new(Status.Fail, check, $"{url}: {tls.Message}", "see the certificate check");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return new(Status.Fail, check, $"{url}: {e.Message}");
        }
    }
}
