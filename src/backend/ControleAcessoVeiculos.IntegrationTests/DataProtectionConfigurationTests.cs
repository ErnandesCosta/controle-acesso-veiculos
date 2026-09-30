using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ControleAcessoVeiculos.API.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ControleAcessoVeiculos.IntegrationTests;

public sealed class DataProtectionConfigurationTests
{
    [Fact]
    public void SharedEncryptedKeyRingSurvivesProviderReplacement()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"controle-acesso-data-protection-{Guid.NewGuid():N}");
        var keyRingPath = Path.Combine(testDirectory, "keys");
        var certificatePath = Path.Combine(testDirectory, "key-encryption.pfx");
        var certificatePasswordPath = Path.Combine(testDirectory, "certificate-password.txt");
        const string certificatePassword = "test-only-certificate-password";
        Directory.CreateDirectory(testDirectory);

        try
        {
            CreateCertificate(certificatePath, certificatePassword);
            File.WriteAllText(certificatePasswordPath, certificatePassword);
            var configuration = CreateConfiguration(
                keyRingPath,
                certificatePath,
                certificatePasswordPath);

            using var firstServices = CreateServiceProvider(configuration);
            var firstProtector = firstServices
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("cross-replica-test");
            var protectedPayload = firstProtector.Protect("fictional-payload");

            using var replacementServices = CreateServiceProvider(configuration);
            var replacementProtector = replacementServices
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("cross-replica-test");

            Assert.Equal(
                "fictional-payload",
                replacementProtector.Unprotect(protectedPayload));

            var keyFile = Assert.Single(Directory.GetFiles(keyRingPath, "key-*.xml"));
            var keyXml = File.ReadAllText(keyFile);
            Assert.Contains("EncryptedData", keyXml, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-payload", keyXml, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ProductionRejectsDisabledKeyRing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddConfiguredDataProtection(
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains(
            "DataProtection:Enabled",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static ServiceProvider CreateServiceProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConfiguredDataProtection(
            configuration,
            new TestHostEnvironment("Testing"));
        return services.BuildServiceProvider();
    }

    private static IConfiguration CreateConfiguration(
        string keyRingPath,
        string certificatePath,
        string certificatePasswordPath) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:Enabled"] = "true",
                ["DataProtection:ApplicationName"] = "controle-acesso-veiculos-tests",
                ["DataProtection:KeyRingPath"] = keyRingPath,
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:CertificatePasswordFile"] = certificatePasswordPath
            })
            .Build();

    private static void CreateCertificate(string path, string password)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=controle-acesso-veiculos-tests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature,
                true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(
            path,
            certificate.Export(X509ContentType.Pfx, password));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "ControleAcessoVeiculos.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
