using System.Text.Json.Serialization;
using Kvx.Server.Api;
using Kvx.Server.Auth;
using Kvx.Server.Keys;
using Kvx.Server.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kvx.Server;

public static class KvxApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(Options(args));
        configure?.Invoke(builder);
        // Kestrel's own default reloads a certificate file when it changes. Its watcher can deadlock when a change
        // arrives during startup (seen as a hang dump: CertificatePathWatcher against CompositeChangeToken). Replacing
        // the certificate already means a kvx restart, as replacing it in the store does.
        builder.WebHost.ConfigureKestrel((context, kestrel) =>
            kestrel.Configure(context.Configuration.GetSection("Kestrel"), reloadOnChange: false));

        builder.Services.AddOptions<KvxOptions>()
            .Bind(builder.Configuration.GetSection("Kvx"))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => KeyProtectors.Create(sp.GetRequiredService<IOptions<KvxOptions>>().Value));
        builder.Services.AddSingleton(sp => SealedStore.Open(
            sp.GetRequiredService<IOptions<KvxOptions>>().Value.ResolveDataDir(),
            sp.GetRequiredService<IKeyProtector>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SealedStore>()));
        builder.Services.AddSingleton<SecretRepository>();
        builder.Services.AddSingleton<DevTokens>();
        builder.Services.AddHostedService<Names.LoopbackDns>();
        builder.Services.TryAddSingleton<PrincipalCredentialFactory>(PrincipalCredentials.ClientSecret);
        builder.Services.TryAddSingleton<PrincipalCredentials>();
        builder.Services.TryAddSingleton(Upstream.Create);
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

        var app = builder.Build();

        // Fail at startup, not on the first request, when the key or store is unusable.
        app.Services.GetRequiredService<SecretRepository>();
        app.Services.GetRequiredService<IOptions<KvxOptions>>().Value.ResolveDns();
        app.Services.GetRequiredService<Upstream>();

        app.UseRouting();   // so the auth middleware can see which endpoint was matched
        app.UseChallengeAuth();
        app.MapIdentity();
        app.MapPrincipal();
        app.MapSecrets();
        return app;
    }

    /// <summary>
    /// Configuration is found next to the binary, not in the working directory: a dotnet tool runs from wherever
    /// the developer happens to be. The client commands use the same options, so they read the same settings.
    /// </summary>
    public static WebApplicationOptions Options(string[] args) =>
        new() { Args = args, ContentRootPath = AppContext.BaseDirectory };

    /// <summary>kvx's configuration without starting anything; for the client commands.</summary>
    public static IConfiguration Configuration(string[] args) =>
        WebApplication.CreateBuilder(Options(args)).Configuration;
}
