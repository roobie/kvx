using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Aspire.Hosting.Kvx;

/// <summary>What `kvx describe` says about the kvx a given command and settings would serve or reach.</summary>
public sealed record KvxDescription(
    string Endpoint,
    string IdentityEndpoint,
    string Domain,
    string VaultUriTemplate,
    string TokenFile,
    string[] Unset,
    string? SslCertDir)
{
    public string VaultUri(string vault) => VaultUriTemplate.Replace("{vault}", vault.ToLowerInvariant());

    /// <summary>Asks kvx itself, so its own layered configuration decides, not a guess at where its settings live.</summary>
    public static async Task<KvxDescription> Read(string command, IReadOnlyList<string> settings, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("describe");
        foreach (var s in settings) psi.ArgumentList.Add(s);

        Process p;
        try { p = Process.Start(psi)!; }
        catch (Win32Exception e)
        {
            throw new DistributedApplicationException(
                $"kvx: cannot run '{command}' ({e.Message}). Install it with `dotnet tool install -g kvx`, " +
                "or set Kvx:Command to where it is.");
        }
        using (p)
        {
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
                throw new DistributedApplicationException(
                    $"kvx: `{command} describe` failed: {(await stderr).Trim()} An older kvx without `describe` needs updating: " +
                    "`dotnet tool update -g kvx`.");
            return JsonSerializer.Deserialize<KvxDescription>(await stdout, Json)
                   ?? throw new DistributedApplicationException($"kvx: `{command} describe` printed nothing.");
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
