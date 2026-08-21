using Sbroglione.Services;

namespace Sbroglione.Tests;

/// <summary>
/// Verifica che la fingerprint della host key SFTP sia calcolata esattamente come
/// <c>ssh-keygen -lf</c> (SHA-256, base64 senza padding, prefisso "SHA256:"): il valore
/// viene confrontato con quello salvato nel profilo, quindi un cambio di formato
/// farebbe fallire ogni riconnessione o, peggio, mascherebbe un cambio di host key.
/// </summary>
public class SftpHostKeyFingerprintTests
{
    // Chiave ed25519 reale; la fingerprint attesa è l'output di `ssh-keygen -lf` sulla stessa chiave.
    private const string HostKeyBase64 =
        "AAAAC3NzaC1lZDI1NTE5AAAAIILQVq2R/q3qyO0srLhw+KTNJ4noE9jeZ7VeMZGsLPSm";
    private const string ExpectedFingerprint =
        "SHA256:1pIoydAzWI86AG/Km3WljFhNV9FessrHenbiNW2rqb4";

    [Fact]
    public void ComputeSha256Fingerprint_MatchesOpenSshFormat()
    {
        var hostKey = Convert.FromBase64String(HostKeyBase64);

        var fingerprint = SftpRemoteClient.ComputeSha256Fingerprint(hostKey);

        Assert.Equal(ExpectedFingerprint, fingerprint);
    }

    [Fact]
    public void ComputeSha256Fingerprint_HasNoBase64Padding()
    {
        // 32 byte di hash => 44 caratteri base64 con un '=' di padding, che va rimosso.
        var fingerprint = SftpRemoteClient.ComputeSha256Fingerprint(new byte[] { 1, 2, 3 });

        Assert.StartsWith("SHA256:", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain('=', fingerprint);
    }

    [Fact]
    public void ComputeSha256Fingerprint_DiffersForDifferentKeys()
    {
        var a = SftpRemoteClient.ComputeSha256Fingerprint(Convert.FromBase64String(HostKeyBase64));
        var b = SftpRemoteClient.ComputeSha256Fingerprint([.. Convert.FromBase64String(HostKeyBase64), (byte)0]);

        Assert.NotEqual(a, b);
    }
}
