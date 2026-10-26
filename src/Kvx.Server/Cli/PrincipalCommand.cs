using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kvx.Server.Api;
using Kvx.Server.Auth;

namespace Kvx.Server.Cli;

/// <summary>
/// `kvx principal set | show | clear [overrides]`: the service principals kvx signs in as. An app gets one by naming
/// its client id or an alias (AZURE_CLIENT_ID for DefaultAzureCredential); the default one serves apps that name none
/// when Kvx:Passthrough is ServicePrincipal. It goes through the serving kvx, which keeps them in the sealed store.
/// The secret comes from stdin, never argv, which shell history and process listings would keep.
/// </summary>
public static class PrincipalCommand
{
    private static string Usage => Help.Usage("principal");

    public static async Task<int> Execute(string[] args)
    {
        var action = args.Length > 0 ? args[0] : "";
        string? tenant = null, clientId = null;
        List<string>? aliases = null;
        var makeDefault = false;
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
                    case "--for": (aliases ??= []).Add(Value()); break;
                    case "--default": makeDefault = true; break;
                    default: overrides.Add(args[i]); break;
                }
            }
            catch (ArgumentException e) { return KvxCli.Fail(e.Message); }
        }
        var valid = action switch
        {
            "set" => tenant is not null && clientId is not null,
            "clear" => clientId is not null && tenant is null && aliases is null && !makeDefault,
            "show" => tenant is null && clientId is null && aliases is null && !makeDefault,
            _ => false,
        };
        if (!valid) return KvxCli.Fail(Usage);

        string? secret = null;
        if (action == "set")
        {
            secret = ReadSecret();
            if (secret.Length == 0) return KvxCli.Fail("no client secret was given on stdin.");
        }

        var (client, error) = await KvxClient.Connect(overrides.ToArray());
        if (client is null) return KvxCli.FailToConnect(error!);
        using var http = client.Http();
        var path = PrincipalApi.Path.TrimStart('/');
        var request = action switch
        {
            "set" => new HttpRequestMessage(HttpMethod.Put, path)
                { Content = JsonContent.Create(new PrincipalRequest(tenant, clientId, secret, aliases?.ToArray(), makeDefault)) },
            "clear" => new HttpRequestMessage(HttpMethod.Delete, $"{path}/{Uri.EscapeDataString(clientId!)}"),
            _ => new HttpRequestMessage(HttpMethod.Get, path),
        };
        request.Headers.Authorization = client.Bearer;

        using var response = await http.SendAsync(request);
        if (action == "clear" && response.StatusCode == HttpStatusCode.NotFound)
        {
            Console.Out.WriteLine($"no service principal with client id {clientId} is stored.");
            return 0;
        }
        if (!response.IsSuccessStatusCode)
            return KvxCli.Fail($"{(int)response.StatusCode} {await ErrorMessage(response)}");
        if (action == "clear")
        {
            Console.Out.WriteLine($"service principal {clientId} removed.");
            return 0;
        }

        var state = (await response.Content.ReadFromJsonAsync<PrincipalState>())!;
        if (state.Principals.Length == 0)
        {
            Console.Out.WriteLine("no service principal is stored.");
            return 1;
        }
        foreach (var view in state.Principals)
        {
            var updated = DateTimeOffset.FromUnixTimeSeconds(view.Updated).ToLocalTime();
            Console.Out.WriteLine($"service principal {view.ClientId} in tenant {view.TenantId}, set {updated:yyyy-MM-dd HH:mm}" +
                (view.Default ? ", default" : "") + ".");
            var ids = string.Join(" or ", view.Aliases.Prepend(view.ClientId));
            Console.Out.WriteLine($"  apps get it with AZURE_CLIENT_ID={ids}.");
        }
        var hasDefault = state.Principals.Any(p => p.Default);
        Console.Out.WriteLine((state.DefaultUsed, hasDefault) switch
        {
            (true, true) => "Apps that name no identity get the default one; `kvx doctor` checks that each signs in.",
            (true, false) => "Apps that name no identity get an error: no principal is the default. Mark one with `kvx principal set … --default`.",
            (false, true) => $"Apps that name no identity do not get the default one: the serving kvx needs Kvx:Passthrough={Upstream.ServicePrincipal} and a restart.",
            _ => "Apps that name no identity get kvx's passthrough, not these.",
        });
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
