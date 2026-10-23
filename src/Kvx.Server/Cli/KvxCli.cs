using Kvx.Server.Keys;
using Kvx.Server.Storage;

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
            case "doctor": return await DoctorCommand.Execute(rest);
            case "describe": return DescribeCommand.Execute(rest);
            case "help" or "--help" or "-h": Console.Out.Write(Usage); return 0;
            case "serve": args = rest; break;
        }
        var reuse = args.Contains(ReuseFlag);
        args = args.Where(a => a != ReuseFlag).ToArray();
        try
        {
            while (true)
            {
                try
                {
                    await KvxApp.Build(args).RunAsync();
                    return 0;
                }
                catch (StoreInUseException e) when (reuse)
                {
                    if (await WaitWhileAnotherServes(args) is { } error) return Fail($"{e.Message} {error}");
                }
            }
        }
        catch (InvalidOperationException e) when (e.TargetSite?.DeclaringType?.Name == "ListenOptionsHttpsExtensions")
        {
            // Kestrel found no certificate, and its own exception reads as a crash.
            return Fail("there is no certificate to serve https with. Create and trust the ASP.NET Core development " +
                        $"certificate with `dotnet dev-certs https --trust`, or configure one under Kestrel:Certificates:Default. {DoctorHint}");
        }
        catch (KeyProtectorException e)
        {
            return Fail($"{e.Message} {DoctorHint}");
        }
        catch (StoreInUseException e)
        {
            return Fail(e.Message);
        }
    }

    private const string DoctorHint = "`kvx doctor` checks the whole setup.";

    /// <summary>
    /// Serve unless another kvx already serves this store; then use that one, and serve once it stops. Whoever
    /// starts kvx on demand (an Aspire AppHost) gets the developer's own kvx when there is one, and never a second
    /// writer, without having to decide which in advance.
    /// </summary>
    private const string ReuseFlag = "--reuse";

    /// <summary>
    /// Returns once the kvx holding the store stops answering, or why it cannot be used: a holder that does not
    /// answer at this kvx's endpoint with this session's token was started with other settings, and an app given
    /// this kvx's settings would not reach it.
    /// </summary>
    private static async Task<string?> WaitWhileAnotherServes(string[] args)
    {
        var (client, error) = await KvxClient.Connect(args);
        if (client is null) return $"It does not answer as this kvx would: {error}";
        Console.Out.WriteLine($"kvx: using the kvx already serving this store at {client.Endpoint}; this one serves when that one stops.");
        do await Task.Delay(TimeSpan.FromSeconds(2));
        while ((await KvxClient.Connect(args)).Client is not null);
        Console.Out.WriteLine("kvx: the kvx serving this store stopped; serving it here.");
        return null;
    }

    internal static int Fail(string message)
    {
        Console.Error.WriteLine($"kvx: {message}");
        return 1;
    }

    /// <summary>For a client command that found no serving kvx; usually kvx failed to start for a machine-level reason.</summary>
    internal static int FailToConnect(string error) =>
        Fail($"{error} If kvx will not start, `kvx doctor` says why.");

    /// <summary>Splits `[config overrides] -- command…`; without `--` everything is the command.</summary>
    internal static (string[] Overrides, string[] Command) SplitAtDashDash(string[] args)
    {
        var i = Array.IndexOf(args, "--");
        return i < 0 ? ([], args) : (args[..i], args[(i + 1)..]);
    }

    private const string Usage = """
        kvx — a local Azure Key Vault for development secrets.

        Usage:
          kvx [serve] [--reuse] [--setting=value…]  serve vaults (default); with --reuse, use the kvx already
                                                    serving this store, and serve when it stops
          kvx run [--setting=value… --] <app> [args]  run an app with kvx as its managed identity
          kvx env [--shell posix|fish|pwsh]         print the same environment for your shell
          kvx import --vault <name> [--project <dir> | --id <user-secrets-id> | --file <secrets.json>] [--remove-plaintext]
                                                    move .NET user-secrets into a vault
          kvx doctor [--setting=value…]             check name resolution, the certificate, the key protector
                                                    and the serving kvx, and say how to fix what is wrong
          kvx describe [--setting=value…]           where kvx listens and what apps need, as JSON, for tools
          kvx help

        Settings (any command): --urls=…, --Kvx:Domain=…, --Kvx:TokenFile=…, see appsettings.json next to kvx.

        """;
}
