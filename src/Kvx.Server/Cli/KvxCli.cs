namespace Kvx.Server.Cli;

/// <summary>
/// One binary: no verb (or anything that is not a verb) serves, so test hosts and plain `kvx --urls=…` keep working;
/// the other verbs are clients of a kvx that is already serving.
/// </summary>
public static class KvxCli
{
    public static async Task<int> Dispatch(string[] args)
    {
        var verb = args.Length > 0 ? args[0] : "";
        var rest = args.Skip(1).ToArray();
        switch (verb)
        {
            case "run": return await RunCommand.Execute(rest);
            case "env": return await EnvCommand.Execute(rest);
            case "import": return await ImportCommand.Execute(rest);
            case "help" or "--help" or "-h": Console.Out.Write(Usage); return 0;
            case "serve": args = rest; break;
        }
        await KvxApp.Build(args).RunAsync();
        return 0;
    }

    internal static int Fail(string message)
    {
        Console.Error.WriteLine($"kvx: {message}");
        return 1;
    }

    /// <summary>Splits `[config overrides] -- command…`; without `--` everything is the command.</summary>
    internal static (string[] Overrides, string[] Command) SplitAtDashDash(string[] args)
    {
        var i = Array.IndexOf(args, "--");
        return i < 0 ? ([], args) : (args[..i], args[(i + 1)..]);
    }

    private const string Usage = """
        kvx — a local Azure Key Vault for development secrets.

        Usage:
          kvx [serve] [--setting=value…]            serve vaults (default)
          kvx run [--setting=value… --] <app> [args]  run an app with kvx as its managed identity
          kvx env [--shell posix|fish|pwsh]         print the same environment for your shell
          kvx import --vault <name> [--project <dir> | --id <user-secrets-id> | --file <secrets.json>] [--remove-plaintext]
                                                    move .NET user-secrets into a vault
          kvx help

        Settings (any command): --urls=…, --Kvx:Domain=…, --Kvx:TokenFile=…, see appsettings.json next to kvx.

        """;
}
