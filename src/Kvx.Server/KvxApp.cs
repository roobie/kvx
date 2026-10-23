using System.Text.Json.Serialization;
using Kvx.Server.Api;
using Kvx.Server.Auth;
using Kvx.Server.Keys;
using Kvx.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kvx.Server;

public static class KvxApp
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

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
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

        var app = builder.Build();

        // Fail at startup, not on the first request, when the key or store is unusable.
        app.Services.GetRequiredService<SecretRepository>();

        app.UseRouting();   // so the auth middleware can see which endpoint was matched
        app.UseChallengeAuth();
        app.MapIdentity();
        app.MapSecrets();
        return app;
    }
}
