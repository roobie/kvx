using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Kvx.Server;
using Kvx.Server.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Kvx.Tests;

/// <summary>kvx serving on a real loopback port, plus a way to run the kvx binary's client commands against it.</summary>
public sealed class LiveKvx : IAsyncDisposable
{
    public TempDir Dir { get; } = new();
    public int Port { get; } = FreePort();
    public string TokenFile => Path.Combine(Dir.Path, "run", "dev-token");
    public string Urls => $"http://127.0.0.1:{Port}";

    /// <summary>The settings a client command needs to find this server.</summary>
    public string[] ClientSettings => [$"--urls={Urls}", $"--Kvx:TokenFile={TokenFile}"];

    private Microsoft.AspNetCore.Builder.WebApplication? _app;

    public async Task StartAsync()
    {
        _app = KvxApp.Build([
            $"--urls={Urls}",
            $"--Kvx:DataDir={Path.Combine(Dir.Path, "data")}",
            $"--Kvx:TokenFile={TokenFile}",
        ], b => b.Services.AddSingleton(FakeLogin.Upstream));
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        Dir.Dispose();
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
