using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting.Kvx;

/// <summary>
/// Healthy when kvx answers its identity endpoint with this login session's token, which proves both that it
/// serves and that the token apps will be given is the one it accepts. Same check as kvx's own client commands.
/// </summary>
internal sealed class KvxHealthCheck(KvxResource kvx) : IHealthCheck
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (kvx.Description is not { } d) return HealthCheckResult.Unhealthy("kvx has not been described yet");
        var token = ReadToken(d.TokenFile);
        if (token is null) return HealthCheckResult.Unhealthy($"no session token at {d.TokenFile} yet");

        using var probe = new HttpRequestMessage(HttpMethod.Get,
            $"{d.IdentityEndpoint}?api-version=2019-08-01&resource={Uri.EscapeDataString($"https://{d.Domain}")}");
        probe.Headers.Add("X-IDENTITY-HEADER", token);
        try
        {
            using var response = await Http.SendAsync(probe, ct);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{d.Endpoint} answered {(int)response.StatusCode} to kvx's identity check");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy($"kvx is not serving at {d.Endpoint}", e);
        }
    }

    internal static string? ReadToken(string file)
    {
        try { return File.Exists(file) ? File.ReadAllText(file).Trim() : null; }
        catch (IOException) { return null; }
    }
}
