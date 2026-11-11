<#
.SYNOPSIS
    Makes the one certificate Capta's releases are signed with, and says how to give it to GitHub.

.DESCRIPTION
    Run once, on your own PC. Releases are built and signed by .github/workflows/release.yml; with this
    certificate in two repository secrets, every release is signed with the same one, so a PC that trusted it
    once (setup asks for administrator approval that first time) never needs to again, and Integra can update
    Capta without a prompt. Without the secrets the workflow makes a throwaway certificate for each release,
    as before. See docs/releasing.md for what this trusts and how to replace it.

    The certificate's subject is read from the package's Publisher, because Windows only accepts a package whose
    signer's subject equals it. The private key goes into the PFX file this writes (and stays in this
    user's certificate store); the file's password is random and shown once.

.PARAMETER OutputDir
    Where the PFX, its base64 text and the public .cer go. Keep it on a local disk, and delete the PFX and the
    text once the secrets are saved.

.PARAMETER Years
    How long the certificate lasts.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $env:USERPROFILE '.stirling-build\capta-signing'),
    [ValidateRange(1, 20)][int]$Years = 10
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
[xml]$manifest = Get-Content (Join-Path $repo 'src\Capta\Package.appxmanifest') -Raw
$subject = $manifest.Package.Identity.Publisher
New-Item -ItemType Directory -Force $OutputDir | Out-Null

$bytes = New-Object byte[] 24
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$passwordText = [Convert]::ToBase64String($bytes)
$password = ConvertTo-SecureString $passwordText -AsPlainText -Force

$cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
    -FriendlyName 'Capta release signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears($Years) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

$pfx = Join-Path $OutputDir 'Capta-signing.pfx'
$cer = Join-Path $OutputDir 'Capta-signing.cer'
$text = Join-Path $OutputDir 'CAPTA_SIGNING_PFX.txt'
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)) | Set-Content -Path $text -NoNewline

Write-Host ''
Write-Host "Certificate: $subject  thumbprint $($cert.Thumbprint)  expires $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ''
Write-Host 'Now, on GitHub: stephenstirling/capta > Settings > Secrets and variables > Actions > New repository secret.'
Write-Host '  1. Name: CAPTA_SIGNING_PFX             Value: the whole contents of'
Write-Host "       $text"
Write-Host '  2. Name: CAPTA_SIGNING_PFX_PASSWORD    Value (shown only now):'
Write-Host "       $passwordText"
Write-Host ''
Write-Host 'When both are saved, delete the PFX and the text file (the key is also in your certificate store):'
Write-Host "       Remove-Item '$pfx', '$text'"
Write-Host ''
Write-Host 'The next release is signed with this certificate. The public part is here, for people who want to compare:'
Write-Host "       $cer"
