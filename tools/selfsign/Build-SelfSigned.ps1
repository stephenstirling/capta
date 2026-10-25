<#
.SYNOPSIS
    Builds Capta as a self-signed Release package and installs it on this PC.

.DESCRIPTION
    For your own PC (or a few you control), not for giving to other people: Windows only installs a package
    whose signer it trusts, so this makes a certificate for the package's publisher, signs the .msixbundle with
    it, and (after you've trusted the certificate once, as an administrator) installs it. See
    docs/selfsigned-install.md.

    Steps: find or create the certificate (CurrentUser\My) -> build the Release x64 bundle like the release
    workflow does -> sign it -> check the certificate is trusted -> remove the installed Capta and install this one.

.PARAMETER InstallOnly
    Skip the build and install the bundle already in -OutputDir (use this after trusting the certificate).

.PARAMETER SkipInstall
    Build and sign only.

.PARAMETER OutputDir
    Where the signed bundle and the .cer go. Keep it on a local disk.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $env:USERPROFILE '.stirling-build\capta-selfsigned'),
    [switch]$InstallOnly,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifestPath = Join-Path $repo 'src\Capta\Package.appxmanifest'

# The certificate's subject must equal the package's Publisher exactly.
[xml]$manifest = Get-Content $manifestPath -Raw
$subject = $manifest.Package.Identity.Publisher
$packageName = $manifest.Package.Identity.Name
$cerPath = Join-Path $OutputDir 'Capta-selfsigned.cer'
New-Item -ItemType Directory -Force $OutputDir | Out-Null

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-SigningCertificate {
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) -and
            ($_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')
        } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
}

function Find-SignTool {
    $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw 'signtool.exe not found. Install the Windows SDK (it comes with Visual Studio''s .NET desktop workload).' }
    $found.FullName
}

# ---- 1. Certificate ----
$cert = Get-SigningCertificate
if (-not $cert) {
    Write-Host "Creating a self-signed certificate for $subject (valid 5 years, stored for this user only)..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
        -FriendlyName 'Capta self-signed (this PC only)' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Certificate: $($cert.Thumbprint)"

$bundle = $null
if (-not $InstallOnly) {
    # ---- 2. Build ----
    if (-not (Get-Command msbuild -ErrorAction SilentlyContinue)) {
        $vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
        Import-Module "$vs\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
        Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -Arch amd64 | Out-Null
    }
    $started = Get-Date
    Push-Location $repo
    try {
        # The same build the release workflow runs (docs/store-submission.md), for x64 only and unsigned;
        # signing is done below so the certificate never has to be known to MSBuild.
        msbuild src\Capta\Capta.csproj -restore -m -p:Configuration=Release -p:Platform=x64 `
            -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=StoreUpload `
            -p:AppxBundle=Always '-p:AppxBundlePlatforms=x64' -p:AppxPackageSigningEnabled=false -v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
    }
    finally { Pop-Location }

    $searchRoots = @((Join-Path $repo 'src\Capta\bin'), (Join-Path $env:USERPROFILE '.stirling-build\capta\Capta\bin')) |
        Where-Object { Test-Path $_ }
    $built = Get-ChildItem $searchRoots -Recurse -Filter *.msixbundle |
        Where-Object { $_.LastWriteTime -ge $started } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $built) { throw 'The build produced no .msixbundle.' }
    $bundle = Join-Path $OutputDir $built.Name
    Copy-Item $built.FullName $bundle -Force

    # ---- 3. Sign ----
    & (Find-SignTool) sign /fd SHA256 /sha1 $cert.Thumbprint /s My $bundle
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed to sign the bundle.' }
    $signer = (Get-AuthenticodeSignature $bundle).SignerCertificate
    if (-not $signer -or $signer.Thumbprint -ne $cert.Thumbprint) { throw 'The bundle is not signed with the new certificate.' }
    Write-Host "Signed: $bundle"
}
else {
    $bundle = Get-ChildItem $OutputDir -Filter *.msixbundle | Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $bundle) { throw "No .msixbundle in $OutputDir. Run without -InstallOnly first." }
}

if ($SkipInstall) { return }

# ---- 4. Trust ----
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint
if (-not $trusted) {
    if (Test-Admin) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Write-Host 'Trusted the certificate on this PC (Trusted People).'
    }
    else {
        Write-Host ''
        Write-Host 'Windows won''t install the package until this PC trusts the certificate. Once, in an ADMINISTRATOR PowerShell:'
        Write-Host ''
        Write-Host "    Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
        Write-Host ''
        Write-Host 'Then run this script again with -InstallOnly (in a normal PowerShell).'
        exit 2
    }
}

# ---- 5. Install ----
Get-Process Capta -ErrorAction SilentlyContinue | Stop-Process -Force
$installed = Get-AppxPackage $packageName
if ($installed) {
    # The same identity can't be installed twice (this also replaces a Developer Mode build). Keep its settings if we can.
    try { $installed | Remove-AppxPackage -PreserveApplicationData }
    catch {
        Write-Warning 'Could not keep Capta''s settings and Recent captures; removing it without them.'
        $installed | Remove-AppxPackage
    }
}
Add-AppxPackage -Path $bundle
$package = Get-AppxPackage $packageName
if (-not $package) { throw 'Capta is not installed after Add-AppxPackage.' }
Write-Host "Installed $($package.PackageFullName)"
if (-not $env:CI) { Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App" }
