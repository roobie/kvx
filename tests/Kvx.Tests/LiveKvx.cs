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

    public async Task StartAsync()
    {
        WriteCertificate(CertFile, TrustDir, Domain, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        _app = KvxApp.Build([
            .. ClientSettings,
            $"--Kvx:DataDir={DataDir}",
            .. CertificateSettings,
        ], b => b.Services.AddSingleton(FakeLogin.Upstream));
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        Dir.Dispose();
    }

    private const string CertPassword = "test";

    /// <summary>A self-signed server certificate for *.<paramref name="domain"/> as a .pfx, and its PEM in <paramref name="trustDir"/>.</summary>
    public static void WriteCertificate(string pfx, string trustDir, string domain, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={domain}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName($"*.{domain}");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, CertPassword));
        Directory.CreateDirectory(trustDir);
        File.WriteAllText(Path.Combine(trustDir, $"{Path.GetFileNameWithoutExtension(pfx)}.pem"), cert.ExportCertificatePem());
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
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        configure?.Invoke(psi);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new Result(p.ExitCode, await stdout, await stderr);
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
