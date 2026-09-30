using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace ControleAcessoVeiculos.API.Security;

public static class DataProtectionConfiguration
{
    public static IServiceCollection AddConfiguredDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = configuration
            .GetSection(DataProtectionKeyRingOptions.SectionName)
            .Get<DataProtectionKeyRingOptions>() ?? new DataProtectionKeyRingOptions();
        var required = environment.IsProduction() ||
            environment.IsEnvironment("LocalContainer");
        options.Validate(required);

        if (!options.Enabled)
        {
            return services;
        }

        Directory.CreateDirectory(options.KeyRingPath!);

        var certificatePassword = File.ReadAllText(options.CertificatePasswordFile!).Trim();
        if (string.IsNullOrWhiteSpace(certificatePassword))
        {
            throw new InvalidOperationException(
                "O secret com a senha do certificado de Data Protection está vazio.");
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.CertificatePath!,
            certificatePassword,
            X509KeyStorageFlags.EphemeralKeySet);
        certificatePassword = string.Empty;
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "O certificado de Data Protection deve possuir chave privada.");
        }

        services.AddSingleton(certificate);
        services
            .AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(options.KeyRingPath!))
            .ProtectKeysWithCertificate(certificate);

        return services;
    }
}
