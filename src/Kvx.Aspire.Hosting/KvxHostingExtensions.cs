using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Kvx;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting;

/// <summary>Runs an AppHost's Azure Key Vault on the developer's kvx in run mode.</summary>
public static class KvxHostingExtensions
{
    private const string KvxName = "kvx";

    /// <summary>
    /// In run mode, serves this vault from kvx instead of provisioning it in Azure: the vault becomes
    /// https://&lt;name&gt;.&lt;kvx domain&gt;, and every resource that references it gets kvx as its managed identity
    /// and waits for kvx. In publish mode nothing changes, so the real Key Vault is provisioned as before.
    /// </summary>
    /// <remarks>
    /// The AppHost's configuration section Kvx sets Command (default "kvx") and Settings (passed to every kvx command).
    /// </remarks>
    public static IResourceBuilder<AzureKeyVaultResource> RunAsKvx(this IResourceBuilder<AzureKeyVaultResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var kvx = AddOrGetKvx(builder.ApplicationBuilder);
        return builder
            .WithAnnotation(new EmulatorResourceAnnotation())
            .WithAnnotation(new KvxVaultAnnotation(kvx.Resource))
            .WithParentRelationship(kvx);
    }

    private static IResourceBuilder<KvxResource> AddOrGetKvx(IDistributedApplicationBuilder app)
    {
        if (app.Resources.OfType<KvxResource>().SingleOrDefault() is { } existing)
            return app.CreateResourceBuilder(existing);

        var section = app.Configuration.GetSection("Kvx");
        var command = section["Command"] is { Length: > 0 } c ? c : "kvx";
        var resource = new KvxResource(KvxName, command, app.AppHostDirectory);
        resource.Settings.AddRange(section.GetSection("Settings").Get<string[]>() ?? []);

        var healthKey = $"{KvxName}_serving";
        app.Services.AddHealthChecks().Add(new(healthKey, _ => new KvxHealthCheck(resource), failureStatus: null, tags: null));

        app.Eventing.Subscribe<BeforeStartEvent>(async (e, ct) =>
        {
            resource.Description = await KvxDescription.Read(command, resource.Settings, ct);
            Wire(e.Model, resource);
        });
        app.Eventing.Subscribe<BeforeResourceStartedEvent>((e, _) =>
        {
            foreach (var vault in app.Resources.OfType<AzureKeyVaultResource>()
                         .Where(v => v.Annotations.OfType<KvxVaultAnnotation>().Any(a => a.Kvx == resource)))
                MarkProvisioned(vault);
            return Task.CompletedTask;
        });

        return app.AddResource(resource)
            .WithArgs(context =>
            {
                context.Args.Add("serve");
                context.Args.Add("--reuse");
                foreach (var s in resource.Settings) context.Args.Add(s);
            })
            .WithHealthCheck(healthKey)
            .WithIconName("Vault")
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Points each kvx vault at kvx and gives every resource that references one kvx's identity. Done at start,
    /// because only then is the model final and kvx's description known.
    /// </summary>
    private static void Wire(DistributedApplicationModel model, KvxResource kvx)
    {
        var d = kvx.RequireDescription();
        var vaults = model.Resources.OfType<AzureKeyVaultResource>()
            .Where(v => v.Annotations.OfType<KvxVaultAnnotation>().Any(a => a.Kvx == kvx))
            .ToHashSet();
        foreach (var vault in vaults)
        {
            vault.Outputs["vaultUri"] = d.VaultUri(vault.Name);
            MarkProvisioned(vault);
        }

        foreach (var resource in model.Resources.OfType<IResourceWithEnvironment>())
        {
            var references = resource.Annotations.OfType<ResourceRelationshipAnnotation>()
                .Any(r => r.Type == "Reference" && r.Resource is AzureKeyVaultResource v && vaults.Contains(v));
            if (!references) continue;

            resource.Annotations.Add(new WaitAnnotation(kvx, WaitType.WaitUntilHealthy));
            resource.Annotations.Add(new EnvironmentCallbackAnnotation(context => SetIdentity(context, d)));
            Trust(resource, kvx.Trust);
        }
        if (d.SslCertDir is { } dir) LoadTrust(kvx.Trust, dir);
    }

    /// <summary>
    /// Aspire gives each resource its own SSL_CERT_DIR built from the certificate authorities it trusts, so kvx's
    /// certificate is handed to Aspire rather than set in the environment. Joins whatever the resource trusts already.
    /// </summary>
    private static void Trust(IResource resource, CertificateAuthorityCollection trust)
    {
        if (resource.Annotations.OfType<CertificateAuthorityCollectionAnnotation>().LastOrDefault() is { } existing)
        {
            if (!existing.CertificateAuthorityCollections.Contains(trust)) existing.CertificateAuthorityCollections.Add(trust);
            return;
        }
        resource.Annotations.Add(new CertificateAuthorityCollectionAnnotation { CertificateAuthorityCollections = { trust } });
    }

    /// <summary>The certificates in kvx's trust directory, which holds PEM files and links to them.</summary>
    private static void LoadTrust(CertificateAuthorityCollection trust, string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir).Where(f => new FileInfo(f).LinkTarget is null))
        {
            var certificates = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
            try { certificates.ImportFromPemFile(file); }
            catch (System.Security.Cryptography.CryptographicException) { continue; }
            foreach (var c in certificates)
                if (!trust.Certificates.Contains(c)) trust.Certificates.Add(c);
        }
    }

    /// <summary>
    /// A vault's outputs wait on its provisioning, which never runs for a vault kvx serves. Aspire may arm that wait
    /// after start, so this runs again before each resource starts.
    /// </summary>
    private static void MarkProvisioned(AzureKeyVaultResource vault)
    {
        if (vault.ProvisioningTaskCompletionSource is not { } provisioning || provisioning.Task.IsFaulted || provisioning.Task.IsCanceled)
            vault.ProvisioningTaskCompletionSource = provisioning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        provisioning.TrySetResult();
    }

    /// <summary>
    /// What `kvx run` gives an app. A variable kvx would unset is set empty instead, because a resource inherits the
    /// AppHost's environment and Azure.Identity treats an empty variable as absent.
    /// </summary>
    private static void SetIdentity(EnvironmentCallbackContext context, KvxDescription d)
    {
        var token = KvxHealthCheck.ReadToken(d.TokenFile)
                    ?? throw new DistributedApplicationException($"kvx: no session token at {d.TokenFile}; is kvx serving?");
        foreach (var name in d.Unset) context.EnvironmentVariables[name] = "";
        context.EnvironmentVariables["IDENTITY_ENDPOINT"] = d.IdentityEndpoint;
        context.EnvironmentVariables["IDENTITY_HEADER"] = token;
    }
}

/// <summary>Marks a Key Vault served by kvx.</summary>
internal sealed class KvxVaultAnnotation(KvxResource kvx) : IResourceAnnotation
{
    public KvxResource Kvx { get; } = kvx;
}
