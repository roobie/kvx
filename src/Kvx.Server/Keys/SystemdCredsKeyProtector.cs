using System.ComponentModel;
using System.Diagnostics;

namespace Kvx.Server.Keys;

/// <summary>
/// Per-user systemd credentials (systemd ≥ 256): TPM2-backed where available, no prompt,
/// works without a desktop session. The credential name is bound into the ciphertext.
/// </summary>
public sealed class SystemdCredsKeyProtector(string credentialName) : IKeyProtector
{
    public byte[] Wrap(byte[] key) => Run("encrypt", key);
    public byte[] Unwrap(byte[] wrapped) => Run("decrypt", wrapped);

    private byte[] Run(string verb, byte[] input)
    {
        var psi = new ProcessStartInfo("systemd-creds")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { verb, "--user", $"--name={credentialName}", "-", "-" })
            psi.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new KeyProtectorException("systemd-creds did not start.");
        }
        catch (Win32Exception e)
        {
            throw new KeyProtectorException("systemd-creds was not found; kvx needs systemd 256 or later.", e);
        }

        using (process)
        {
            var output = new MemoryStream();
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardInput.BaseStream.Write(input);
            process.StandardInput.Close();
            process.WaitForExit();
            stdout.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
                throw new KeyProtectorException(
                    $"systemd-creds {verb} failed ({process.ExitCode}): {stderr.GetAwaiter().GetResult().Trim()}");
            return output.ToArray();
        }
    }
}
