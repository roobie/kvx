using System.Reflection;
using System.Text.Json;
using Kvx.Server.Auth;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx describe [overrides]`: where kvx listens and what an app needs to use it, as JSON, read from kvx's own
/// configuration without connecting. Tools that start or attach to kvx (the Aspire integration) use it so they
/// never have to know where kvx keeps its settings or how they are layered.
/// </summary>
public static class DescribeCommand
{
    public static int Execute(string[] args)
    {
        var config = KvxApp.Configuration(args);
        var options = new KvxOptions();
        config.GetSection("Kvx").Bind(options);

        var http = KvxClient.ListenEndpoint(config["urls"]);
        var https = KvxClient.ListenEndpoint(config["urls"], "https");
        if (http is null || https is null)
            return KvxCli.Fail($"urls '{config["urls"]}' needs both an http:// listener (identity and commands) and an https:// one (vaults).");
        string tokenFile;
        try { tokenFile = options.ResolveTokenFile(); }
        catch (InvalidOperationException e) { return KvxCli.Fail(e.Message); }

        var trust = options.ResolveDevCertTrustDir();
        var description = new
        {
            version = typeof(DescribeCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            endpoint = http.ToString(),
            identityEndpoint = new Uri(http, IdentityEndpoint.Path).ToString(),
            vaultUriTemplate = $"https://{{vault}}.{options.Domain}:{https.Port}/",
            tokenFile,
            dataDir = options.ResolveDataDir(),
            unset = options.RunUnset,
            sslCertDir = OperatingSystem.IsLinux() && Directory.Exists(trust) ? trust : null,
        };
        Console.Out.WriteLine(JsonSerializer.Serialize(description, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
