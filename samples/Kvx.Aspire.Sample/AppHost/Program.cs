var builder = DistributedApplication.CreateBuilder(args);

// A real Azure Key Vault when published; the developer's kvx when run.
var secrets = builder.AddAzureKeyVault("secrets").RunAsKvx();

builder.AddProject<Projects.Kvx_Aspire_Sample_Worker>("worker")
    .WithReference(secrets);

// Anything that is not .NET trusts the vault's certificate too: curl prints the vault's HTTP status, or fails the handshake.
builder.AddExecutable("curl", "curl", ".", "--silent", "--show-error", "--output", OperatingSystem.IsWindows() ? "NUL" : "/dev/null", "--write-out", "%{http_code}\n")
    .WithArgs(context => context.Args.Add(secrets.Resource.UriExpression))
    .WithReference(secrets);

builder.Build().Run();
