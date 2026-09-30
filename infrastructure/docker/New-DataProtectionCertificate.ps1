[CmdletBinding()]
param(
  [string]$OutputPath = (Join-Path $PSScriptRoot 'secrets/data-protection.pfx'),

  [string]$PasswordOutputPath = (Join-Path $PSScriptRoot 'secrets/data-protection-password.txt'),

  [Security.SecureString]$Password,

  [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedParent = [IO.Path]::GetFullPath((Split-Path -Parent $OutputPath))
$resolvedOutput = Join-Path $resolvedParent (Split-Path -Leaf $OutputPath)
$resolvedPasswordParent = [IO.Path]::GetFullPath((Split-Path -Parent $PasswordOutputPath))
$resolvedPasswordOutput = Join-Path $resolvedPasswordParent (Split-Path -Leaf $PasswordOutputPath)

if (((Test-Path -LiteralPath $resolvedOutput) -or
    (Test-Path -LiteralPath $resolvedPasswordOutput)) -and -not $Force) {
  throw 'The certificate or password secret already exists. Use -Force only when intentionally rotating both files.'
}

if ($null -eq $Password) {
  $Password = Read-Host 'Choose a strong password for the local Data Protection certificate' -AsSecureString
}

$pointer = [IntPtr]::Zero
$rsa = $null
$certificate = $null
try {
  $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
  $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
  if ($plainPassword.Length -lt 16) {
    throw 'The certificate password must contain at least 16 characters.'
  }

  New-Item -ItemType Directory -Path $resolvedParent -Force | Out-Null
  New-Item -ItemType Directory -Path $resolvedPasswordParent -Force | Out-Null
  $rsa = [Security.Cryptography.RSA]::Create(3072)
  $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=controle-acesso-veiculos-local-data-protection',
    $rsa,
    [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1)
  $request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new(
      $false,
      $false,
      0,
      $true))
  $keyUsage = [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment `
    -bor [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature
  $request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
      $keyUsage,
      $true))
  $certificate = $request.CreateSelfSigned(
    [DateTimeOffset]::UtcNow.AddMinutes(-5),
    [DateTimeOffset]::UtcNow.AddYears(2))
  $bytes = $certificate.Export(
    [Security.Cryptography.X509Certificates.X509ContentType]::Pfx,
    $plainPassword)
  [IO.File]::WriteAllBytes($resolvedOutput, $bytes)
  [Array]::Clear($bytes, 0, $bytes.Length)
  [IO.File]::WriteAllText($resolvedPasswordOutput, $plainPassword)

  if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    foreach ($secretPath in @($resolvedOutput, $resolvedPasswordOutput)) {
      $acl = [Security.AccessControl.FileSecurity]::new()
      $acl.SetAccessRuleProtection($true, $false)
      $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $identity,
        [Security.AccessControl.FileSystemRights]::FullControl,
        [Security.AccessControl.AccessControlType]::Allow))
      Set-Acl -LiteralPath $secretPath -AclObject $acl
    }
  }
  else {
    & chmod 600 $resolvedOutput $resolvedPasswordOutput
    if ($LASTEXITCODE -ne 0) { throw 'Failed to restrict the password secret permissions.' }
  }

  Write-Output "Local Data Protection certificate created at '$resolvedOutput'."
  Write-Output "Password secret created at '$resolvedPasswordOutput'."
  Write-Output 'Both files are ignored by Git. Production must obtain them from an approved secret manager.'
}
finally {
  if ($null -ne $certificate) { $certificate.Dispose() }
  if ($null -ne $rsa) { $rsa.Dispose() }
  if ($pointer -ne [IntPtr]::Zero) {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
  }
  Remove-Variable plainPassword -ErrorAction SilentlyContinue
}
