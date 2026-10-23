namespace Kvx.Server.Cli;

/// <summary>
/// `kvx env [--shell posix|fish|pwsh] [overrides]`: what `kvx run` sets, as shell commands to eval,
/// for IDEs and long-lived shells. Values are single-quoted so the token never needs escaping by hand.
/// </summary>
public static class EnvCommand
{
    public static async Task<int> Execute(string[] args)
    {
        var shell = DefaultShell();
        var overrides = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--shell" && i + 1 < args.Length) shell = args[++i];
            else if (args[i].StartsWith("--shell=", StringComparison.Ordinal)) shell = args[i]["--shell=".Length..];
            else overrides.Add(args[i]);
        }
        if (!Syntax.TryGetValue(shell, out var syntax))
            return KvxCli.Fail($"unknown shell '{shell}'; use one of: {string.Join(", ", Syntax.Keys)}");

        var (client, error) = await KvxClient.Connect(overrides.ToArray());
        if (client is null) return KvxCli.Fail(error!);

        foreach (var name in client.Options.RunUnset)
            if (Environment.GetEnvironmentVariable(name) is not null)
                Console.Out.WriteLine(syntax.Unset(name));
        foreach (var (name, value) in client.AppVariables())
            Console.Out.WriteLine(syntax.Export(name, value));
        return 0;
    }

    private sealed record ShellSyntax(Func<string, string> Unset, Func<string, string, string> Export);

    private static readonly Dictionary<string, ShellSyntax> Syntax = new()
    {
        ["posix"] = new(n => $"unset {n}", (n, v) => $"export {n}='{v.Replace("'", "'\\''")}'"),
        ["fish"] = new(n => $"set -e {n}", (n, v) => $"set -gx {n} '{v.Replace("\\", "\\\\").Replace("'", "\\'")}'"),
        ["pwsh"] = new(n => $"Remove-Item Env:{n} -ErrorAction SilentlyContinue", (n, v) => $"$env:{n} = '{v.Replace("'", "''")}'"),
    };

    private static string DefaultShell() =>
        Path.GetFileName(Environment.GetEnvironmentVariable("SHELL") ?? "") switch
        {
            "fish" => "fish",
            "pwsh" => "pwsh",
            _ => "posix",
        };
}
