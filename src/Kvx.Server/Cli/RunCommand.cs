using System.ComponentModel;
using System.Diagnostics;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx run [overrides --] app args`: starts the app with kvx as its managed identity and returns its exit code.
/// Variables that would make DefaultAzureCredential choose something else first are removed, and said so,
/// because an app silently authenticating as a service principal is the failure that is hard to spot.
/// </summary>
public static class RunCommand
{
    /// <summary>What a shell returns when the command cannot be found or started.</summary>
    private const int NotStarted = 127;

    public static async Task<int> Execute(string[] args)
    {
        var (overrides, command) = KvxCli.SplitAtDashDash(args);
        if (command.Length == 0) return KvxCli.Fail("usage: kvx run [--setting=value… --] <app> [args…]");

        var (client, error) = await KvxClient.Connect(overrides);
        if (client is null) return KvxCli.Fail(error!);

        var start = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var arg in command[1..]) start.ArgumentList.Add(arg);
        foreach (var name in client.Options.RunUnset)
            if (start.Environment.Remove(name))
                Console.Error.WriteLine($"kvx: unset {name} for this app, so DefaultAzureCredential uses kvx");
        foreach (var (name, value) in client.IdentityVariables()) start.Environment[name] = value;

        // The terminal delivers Ctrl-C to the whole process group; let the app decide, and report its exit code.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        Process app;
        try { app = Process.Start(start)!; }
        catch (Win32Exception e)
        {
            Console.Error.WriteLine($"kvx: cannot start '{command[0]}': {e.Message}");
            return NotStarted;
        }
        using (app)
        {
            await app.WaitForExitAsync();
            return app.ExitCode;
        }
    }
}
