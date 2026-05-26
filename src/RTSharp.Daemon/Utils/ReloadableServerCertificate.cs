using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RTSharp.Daemon.Utils;

public sealed class ReloadableServerCertificate
{
    private readonly Lock Lock = new();
    private readonly string PublicPemPath;
    private readonly string PrivatePemPath;

    public X509Certificate2 Current { get; private set; }

    public ReloadableServerCertificate((string Public, string Private) CertificatePaths)
    {
        PublicPemPath = CertificatePaths.Public;
        PrivatePemPath = CertificatePaths.Private;
        Current = LoadCertificate();
    }

    public void Reload()
    {
        lock (Lock) {
            X509Certificate2 newCertificate;

            try {
                newCertificate = LoadCertificate();
            } catch (Exception ex) {
                Console.WriteLine($"Failed to reload server certificate from {PublicPemPath} and {PrivatePemPath}: {ex}");
                return;
            }

            Current = newCertificate;
        }
    }

    private X509Certificate2 LoadCertificate()
    {
        var publicPem = File.ReadAllText(PublicPemPath);
        var privatePem = File.ReadAllText(PrivatePemPath);
        return X509Certificate2.CreateFromPem(publicPem, privatePem);
    }
}
