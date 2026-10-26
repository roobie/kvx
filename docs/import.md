# Move .NET user-secrets into a vault

```sh
kvx import --vault myapp --project src/MyApp            # or --id <UserSecretsId>, or --file secrets.json
kvx import --vault myapp --project src/MyApp --remove-plaintext
```

Keys are stored under the names the Key Vault configuration provider maps back, so `ConnectionStrings:Db` becomes `ConnectionStrings--Db`, and the app reads the same keys after:

```csharp
builder.Configuration.AddAzureKeyVault(new Uri("https://myapp.dev.localhost:5381"), new DefaultAzureCredential());
```

kvx reports any key that cannot be a secret name, and keeps the plaintext file unless every key was imported. Re-running skips secrets that are already in the vault unchanged.
