using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RTSharp.Daemon.Utils;

public sealed class ReloadableServerCertificate
{
    private readonly Lock Lock = new();
    private readonly string PublicPemPath;
    private readonly string PrivatePemPath;

    public X509Certificate2? Current { get; private set; }

    public ReloadableServerCertificate((string? Public, string? Private) CertificatePaths)
    {
        PublicPemPath = CertificatePaths.Public!;
        PrivatePemPath = CertificatePaths.Private!;
    }

    public bool Load()
    {
        lock (Lock) {
            var newCertificate = LoadCertificate();

            if (newCertificate == null) {
                return false;
            }

            Current = newCertificate;
            return true;
        }
    }

    private X509Certificate2? LoadCertificate()
    {
        try {
            var publicPem = File.ReadAllText(PublicPemPath);
            var privatePem = File.ReadAllText(PrivatePemPath);
            return X509Certificate2.CreateFromPem(publicPem, privatePem);
        } catch (Exception ex) {
            Console.WriteLine($"Failed to load server certificate from {PublicPemPath} and {PrivatePemPath}: {ex}");
            return null;
        }
    }
}
