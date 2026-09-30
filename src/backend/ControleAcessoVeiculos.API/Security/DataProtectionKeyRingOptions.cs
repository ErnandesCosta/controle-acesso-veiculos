namespace ControleAcessoVeiculos.API.Security;

public sealed class DataProtectionKeyRingOptions
{
    public const string SectionName = "DataProtection";

    public bool Enabled { get; init; }

    public string ApplicationName { get; init; } = "controle-acesso-veiculos";

    public string? KeyRingPath { get; init; }

    public string? CertificatePath { get; init; }

    public string? CertificatePasswordFile { get; init; }

    public void Validate(bool required)
    {
        if (!Enabled)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "DataProtection:Enabled deve ser true neste ambiente.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(ApplicationName))
        {
            throw new InvalidOperationException(
                "DataProtection:ApplicationName deve ser informado.");
        }

        ValidateAbsolutePath(KeyRingPath, "DataProtection:KeyRingPath");
        ValidateAbsolutePath(CertificatePath, "DataProtection:CertificatePath");

        ValidateAbsolutePath(
            CertificatePasswordFile,
            "DataProtection:CertificatePasswordFile");
    }

    private static void ValidateAbsolutePath(string? path, string settingName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{settingName} deve ser um caminho absoluto.");
        }
    }
}
