using System.Net.Http.Headers;
using Kvx.Server.Auth;

namespace Kvx.Server.Cli;

/// <summary>
/// A client command's view of the kvx that is serving: where it listens and this login session's token.
/// The client never opens the store: the server holds it in memory, so a second writer would lose updates.
/// </summary>
public sealed class KvxClient
{
    public required Uri Endpoint { get; init; }
    public required string Token { get; init; }
    public required KvxOptions Options { get; init; }

    public string IdentityEndpoint => new Uri(Endpoint, Auth.IdentityEndpoint.Path).ToString();

    public HttpClient Http() => new() { BaseAddress = Endpoint, Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Connects to the serving kvx, or returns why not in words that tell the developer what to do.
    /// Asking for kvx's own token proves both that it listens and that the token file belongs to it.
    /// </summary>
    public static async Task<(KvxClient? Client, string? Error)> Connect(string[] overrides)
    {
        var config = KvxApp.Configuration(overrides);
        var options = new KvxOptions();
        config.GetSection("Kvx").Bind(options);
        var endpoint = ListenEndpoint(config["urls"]);
        if (endpoint is null)
            return (null, $"urls '{config["urls"]}' has no http:// listener for kvx's identity endpoint and commands.");

        string file;
        try { file = options.ResolveTokenFile(); }
        catch (InvalidOperationException e) { return (null, e.Message); }
        var start = $"start kvx first (run `kvx` in another terminal), using the same settings as this command.";
        var token = DevTokens.Read(file);
        if (token is null)
            return (null, $"kvx is not serving: there is no session token at {file}; {start}");

        var client = new KvxClient { Endpoint = endpoint, Token = token, Options = options };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var probe = new HttpRequestMessage(HttpMethod.Get,
            $"{client.IdentityEndpoint}?api-version=2019-08-01&resource={Uri.EscapeDataString($"https://{options.Domain}")}");
        probe.Headers.Add("X-IDENTITY-HEADER", token);
        try
        {
            using var response = await http.SendAsync(probe);
            if (response.IsSuccessStatusCode) return (client, null);
            return (null, $"the server at {endpoint} answered {(int)response.StatusCode} to kvx's identity check; " +
                          $"is it kvx with token file {file}? {start}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (null, $"kvx is not serving at {endpoint} ({e.Message}); {start}");
        }
    }

    /// <summary>
    /// The first listen URL of the scheme, with a wildcard host made reachable as loopback. Clients and the identity
    /// endpoint use the plain-http one so that nothing but the app's vault calls depends on certificate trust, as in
    /// Azure, where IDENTITY_ENDPOINT is plain http too.
    /// </summary>
    internal static Uri? ListenEndpoint(string? urls, string scheme = "http")
    {
        var first = urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(u => u.StartsWith($"{scheme}://", StringComparison.OrdinalIgnoreCase));
        if (first is null) return null;
        foreach (var wildcard in new[] { "://*:", "://+:", "://0.0.0.0:", "://[::]:" })
            first = first.Replace(wildcard, "://127.0.0.1:");
        return Uri.TryCreate(first, UriKind.Absolute, out var uri) ? new Uri(uri.GetLeftPart(UriPartial.Authority) + "/") : null;
    }

    /// <summary>
    /// The variables an app needs to use kvx as its managed identity and, on Linux, to trust the dev cert
    /// kvx serves vaults with.
    /// </summary>
    public IReadOnlyDictionary<string, string> AppVariables()
    {
        var variables = new Dictionary<string, string>
        {
            ["IDENTITY_ENDPOINT"] = IdentityEndpoint,
            ["IDENTITY_HEADER"] = Token,
        };
        var trust = Options.ResolveDevCertTrustDir();
        var current = Environment.GetEnvironmentVariable("SSL_CERT_DIR");
        if (OperatingSystem.IsLinux() && Directory.Exists(trust) && current?.Split(':').Contains(trust) != true)
            variables["SSL_CERT_DIR"] = string.IsNullOrEmpty(current) ? trust : $"{trust}:{current}";
        return variables;
    }

    public AuthenticationHeaderValue Bearer => new("Bearer", Token);
}
