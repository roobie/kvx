namespace Kvx.Tests;

/// <summary>
/// `kvx doctor` as its own process. Each failure is induced through settings and the doctor's own environment,
/// so the machine's dev cert, name resolution and keyring are never touched.
/// </summary>
public sealed class DoctorTests : IAsyncLifetime
{
    private readonly LiveKvx _kvx = new();

    public Task InitializeAsync() => _kvx.StartAsync();
    public Task DisposeAsync() => _kvx.DisposeAsync().AsTask();

    /// <summary>Settings for a kvx that is not serving, so only the machine checks decide the outcome.</summary>
    private string[] NotServing(string domain = LiveKvx.Domain) =>
    [
        $"--urls=https://127.0.0.1:{LiveKvx.FreePort()};http://127.0.0.1:{LiveKvx.FreePort()}",
        $"--Kvx:TokenFile={Path.Combine(_kvx.Dir.Path, "no-such-token")}",
        $"--Kvx:Domain={domain}",
    ];

    private Task<LiveKvx.Result> Doctor(IEnumerable<string> settings, Action<System.Diagnostics.ProcessStartInfo>? configure = null) =>
        LiveKvx.Cli(["doctor", .. settings], psi =>
        {
            psi.Environment["SSL_CERT_DIR"] = _kvx.TrustDir;
            configure?.Invoke(psi);
        });

    private static string Line(LiveKvx.Result result, string check) =>
        result.Stdout.Split('\n').FirstOrDefault(l => l.Length > 6 && l[6..].StartsWith(check)) ?? $"<no {check} line>\n{result}";

    [Fact]
    public async Task Everything_is_ok_against_a_serving_kvx()
    {
        var result = await Doctor([.. _kvx.ClientSettings, .. _kvx.CertificateSettings]);

        Assert.True(result.ExitCode == 0, result.ToString());
        foreach (var check in new[] { "name resolution", "certificate", "key protector", "serving", "https" })
            Assert.StartsWith("ok", Line(result, check));
    }

    [Fact]
    public async Task Not_serving_is_a_warning_not_a_failure()
    {
        var result = await Doctor([.. NotServing(), .. _kvx.CertificateSettings]);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.StartsWith("warn", Line(result, "serving"));
        Assert.Contains("start kvx", result.Stdout);
    }

    [Fact]
    public async Task An_unresolvable_domain_fails_with_how_to_resolve_it()
    {
        var result = await Doctor([.. NotServing("kvx-doctor.invalid"), .. _kvx.CertificateSettings]);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("FAIL", Line(result, "name resolution"));
        Assert.Contains("fix:", result.Stdout);
    }

    [Fact]
    public async Task A_user_without_a_dev_cert_is_told_to_create_one()
    {
        using var home = new TempDir();
        var result = await Doctor(NotServing(), psi => { psi.Environment["HOME"] = home.Path; psi.Environment["DOTNET_CLI_HOME"] = home.Path; });

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("FAIL", Line(result, "certificate"));
        Assert.Contains("dotnet dev-certs https --trust", result.Stdout);
    }

    [Fact]
    public async Task An_expired_certificate_fails()
    {
        var pfx = Path.Combine(_kvx.Dir.Path, "expired.pfx");
        LiveKvx.WriteCertificate(pfx, _kvx.TrustDir, LiveKvx.Domain, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
        var result = await Doctor([.. NotServing(), .. LiveKvx.Certificate(pfx)]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not now", Line(result, "certificate"));
    }

    [Fact]
    public async Task A_certificate_that_expires_soon_is_a_warning()
    {
        var pfx = Path.Combine(_kvx.Dir.Path, "soon.pfx");
        LiveKvx.WriteCertificate(pfx, _kvx.TrustDir, LiveKvx.Domain, DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(3));
        var result = await Doctor([.. NotServing(), .. LiveKvx.Certificate(pfx)]);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.StartsWith("warn", Line(result, "certificate"));
    }

    [Fact]
    public async Task A_certificate_for_another_domain_fails()
    {
        var result = await Doctor([.. NotServing("other.localhost"), .. _kvx.CertificateSettings]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"*.{LiveKvx.Domain}", Line(result, "certificate"));
    }

    [Fact]
    public async Task An_untrusted_certificate_fails()
    {
        var result = await Doctor([.. NotServing(), .. _kvx.CertificateSettings], psi => psi.Environment.Remove("SSL_CERT_DIR"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not trusted", Line(result, "certificate"));
    }

    [Fact]
    public async Task An_unknown_key_protector_fails()
    {
        var result = await Doctor([.. NotServing(), .. _kvx.CertificateSettings, "--Kvx:KeyProtector=nope"]);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("FAIL", Line(result, "key protector"));
    }

    [Fact]
    public async Task A_missing_systemd_creds_fails_with_the_version_needed()
    {
        using var empty = new TempDir();
        var dotnet = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
            .Select(d => Path.Combine(d, "dotnet")).First(File.Exists);
        var result = await LiveKvx.Run(dotnet,
            [Path.Combine(AppContext.BaseDirectory, "Kvx.Server.dll"), "doctor", .. NotServing(), .. _kvx.CertificateSettings],
            psi => { psi.Environment["PATH"] = empty.Path; psi.Environment["SSL_CERT_DIR"] = _kvx.TrustDir; });

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("FAIL", Line(result, "key protector"));
        Assert.Contains("systemd 256", result.Stdout);
    }
}
