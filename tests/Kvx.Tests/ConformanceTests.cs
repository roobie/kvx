using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Time.Testing;

namespace Kvx.Tests;

/// <summary>The secrets API beyond set/get, driven only through a stock SecretClient.</summary>
public sealed class ConformanceTests : IDisposable
{
    private const string Vault = "https://test.dev.localhost";
    private readonly TempDir _dir = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public void Dispose() => _dir.Dispose();

    private KvxHost Host() => new(_dir, _time);

    [Fact]
    public async Task Lists_secrets_across_pages()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        var names = Enumerable.Range(0, 30).Select(i => $"s-{i:D2}").ToList();
        foreach (var name in names) await client.SetSecretAsync(name, "v");

        var pages = new List<Page<SecretProperties>>();
        await foreach (var page in client.GetPropertiesOfSecretsAsync().AsPages()) pages.Add(page);

        Assert.True(pages.Count > 1, "expected more than one page");
        Assert.Equal(names, pages.SelectMany(p => p.Values).Select(p => p.Name).Order());
        Assert.All(pages.SelectMany(p => p.Values), p => Assert.Null(p.Version));
    }

    [Fact]
    public async Task Lists_versions_across_pages()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        var versions = new List<string>();
        for (var i = 0; i < 30; i++) versions.Add((await client.SetSecretAsync("s", $"v{i}")).Value.Properties.Version);

        var pages = new List<Page<SecretProperties>>();
        await foreach (var page in client.GetPropertiesOfSecretVersionsAsync("s").AsPages()) pages.Add(page);

        Assert.True(pages.Count > 1, "expected more than one page");
        Assert.Equal(versions.Order(), pages.SelectMany(p => p.Values).Select(p => p.Version).Order());
    }

    [Fact]
    public async Task Updates_properties_without_touching_the_value()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        var props = (await client.SetSecretAsync("s", "v")).Value.Properties;
        var expires = DateTimeOffset.FromUnixTimeSeconds(_time.GetUtcNow().AddDays(30).ToUnixTimeSeconds());

        props.Enabled = false;
        props.ContentType = "text/plain";
        props.ExpiresOn = expires;
        props.Tags["env"] = "dev";
        var updated = (await client.UpdateSecretPropertiesAsync(props)).Value;

        Assert.False(updated.Enabled);
        Assert.Equal("text/plain", updated.ContentType);
        Assert.Equal(expires, updated.ExpiresOn);
        Assert.Equal("dev", updated.Tags["env"]);
        var disabled = await Assert.ThrowsAsync<RequestFailedException>(() => client.GetSecretAsync("s"));
        Assert.Equal(403, disabled.Status);

        updated.Enabled = true;
        await client.UpdateSecretPropertiesAsync(updated);
        var secret = (await client.GetSecretAsync("s")).Value;
        Assert.Equal("v", secret.Value);
        Assert.Equal("text/plain", secret.Properties.ContentType);
    }

    [Fact]
    public async Task Delete_then_recover()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        await client.SetSecretAsync("s", "v");

        var delete = await client.StartDeleteSecretAsync("s");
        await delete.WaitForCompletionAsync();
        Assert.Equal(404, (await Assert.ThrowsAsync<RequestFailedException>(() => client.GetSecretAsync("s"))).Status);

        var deleted = (await client.GetDeletedSecretAsync("s")).Value;
        Assert.Equal(new Uri($"{Vault}/deletedsecrets/s"), deleted.RecoveryId);
        Assert.Equal(_time.GetUtcNow().AddDays(90).ToUnixTimeSeconds(), deleted.ScheduledPurgeDate?.ToUnixTimeSeconds());
        Assert.Equal(["s"], await Names(client.GetDeletedSecretsAsync()));
        Assert.Empty(await Names(client.GetPropertiesOfSecretsAsync()));

        var recover = await client.StartRecoverDeletedSecretAsync("s");
        await recover.WaitForCompletionAsync();
        Assert.Equal("v", (await client.GetSecretAsync("s")).Value.Value);
        Assert.Empty(await Names(client.GetDeletedSecretsAsync()));
    }

    [Fact]
    public async Task Purge_frees_the_name()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        await client.SetSecretAsync("s", "old");
        await (await client.StartDeleteSecretAsync("s")).WaitForCompletionAsync();

        await client.PurgeDeletedSecretAsync("s");

        Assert.Equal(404, (await Assert.ThrowsAsync<RequestFailedException>(() => client.GetDeletedSecretAsync("s"))).Status);
        await client.SetSecretAsync("s", "new");
        Assert.Equal("new", (await client.GetSecretAsync("s")).Value.Value);
        Assert.Single(await Versions(client, "s"));
    }

    [Fact]
    public async Task Backup_purge_restore_brings_back_every_version()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        var first = (await client.SetSecretAsync("s", "one")).Value.Properties.Version;
        await client.SetSecretAsync("s", "two");

        var backup = (await client.BackupSecretAsync("s")).Value;
        await (await client.StartDeleteSecretAsync("s")).WaitForCompletionAsync();
        await client.PurgeDeletedSecretAsync("s");
        var restored = (await client.RestoreSecretBackupAsync(backup)).Value;

        Assert.Equal("s", restored.Name);
        Assert.Equal("two", (await client.GetSecretAsync("s")).Value.Value);
        Assert.Equal("one", (await client.GetSecretAsync("s", first)).Value.Value);
        Assert.Equal(2, (await Versions(client, "s")).Count);
    }

    [Fact]
    public async Task Surfaces_service_errors()
    {
        using var host = Host();
        var client = host.SecretClient(Vault);
        await client.SetSecretAsync("live", "v");
        var backup = (await client.BackupSecretAsync("live")).Value;
        await client.SetSecretAsync("gone", "v");
        await (await client.StartDeleteSecretAsync("gone")).WaitForCompletionAsync();

        var reuse = await Assert.ThrowsAsync<RequestFailedException>(() => client.SetSecretAsync("gone", "v"));
        Assert.Equal((409, "Conflict"), (reuse.Status, reuse.ErrorCode));

        var overwrite = await Assert.ThrowsAsync<RequestFailedException>(() => client.RestoreSecretBackupAsync(backup));
        Assert.Equal((409, "Conflict"), (overwrite.Status, overwrite.ErrorCode));

        var garbage = await Assert.ThrowsAsync<RequestFailedException>(
            () => client.RestoreSecretBackupAsync(RandomNumberGenerator.GetBytes(64)));
        Assert.Equal(400, garbage.Status);

        foreach (var call in new Func<Task>[]
                 {
                     () => client.GetDeletedSecretAsync("never"),
                     () => client.PurgeDeletedSecretAsync("never"),
                     () => client.StartRecoverDeletedSecretAsync("never"),
                     () => client.StartDeleteSecretAsync("never"),
                 })
        {
            var e = await Assert.ThrowsAsync<RequestFailedException>(call);
            Assert.Equal((404, "SecretNotFound"), (e.Status, e.ErrorCode));
        }
    }

    [Fact]
    public async Task Deleted_state_survives_restart_until_the_purge_date()
    {
        using (var first = Host())
        {
            var client = first.SecretClient(Vault);
            await client.SetSecretAsync("s", "v");
            await (await client.StartDeleteSecretAsync("s")).WaitForCompletionAsync();
        }

        using (var second = Host())
            Assert.Equal("s", (await second.SecretClient(Vault).GetDeletedSecretAsync("s")).Value.Name);

        _time.Advance(TimeSpan.FromDays(90));
        using var third = Host();
        var after = third.SecretClient(Vault);
        Assert.Equal(404, (await Assert.ThrowsAsync<RequestFailedException>(() => after.GetDeletedSecretAsync("s"))).Status);
        await after.SetSecretAsync("s", "reused");
    }

    [Fact]
    public async Task Full_lifecycle_leaves_no_plaintext_on_disk_or_in_backups()
    {
        const string name = "kvx-canary-name";
        const string value = "kvx-canary-value-7f3a";
        var scanned = new List<(string Where, byte[] Bytes)>();

        using (var host = Host())
        {
            var client = host.SecretClient(Vault);
            var props = (await client.SetSecretAsync(name, value)).Value.Properties;
            props.Tags["note"] = "x";
            await client.UpdateSecretPropertiesAsync(props);
            scanned.Add(("backup", (await client.BackupSecretAsync(name)).Value));
            await (await client.StartDeleteSecretAsync(name)).WaitForCompletionAsync();
            await (await client.StartRecoverDeletedSecretAsync(name)).WaitForCompletionAsync();
            await (await client.StartDeleteSecretAsync(name)).WaitForCompletionAsync();   // left deleted on disk
        }

        foreach (var file in Directory.GetFiles(Path.Combine(_dir.Path, "data"), "*", SearchOption.AllDirectories))
            scanned.Add((file, await File.ReadAllBytesAsync(file)));

        foreach (var (where, bytes) in scanned)
        foreach (var needle in new[] { name, value })
            Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) < 0, $"{needle} found in {where}");
    }

    private static async Task<List<string>> Names<T>(AsyncPageable<T> items) where T : notnull
    {
        var names = new List<string>();
        await foreach (var item in items)
            names.Add(item switch
            {
                DeletedSecret d => d.Name,
                SecretProperties p => p.Name,
                _ => throw new ArgumentException(typeof(T).Name),
            });
        return names;
    }

    private static async Task<List<SecretProperties>> Versions(SecretClient client, string name)
    {
        var versions = new List<SecretProperties>();
        await foreach (var v in client.GetPropertiesOfSecretVersionsAsync(name)) versions.Add(v);
        return versions;
    }
}
