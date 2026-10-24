using namespace System.Security.Cryptography
using namespace System.Security.Cryptography.X509Certificates

# Windows and macOS keep trust only in the OS store, which a test can't write without admin rights or a prompt, so CI
# trusts one CA machine-wide and the tests issue their certificates from it (LiveKvx.TestCa reads KVX_TEST_CA).
$ErrorActionPreference = 'Stop'
$key = [RSA]::Create(2048)
$req = [CertificateRequest]::new('CN=kvx test CA', $key, [HashAlgorithmName]::SHA256, [RSASignaturePadding]::Pkcs1)
$req.CertificateExtensions.Add([X509BasicConstraintsExtension]::new($true, $false, 0, $true))
$req.CertificateExtensions.Add([X509KeyUsageExtension]::new([X509KeyUsageFlags]'KeyCertSign, CrlSign', $true))
$req.CertificateExtensions.Add([X509SubjectKeyIdentifierExtension]::new($req.PublicKey, $false))
# Back-dated so the tests can issue already-expired certificates under it.
$ca = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-365), [DateTimeOffset]::UtcNow.AddDays(365))

$bundle = Join-Path $env:RUNNER_TEMP 'kvx-test-ca.pfx'
$cer = Join-Path $env:RUNNER_TEMP 'kvx-test-ca.cer'
[IO.File]::WriteAllBytes($bundle, $ca.Export([X509ContentType]::Pfx, 'test'))
[IO.File]::WriteAllBytes($cer, $ca.Export([X509ContentType]::Cert))

if ($IsWindows) { Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null }
else {
    sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain $cer
    if ($LASTEXITCODE -ne 0) { throw "security add-trusted-cert exited $LASTEXITCODE" }
}
"KVX_TEST_CA=$bundle" >> $env:GITHUB_ENV
