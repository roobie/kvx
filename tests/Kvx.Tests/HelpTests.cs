using Kvx.Server.Cli;

namespace Kvx.Tests;

/// <summary>`kvx help`, guides built from docs/, and per-command help, as the kvx process prints them.</summary>
public sealed class HelpTests
{
    private static readonly string Repo = FindRepo();

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kvx.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("no Kvx.slnx above the test binaries");
    }

    private static string[] DocFiles() =>
        [.. Directory.GetFiles(Path.Combine(Repo, "docs"), "*.md").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];

    [Fact]
    public void Every_doc_is_a_topic_and_every_topic_a_doc()
    {
        Assert.Equal(DocFiles(), Help.Topics.Select(t => t.Name));
        Assert.Contains(Help.FirstTopic, DocFiles());
    }

    [Fact]
    public void The_readme_links_every_guide()
    {
        var readme = File.ReadAllText(Path.Combine(Repo, "README.md"));
        foreach (var name in DocFiles())
        {
            Assert.Contains($"(docs/{name}.md)", readme);
            Assert.Contains($"`{name}`", readme);
        }
    }

    [Fact]
    public async Task Help_lists_the_commands_and_one_line_per_goal()
    {
        var help = await LiveKvx.Cli(["help"]);
        Assert.True(help.ExitCode == 0, help.ToString());
        foreach (var (verb, _, _) in Help.Commands) Assert.Contains($"kvx {(verb == "serve" ? "[serve]" : verb)}", help.Stdout);
        Assert.Contains("What do you want to do?", help.Stdout);
        foreach (var topic in Help.Topics) Assert.Contains($"kvx help {topic.Name}", help.Stdout);
        Assert.Equal(help.Stdout, (await LiveKvx.Cli(["--help"])).Stdout);
    }

    [Fact]
    public async Task A_topic_prints_its_guide_and_where_it_is_online()
    {
        var help = await LiveKvx.Cli(["help", "passthrough"]);
        Assert.True(help.ExitCode == 0, help.ToString());
        var doc = File.ReadAllText(Path.Combine(Repo, "docs", "passthrough.md"));
        Assert.StartsWith(doc.TrimEnd(), help.Stdout.ReplaceLineEndings("\n"));
        Assert.Matches(@"Online: https://github\.com/roobie/kvx/blob/[0-9a-f]{40}/docs/passthrough\.md", help.Stdout);
    }

    [Fact]
    public async Task An_unknown_topic_lists_the_known_ones()
    {
        var help = await LiveKvx.Cli(["help", "nope"]);
        Assert.Equal(1, help.ExitCode);
        foreach (var topic in Help.Topics) Assert.Contains(topic.Name, help.Stderr);
    }

    [Theory]
    [InlineData("principal", "--client-id")]
    [InlineData("run", "<app>")]
    [InlineData("doctor", "kvx doctor")]
    public async Task A_command_with_help_prints_its_usage(string verb, string expected)
    {
        var help = await LiveKvx.Cli([verb, "--help"]);
        Assert.True(help.ExitCode == 0, help.ToString());
        Assert.Contains(expected, help.Stdout);
        Assert.Equal(help.Stdout, (await LiveKvx.Cli(["help", verb])).Stdout);
    }
}
