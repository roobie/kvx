using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kvx.Tests;

/// <summary>
/// The sample AppHost, run through Aspire's own orchestration, with kvx as the test-built binary and settings that
/// keep it on a temporary store, its own certificate and free ports. Needs *.localhost resolution, like the TLS tests.
/// </summary>
[Trait("Category", "E2E")]
public sealed class AspireTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private readonly LiveKvx _kvx = new();

    public Task InitializeAsync()
    {
        _kvx.PrepareCertificate();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _kvx.DisposeAsync().AsTask();

    [Fact]
    public async Task Starts_kvx_when_none_serves_and_an_unmodified_app_round_trips()
    {
        await using (var run = await Run(worker => worker
                         // What a developer's shell may carry, and would win over kvx unless removed.
                         .WithEnvironment("AZURE_TENANT_ID", "00000000-0000-0000-0000-000000000000")
                         .WithEnvironment("AZURE_CLIENT_ID", "00000000-0000-0000-0000-000000000000")
                         .WithEnvironment("AZURE_CLIENT_SECRET", "a-service-principal-secret")))
        {
            var worker = await run.Finished("worker");
            Assert.True(worker.ExitCode == 0, run.Describe());
            Assert.Contains("sample-greeting = hello from the vault", run.Logs("worker"));

            var curl = await run.Finished("curl");
            Assert.True(curl.ExitCode == 0, run.Describe());
            // Any HTTP status means TLS to the vault host was trusted; curl prints 000 when it never got one.
            Assert.Contains(run.Logs("curl").Split('\n'), l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^[1-5]\d\d$"));

            Assert.DoesNotContain("using the kvx already serving", run.Logs("kvx"));
        }

        // The AppHost's kvx stopped with it, so the store is free again.
        await _kvx.StartAsync();
    }

    [Fact]
    public async Task Uses_the_kvx_already_serving_and_its_imported_secrets()
    {
        await _kvx.StartAsync();
        var file = Path.Combine(_kvx.Dir.Path, "secrets.json");
        File.WriteAllText(file, """{ "ApiKey": "k1-imported" }""");
        var import = await LiveKvx.Cli(["import", "--vault", "secrets", "--file", file, .. _kvx.ClientSettings]);
        Assert.True(import.ExitCode == 0, import.ToString());

        await using (var run = await Run())
        {
            var worker = await run.Finished("worker");
            Assert.True(worker.ExitCode == 0, run.Describe());
            Assert.Contains("ApiKey: 11 characters", run.Logs("worker"));
            Assert.Contains("using the kvx already serving this store", run.Logs("kvx"));
        }

        // The developer's kvx outlives the AppHost.
        var env = await LiveKvx.Cli(["env", "--shell", "posix", .. _kvx.ClientSettings]);
        Assert.True(env.ExitCode == 0, env.ToString());
    }

    [Fact]
    public void Publish_mode_is_untouched()
    {
        using var dir = new TempDir();
        string[] publish = ["--operation", "publish", "--publisher", "manifest", "--output-path", dir.Path];

        var plain = DistributedApplication.CreateBuilder(publish);
        var expected = plain.AddAzureKeyVault("secrets").Resource;

        var withKvx = DistributedApplication.CreateBuilder(publish);
        var actual = withKvx.AddAzureKeyVault("secrets").RunAsKvx().Resource;

        Assert.False(actual.IsEmulator());
        Assert.Equal(expected.Annotations.Select(a => a.GetType()), actual.Annotations.Select(a => a.GetType()));
        Assert.Equal(plain.Resources.Select(r => r.Name), withKvx.Resources.Select(r => r.Name));
    }

    private async Task<AppRun> Run(Action<IResourceBuilder<ProjectResource>>? configureWorker = null)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Kvx_Aspire_Sample_AppHost>(
        [
            $"--Kvx:Command={Path.Combine(AppContext.BaseDirectory, "Kvx.Server")}",
            .. _kvx.ServerSettings.Select((s, i) => $"--Kvx:Settings:{i}={s}"),
        ]);
        configureWorker?.Invoke(builder.CreateResourceBuilder<ProjectResource>("worker"));
        // Windows' curl (Schannel) refuses a CA-issued certificate whose revocation it can't check. The test CA has no
        // revocation list, while the dev cert a user has is self-signed, which Schannel doesn't check.
        if (OperatingSystem.IsWindows() && LiveKvx.TestCa is not null)
            builder.CreateResourceBuilder<ExecutableResource>("curl").WithArgs("--ssl-no-revoke");
        var log = new CollectingLoggerProvider();
        builder.Services.AddLogging(l => l.AddProvider(log));
        var app = await builder.BuildAsync();
        var run = new AppRun(app, log);
        try
        {
            await app.StartAsync().WaitAsync(Timeout);
        }
        catch (Exception e)
        {
            throw new Xunit.Sdk.XunitException($"AppHost did not start: {e.Message}\n{run.Describe()}");
        }
        return run;
    }

    /// <summary>The AppHost's log, which carries every resource's output under the category "….Resources.&lt;name&gt;".</summary>
    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, string Line)> Lines { get; } = new();
        public ILogger CreateLogger(string category) => new Collector(category, Lines);
        public void Dispose() { }

        private sealed class Collector(string category, ConcurrentQueue<(string, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState, Exception?, string> format)
            {
                if (IsEnabled(level)) lines.Enqueue((category, $"{format(state, e)}{(e is null ? "" : $"\n{e}")}"));
            }
        }
    }

    /// <summary>A started AppHost, its log, and every resource's last state.</summary>
    private sealed class AppRun : IAsyncDisposable
    {
        private readonly DistributedApplication _app;
        private readonly CollectingLoggerProvider _log;
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentDictionary<string, string> _states = new();

        /// <summary>Every resource's last state, then the whole AppHost log, for a failure message.</summary>
        public string Describe() =>
            string.Join('\n', _states.Select(s => $"{s.Key}: {s.Value}")) + "\n--- AppHost log ---\n" +
            string.Join('\n', _log.Lines.Select(l => $"{l.Category}: {l.Line}"));

        public AppRun(DistributedApplication app, CollectingLoggerProvider log)
        {
            _app = app;
            _log = log;
            var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var e in notifications.WatchAsync(_stop.Token))
                        _states[e.Resource.Name] = $"{e.Snapshot.State?.Text} health={e.Snapshot.HealthStatus} exit={e.Snapshot.ExitCode}";
                }
                catch (OperationCanceledException) { }
            });
        }

        public async Task<CustomResourceSnapshot> Finished(string resource)
        {
            var notifications = _app.Services.GetRequiredService<ResourceNotificationService>();
            var e = await notifications.WaitForResourceAsync(resource,
                e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text), _stop.Token).WaitAsync(Timeout);
            await Task.Delay(500);   // the last lines may trail the state change
            return e.Snapshot;
        }

        /// <summary>A resource's output, without the line number and timestamp the AppHost log adds.</summary>
        public string Logs(string resource) => string.Join('\n', _log.Lines
            .Where(l => l.Category.EndsWith($".Resources.{resource}"))
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Line, @"^\d+: \S+ ", "")));

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _stop.CancelAsync();
        }
    }
}
