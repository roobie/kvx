using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx import --vault v [--project dir | --id id | --file path] [--remove-plaintext] [overrides]`:
/// moves plaintext user-secrets into a vault under the names the Key Vault configuration provider maps back
/// (`--` ↔ `:`), so the app reads the same keys. It goes through the serving kvx over its plain-http loopback
/// listener, naming the vault in the Host header, so it needs no certificate trust and is still authenticated.
/// </summary>
public static partial class ImportCommand
{
    private const string ApiVersion = "7.6";

    public static async Task<int> Execute(string[] args)
    {
        string? vault = null, project = null, id = null, file = null;
        var removePlaintext = false;
        var overrides = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            try
            {
                switch (args[i])
                {
                    case "--vault": vault = Value(); break;
                    case "--project": project = Value(); break;
                    case "--id": id = Value(); break;
                    case "--file": file = Value(); break;
                    case "--remove-plaintext": removePlaintext = true; break;
                    default: overrides.Add(args[i]); break;
                }
            }
            catch (ArgumentException e) { return KvxCli.Fail(e.Message); }
        }
        if (vault is null) return KvxCli.Fail("usage: kvx import --vault <name> [--project <dir> | --id <user-secrets-id> | --file <secrets.json>] [--remove-plaintext]");
        if (new[] { project, id, file }.Count(s => s is not null) > 1) return KvxCli.Fail("give at most one of --project, --id, --file");

        string path;
        try { path = file is not null ? Path.GetFullPath(file) : PathHelper.GetSecretsPathFromSecretsId(id ?? UserSecretsId(project ?? ".")); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException) { return KvxCli.Fail(e.Message); }
        if (!File.Exists(path)) return KvxCli.Fail($"no secrets file at {path}");

        var (client, error) = await KvxClient.Connect(overrides.ToArray());
        if (client is null) return KvxCli.FailToConnect(error!);

        var entries = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build()
            .AsEnumerable().Where(kv => kv.Value is not null).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var host = $"{vault}.{client.Options.Domain}";
        using var http = client.Http();
        int imported = 0, unchanged = 0, problems = 0;
        foreach (var (key, value) in entries)
        {
            var name = key.Replace(":", "--");
            if (!SecretName().IsMatch(name))
            {
                Console.Error.WriteLine($"kvx: cannot import '{key}': '{name}' is not a Key Vault secret name (letters, digits and '-', at most 127)");
                problems++;
                continue;
            }
            var current = await Send(http, client, HttpMethod.Get, host, name, null);
            if (current.StatusCode == HttpStatusCode.OK && (await current.Content.ReadFromJsonAsync<JsonObject>())?["value"]?.GetValue<string>() == value)
            {
                unchanged++;
                continue;
            }
            var put = await Send(http, client, HttpMethod.Put, host, name, JsonContent.Create(new { value }));
            if (put.IsSuccessStatusCode)
            {
                Console.Out.WriteLine($"{key} → {name}");
                imported++;
            }
            else
            {
                Console.Error.WriteLine($"kvx: could not import '{key}': {(int)put.StatusCode} {await put.Content.ReadAsStringAsync()}");
                problems++;
            }
        }
        Console.Out.WriteLine($"{imported} imported, {unchanged} already in vault '{vault}', {problems} not imported, from {path}");

        if (problems > 0)
        {
            if (removePlaintext) Console.Error.WriteLine($"kvx: kept {path} because not everything was imported");
            return 1;
        }
        if (removePlaintext)
        {
            File.Delete(path);
            Console.Out.WriteLine($"removed {path}");
        }
        return 0;
    }

    private static Task<HttpResponseMessage> Send(HttpClient http, KvxClient client, HttpMethod method, string host, string name, HttpContent? body)
    {
        var request = new HttpRequestMessage(method, $"secrets/{name}?api-version={ApiVersion}") { Content = body };
        request.Headers.Host = host;
        request.Headers.Authorization = client.Bearer;
        return http.SendAsync(request);
    }

    /// <summary>The UserSecretsId of the project in <paramref name="project"/> (a .csproj, or a directory holding one).</summary>
    private static string UserSecretsId(string project)
    {
        var csproj = Directory.Exists(project)
            ? Directory.GetFiles(project, "*.csproj") switch
            {
                [var one] => one,
                [] => throw new InvalidOperationException($"no .csproj in {Path.GetFullPath(project)}; use --project <file.csproj> or --id"),
                _ => throw new InvalidOperationException($"several .csproj files in {Path.GetFullPath(project)}; use --project <file.csproj>"),
            }
            : project;
        if (!File.Exists(csproj)) throw new InvalidOperationException($"no project at {Path.GetFullPath(csproj)}");
        return XDocument.Load(csproj).Descendants("UserSecretsId").FirstOrDefault()?.Value.Trim() is { Length: > 0 } secretsId
            ? secretsId
            : throw new InvalidOperationException($"{csproj} has no UserSecretsId; use --id or --file");
    }

    [GeneratedRegex("^[0-9a-zA-Z-]{1,127}$")]
    private static partial Regex SecretName();
}
