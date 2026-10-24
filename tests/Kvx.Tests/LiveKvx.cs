using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kvx.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Kvx.Tests;

/// <summary>
/// kvx serving https and http on real loopback ports, plus a way to run the kvx binary's client commands against it.
/// It serves its own certificate for its own domain, so tests neither need nor touch the machine's dev cert,
/// and that certificate sits in the trust directory `kvx run` passes on, as `dotnet dev-certs https --trust` would.
/// </summary>
public sealed class LiveKvx : IAsyncDisposable
{
    public const string Domain = "kvxtest.localhost";

    public TempDir Dir { get; } = new();
    public int Port { get; } = FreePort();
    public int HttpsPort { get; } = FreePort();
    public string TokenFile => Path.Combine(Dir.Path, "run", "dev-token");
    public string Urls => $"http://127.0.0.1:{Port}";
    public string TrustDir => Path.Combine(Dir.Path, "trust");
    public string DataDir => Path.Combine(Dir.Path, "data");
    private string CertFile => Path.Combine(Dir.Path, "kvx.pfx");

    /// <summary>The settings a client command needs to find this server and pass its trust on.</summary>
    public string[] ClientSettings =>
    [
        $"--urls=https://127.0.0.1:{HttpsPort};{Urls}",
        $"--Kvx:TokenFile={TokenFile}",
        $"--Kvx:Domain={Domain}",
        $"--Kvx:DevCertTrustDir={TrustDir}",
    ];

    public string VaultUri(string vault) => $"https://{vault}.{Domain}:{HttpsPort}";

    private Microsoft.AspNetCore.Builder.WebApplication? _app;

    /// <summary>The settings that make kvx serve this server's certificate.</summary>
    public string[] CertificateSettings => Certificate(CertFile);

    public static string[] Certificate(string pfx) =>
        [$"--Kestrel:Certificates:Default:Path={pfx}", $"--Kestrel:Certificates:Default:Password={CertPassword}"];

    /// <summary>The certificate this server serves, for a kvx started elsewhere with <see cref="ServerSettings"/>.</summary>
    public void PrepareCertificate() =>
        WriteCertificate(CertFile, TrustDir, Domain, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));

    public async Task StartAsync()
    {
        PrepareCertificate();
        _app = KvxApp.Build(ServerSettings, b => b.Services.AddSingleton(FakeLogin.Upstream));
        await _app.StartAsync();
    }

    /// <summary>The settings a second kvx process needs to serve exactly what this one serves.</summary>
    public string[] ServerSettings => [.. ClientSettings, $"--Kvx:DataDir={DataDir}", .. CertificateSettings];

    /// <summary>Stops serving and releases the store, as a developer stopping their kvx does.</summary>
    public async Task StopAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Dir.Dispose();
    }

    private const string CertPassword = "test";

    /// <summary>
    /// The CA that CI trusts machine-wide, named by KVX_TEST_CA (a .pfx): on Windows and macOS trust lives only in the OS
    /// store, which a test can't change without admin rights or a prompt. Unset, as on Linux, certificates are self-signed
    /// and the trust directory is enough.
    /// </summary>
    private static readonly X509Certificate2? TestCa =
        Environment.GetEnvironmentVariable("KVX_TEST_CA") is { Length: > 0 } path
            ? X509CertificateLoader.LoadPkcs12FromFile(path, CertPassword)
            : null;

    /// <summary>
    /// A server certificate for *.<paramref name="domain"/> as a .pfx, and its issuer's PEM in <paramref name="trustDir"/>.
    /// Issued by the test CA when there is one, unless <paramref name="selfSigned"/>, which keeps it untrusted everywhere.
    /// </summary>
    public static void WriteCertificate(string pfx, string trustDir, string domain, DateTimeOffset notBefore, DateTimeOffset notAfter,
        bool selfSigned = false)
    {
        var issuer = selfSigned ? null : TestCa;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={domain}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName($"*.{domain}");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        if (issuer is not null)
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
        using var cert = issuer is null
            ? request.CreateSelfSigned(notBefore, notAfter)
            : request.Create(issuer, notBefore, notAfter, RandomNumberGenerator.GetBytes(16)).CopyWithPrivateKey(key);

        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, CertPassword));
        Directory.CreateDirectory(trustDir);
        File.WriteAllText(Path.Combine(trustDir, $"{Path.GetFileNameWithoutExtension(pfx)}.pem"), (issuer ?? cert).ExportCertificatePem());
    }

    /// <summary>Runs `kvx args…` as its own process, the way a developer does.</summary>
    public static Task<Result> Cli(IEnumerable<string> args, Action<ProcessStartInfo>? configure = null) =>
        Run("dotnet", [Path.Combine(AppContext.BaseDirectory, "Kvx.Server.dll"), .. args], configure);

    public static async Task<Result> Run(string file, IEnumerable<string> args, Action<ProcessStartInfo>? configure = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,   // what kvx writes to a pipe on every OS
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        configure?.Invoke(psi);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        // A command that should exit but serves instead would otherwise hold the whole run until CI kills it.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            p.Kill(entireProcessTree: true);
            await p.WaitForExitAsync();
            throw new TimeoutException($"`{file} {string.Join(' ', args)}` did not exit within 60 s.\n{await stdout}\n{await stderr}");
        }
        return new Result(p.ExitCode, await stdout, await stderr);
    }

    /// <summary>`kvx args…` left running, for commands that serve; the caller kills it.</summary>
    public static Process Start(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in (string[])[Path.Combine(AppContext.BaseDirectory, "Kvx.Server.dll"), .. args]) psi.ArgumentList.Add(a);
        return Process.Start(psi)!;
    }

    /// <summary>Reads <paramref name="p"/>'s stdout until a line contains <paramref name="text"/>; fails after a timeout.</summary>
    public static async Task WaitForOutput(Process p, string text, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        var seen = new System.Text.StringBuilder();
        try
        {
            while (await p.StandardOutput.ReadLineAsync(cts.Token) is { } line)
            {
                seen.AppendLine(line);
                if (line.Contains(text)) return;
            }
        }
        catch (OperationCanceledException) { }
        throw new Xunit.Sdk.XunitException($"'{text}' never appeared; stdout:\n{seen}");
    }

    public sealed record Result(int ExitCode, string Stdout, string Stderr)
    {
        public override string ToString() => $"exit {ExitCode}\nstdout:\n{Stdout}\nstderr:\n{Stderr}";
    }

    public static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }
}
