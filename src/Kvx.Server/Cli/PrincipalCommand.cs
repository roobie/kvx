using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kvx.Server.Api;
using Kvx.Server.Auth;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx principal set --tenant t --client-id c | show | clear [overrides]`: the service principal passthrough signs in
/// as when Kvx:Passthrough is ServicePrincipal. It goes through the serving kvx, which keeps it in the sealed store.
/// The secret comes from stdin, never argv, which shell history and process listings would keep.
/// </summary>
public static class PrincipalCommand
{
    private const string Usage =
        "usage: kvx principal set --tenant <tenant id> --client-id <client id>   (client secret on stdin)\n" +
        "       kvx principal show | clear";

    public static async Task<int> Execute(string[] args)
    {
        var action = args.Length > 0 ? args[0] : "";
        string? tenant = null, clientId = null;
        var overrides = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            try
            {
                switch (args[i])
                {
                    case "--tenant": tenant = Value(); break;
                    case "--client-id": clientId = Value(); break;
                    default: overrides.Add(args[i]); break;
                }
            }
            catch (ArgumentException e) { return KvxCli.Fail(e.Message); }
        }
        if (action is not ("set" or "show" or "clear") || (action == "set") != (tenant is not null && clientId is not null))
            return KvxCli.Fail(Usage);

        string? secret = null;
        if (action == "set")
        {
            secret = ReadSecret();
            if (secret.Length == 0) return KvxCli.Fail("no client secret was given on stdin.");
        }

        var (client, error) = await KvxClient.Connect(overrides.ToArray());
        if (client is null) return KvxCli.FailToConnect(error!);
        using var http = client.Http();
        var request = new HttpRequestMessage(action switch
        {
            "set" => HttpMethod.Put,
            "clear" => HttpMethod.Delete,
            _ => HttpMethod.Get,
        }, PrincipalApi.Path.TrimStart('/'));
        request.Headers.Authorization = client.Bearer;
        if (secret is not null) request.Content = JsonContent.Create(new PrincipalRequest(tenant, clientId, secret));

        using var response = await http.SendAsync(request);
        if (action == "clear" && response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.Out.WriteLine("no service principal is set.");
            return 0;
        }
        if (!response.IsSuccessStatusCode)
            return KvxCli.Fail($"{(int)response.StatusCode} {await ErrorMessage(response)}");
        if (action == "clear")
        {
            Console.Out.WriteLine("service principal removed.");
            return 0;
        }

        var state = (await response.Content.ReadFromJsonAsync<PrincipalState>())!;
        if (state.Principal is not { } view)
        {
            Console.Out.WriteLine("no service principal is set.");
            return 1;
        }
        var updated = DateTimeOffset.FromUnixTimeSeconds(view.Updated).ToLocalTime();
        Console.Out.WriteLine($"service principal {view.ClientId} in tenant {view.TenantId}, set {updated:yyyy-MM-dd HH:mm}.");
        Console.Out.WriteLine(state.Used
            ? "Passthrough signs in as it; `kvx doctor` checks that it can."
            : $"Passthrough does not use it: the serving kvx needs Kvx:Passthrough={Upstream.ServicePrincipal} and a restart.");
        return 0;
    }

    /// <summary>Without echo on a terminal; a pipe's trailing newline is not part of the secret.</summary>
    private static string ReadSecret()
    {
        if (Console.IsInputRedirected) return Console.In.ReadToEnd().TrimEnd('\r', '\n');

        Console.Error.Write("client secret: ");
        var secret = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; }
            else if (!char.IsControl(key.KeyChar)) secret.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return secret.ToString();
    }

    private static async Task<string> ErrorMessage(HttpResponseMessage response)
    {
        try { return (await response.Content.ReadFromJsonAsync<ErrorEnvelope>())?.Error.Message ?? ""; }
        catch (System.Text.Json.JsonException) { return await response.Content.ReadAsStringAsync(); }
    }
}
