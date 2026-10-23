using System.Text.Json;
using Kvx.Server.Auth;

namespace Kvx.Tests;

/// <summary>The kvx client commands, each run as its own process against a kvx serving on loopback.</summary>
public sealed class CliTests : IAsyncLifetime
{
    private readonly LiveKvx _kvx = new();

    public Task InitializeAsync() => _kvx.StartAsync();
    public Task DisposeAsync() => _kvx.DisposeAsync().AsTask();

    [Fact]
    public async Task Run_sets_the_identity_and_removes_credentials_that_would_win()
    {
        var result = await LiveKvx.Cli(["run", .. _kvx.ClientSettings, "--", "sh", "-c",
            "echo \"$IDENTITY_ENDPOINT|$IDENTITY_HEADER|${AZURE_CLIENT_SECRET-unset}|$SSL_CERT_DIR\""],
            psi =>
            {
                psi.Environment["AZURE_CLIENT_SECRET"] = "a-service-principal-secret";
                psi.Environment["SSL_CERT_DIR"] = "/etc/ssl/certs";
            });

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal($"{_kvx.Urls}{IdentityEndpoint.Path}|{File.ReadAllText(_kvx.TokenFile).Trim()}|unset|{_kvx.TrustDir}:/etc/ssl/certs",
            result.Stdout.Trim());
        Assert.Contains("unset AZURE_CLIENT_SECRET", result.Stderr);
    }

    [Fact]
    public async Task Run_exits_with_the_apps_exit_code()
    {
        var result = await LiveKvx.Cli(["run", .. _kvx.ClientSettings, "--", "sh", "-c", "exit 7"]);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task Run_reports_a_command_that_cannot_start()
    {
        var result = await LiveKvx.Cli(["run", .. _kvx.ClientSettings, "--", "kvx-no-such-command"]);
        Assert.Equal(127, result.ExitCode);
        Assert.Contains("kvx-no-such-command", result.Stderr);
    }

    [Fact]
    public async Task Run_without_kvx_serving_does_not_start_the_app()
    {
        using var dir = new TempDir();
        var marker = Path.Combine(dir.Path, "app-started");
        var result = await LiveKvx.Cli(["run", $"--urls=http://127.0.0.1:{LiveKvx.FreePort()}",
            $"--Kvx:TokenFile={_kvx.TokenFile}", "--", "touch", marker]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(marker));
        Assert.Contains("start kvx", result.Stderr);
        Assert.Contains("kvx doctor", result.Stderr);
    }

    [Fact]
    public async Task Run_without_a_session_token_does_not_start_the_app()
    {
        using var dir = new TempDir();
        var marker = Path.Combine(dir.Path, "app-started");
        var result = await LiveKvx.Cli(["run", $"--urls={_kvx.Urls}",
            $"--Kvx:TokenFile={Path.Combine(dir.Path, "dev-token")}", "--", "touch", marker]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(marker));
        Assert.Contains("start kvx", result.Stderr);
    }

    [Theory]
    [InlineData("sh")]
    [InlineData("fish")]
    public async Task Env_output_is_accepted_by_the_shell(string shell)
    {
        if (shell == "fish" && !OnPath("fish")) return;   // fish is optional on CI machines
        var env = await LiveKvx.Cli(["env", "--shell", shell == "sh" ? "posix" : "fish", .. _kvx.ClientSettings],
            psi => psi.Environment["AZURE_CLIENT_SECRET"] = "a-service-principal-secret");
        Assert.True(env.ExitCode == 0, env.ToString());

        var script = env.Stdout + "\necho \"$IDENTITY_ENDPOINT|$IDENTITY_HEADER\"\n" +
                     (shell == "sh" ? "echo \"${AZURE_CLIENT_SECRET-unset}\"" : "set -q AZURE_CLIENT_SECRET; or echo unset");
        var result = await LiveKvx.Run(shell, ["-c", script],
            psi => psi.Environment["AZURE_CLIENT_SECRET"] = "a-service-principal-secret");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal([$"{_kvx.Urls}{IdentityEndpoint.Path}|{File.ReadAllText(_kvx.TokenFile).Trim()}", "unset"],
            result.Stdout.Trim().Split('\n'));
    }

    [Fact]
    public async Task Import_maps_keys_reports_the_unmappable_and_skips_unchanged()
    {
        var file = Path.Combine(_kvx.Dir.Path, "secrets.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ConnectionStrings"] = new Dictionary<string, string> { ["Db"] = "Server=db;Password=hunter2" },
            ["ApiKey"] = "k1",
            ["not_mappable"] = "x",
        }));

        var first = await LiveKvx.Cli(["import", "--vault", "app", "--file", file, "--remove-plaintext", .. _kvx.ClientSettings]);
        Assert.Equal(1, first.ExitCode);
        Assert.Contains("ConnectionStrings:Db → ConnectionStrings--Db", first.Stdout);
        Assert.Contains("ApiKey → ApiKey", first.Stdout);
        Assert.Contains("'not_mappable'", first.Stderr);
        Assert.True(File.Exists(file), "the plaintext is kept while anything is left behind");

        File.WriteAllText(file, """{ "ConnectionStrings": { "Db": "Server=db;Password=hunter2" }, "ApiKey": "k2" }""");
        var second = await LiveKvx.Cli(["import", "--vault", "app", "--file", file, "--remove-plaintext", .. _kvx.ClientSettings]);
        Assert.True(second.ExitCode == 0, second.ToString());
        Assert.Contains("1 imported, 1 already in vault", second.Stdout);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Serve_without_a_certificate_refuses_and_says_how_to_get_one()
    {
        using var home = new TempDir();   // a user with no dev cert
        var result = await LiveKvx.Cli([
            $"--urls=https://127.0.0.1:{LiveKvx.FreePort()};http://127.0.0.1:{LiveKvx.FreePort()}",
            $"--Kvx:DataDir={Path.Combine(home.Path, "data")}",
            $"--Kvx:TokenFile={Path.Combine(home.Path, "run", "dev-token")}",
        ], psi => { psi.Environment["HOME"] = home.Path; psi.Environment["DOTNET_CLI_HOME"] = home.Path; });

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("dotnet dev-certs https --trust", result.Stderr);
        Assert.Contains("kvx doctor", result.Stderr);
    }

    [Fact]
    public async Task A_second_server_on_the_same_store_refuses_and_names_the_first()
    {
        using var dir = new TempDir();
        var result = await LiveKvx.Cli([
            $"--urls=https://127.0.0.1:{LiveKvx.FreePort()};http://127.0.0.1:{LiveKvx.FreePort()}",
            $"--Kvx:DataDir={_kvx.DataDir}",
            $"--Kvx:TokenFile={Path.Combine(dir.Path, "dev-token")}",
            .. _kvx.CertificateSettings,
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"in use by another kvx (process {Environment.ProcessId})", result.Stderr);
    }

    private static bool OnPath(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, command)));
}
