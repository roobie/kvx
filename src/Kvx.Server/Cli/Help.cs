using System.Reflection;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx help [topic | command]` and `kvx &lt;command&gt; --help`. Commands are described here, next to the dispatch
/// that parses them; guides are the repo's docs/*.md, built into the binary so they match the installed version and
/// work offline. Each guide's title is the goal it serves, so the list of goals cannot drift from the guides.
/// </summary>
public static class Help
{
    private const string Settings = "[--setting=value…]";

    /// <summary>Synopsis, then what it does; the order is the order `kvx help` lists them in.</summary>
    public static readonly IReadOnlyList<(string Verb, string Synopsis, string Does)> Commands =
    [
        ("serve", $"kvx [serve] [--reuse] {Settings}",
            "serve vaults (the default with no command); with --reuse, use the kvx already serving this store, and serve when it stops"),
        ("run", "kvx run [--setting=value… --] <app> [args…]",
            "run an app with kvx as its managed identity"),
        ("env", "kvx env [--shell posix|fish|pwsh]",
            "print the same environment for your shell or IDE"),
        ("import", "kvx import --vault <name> [--project <dir> | --id <user-secrets-id> | --file <secrets.json>] [--remove-plaintext]",
            "move .NET user-secrets into a vault"),
        ("principal", "kvx principal set --tenant <id> --client-id <id> [--for <id>]… [--default]\n" +
                      "kvx principal show\n" +
                      "kvx principal clear --client-id <id>",
            "store, list or remove the service principals kvx signs in as; apps get one with AZURE_CLIENT_ID set to its id " +
            "or a --for alias, and --default serves apps that name none with Kvx:Passthrough=ServicePrincipal; the secret is read from stdin"),
        ("doctor", $"kvx doctor {Settings}",
            "check name resolution, the certificate, the key protector, the serving kvx and its principals, and say how to fix what is wrong"),
        ("describe", $"kvx describe {Settings}",
            "where kvx listens and what apps need, as JSON, for tools"),
        ("help", "kvx help [<topic> | <command>]",
            "this list, a guide, or one command"),
    ];

    /// <summary>For a command's own "usage:" error.</summary>
    public static string Usage(string verb) =>
        "usage: " + Commands.Single(c => c.Verb == verb).Synopsis.Replace("\n", "\n       ");

    public static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Out.Write(Overview());
            return 0;
        }
        var name = args[0];
        var guide = Guide(name);
        var command = Commands.FirstOrDefault(c => c.Verb == name);
        if (guide is null && command.Verb is null)
            return KvxCli.Fail($"no help on '{name}'. Topics: {string.Join(", ", Topics.Select(t => t.Name))}; " +
                               $"commands: {string.Join(", ", Commands.Select(c => c.Verb))}.");
        if (guide is not null) Console.Out.Write(guide);
        if (command.Verb is not null) Console.Out.Write((guide is null ? "" : "\n") + Describe(command));
        return 0;
    }

    /// <summary>`kvx &lt;verb&gt; --help`; only directly after the verb, so `kvx run app --help` still reaches the app.</summary>
    public static bool Asked(string[] rest) => rest.Length > 0 && rest[0] is "--help" or "-h";

    public static int Command(string verb)
    {
        Console.Out.Write(Describe(Commands.Single(c => c.Verb == verb)));
        return 0;
    }

    public static string Overview()
    {
        var text = new System.Text.StringBuilder("kvx — a local Azure Key Vault for development secrets.\n\nCommands:\n");
        foreach (var c in Commands)
            text.Append(Indent(c.Synopsis, "  ")).Append(Wrap(c.Does, "      "));
        text.Append("\nSettings (any command): --urls=…, --Kvx:Domain=…, --Kvx:TokenFile=…, see appsettings.json next to kvx.\n");
        text.Append("\nWhat do you want to do?\n");
        var width = Topics.Max(t => t.Name.Length);
        foreach (var t in Topics)
            text.Append($"  kvx help {t.Name.PadRight(width)}  {t.Title}\n");
        text.Append($"\nNew to kvx? Start with `kvx help {FirstTopic}`.\n");
        return text.ToString();
    }

    public sealed record Topic(string Name, string Title);

    private const string Prefix = "docs/";
    public const string FirstTopic = "get-started";

    public static IReadOnlyList<Topic> Topics { get; } =
    [
        .. Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(r => r.StartsWith(Prefix) && r.EndsWith(".md"))
            .Select(r => r[Prefix.Length..^3])
            .Order(StringComparer.Ordinal)
            .Select(name => new Topic(name, Title(Read(name)!))),
    ];

    /// <summary>The guide, then where it is online at the commit this kvx was built from.</summary>
    public static string? Guide(string name) =>
        Read(name) is { } text ? $"{text.TrimEnd()}\n\nOnline: {Online(name)}\n" : null;

    public static string Online(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var repo = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryUrl").Value;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = version.IndexOf('+');
        var reference = plus >= 0 ? version[(plus + 1)..] : "main";
        return $"{repo}/blob/{reference}/{Prefix}{name}.md";
    }

    private static string? Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{Prefix}{name}.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Title(string text) =>
        text.Split('\n').FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim()
        ?? throw new InvalidDataException("a guide in docs/ has no '# ' title line");

    private static string Describe((string Verb, string Synopsis, string Does) c) =>
        Indent(c.Synopsis, "") + Wrap(c.Does, "  ");

    private static string Indent(string text, string by) =>
        string.Concat(text.Split('\n').Select(l => by + l + "\n"));

    /// <summary>Into lines of at most 100 columns, as a terminal would otherwise break them mid-word.</summary>
    private static string Wrap(string text, string by)
    {
        var lines = new System.Text.StringBuilder();
        var line = by;
        foreach (var word in text.Split(' '))
        {
            if (line.Length > by.Length && line.Length + 1 + word.Length > 100)
            {
                lines.Append(line).Append('\n');
                line = by;
            }
            line += (line.Length > by.Length ? " " : "") + word;
        }
        return lines.Append(line).Append('\n').ToString();
    }
}
