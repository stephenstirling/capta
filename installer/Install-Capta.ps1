<#
.SYNOPSIS
    The two steps behind Capta-Setup.exe. Run by the setup program, not by hand.

.PARAMETER Step
    Trust   - add the release's certificate to this PC's Trusted People store (needs administrator).
    Install - replace any installed Capta with the bundle and start it (runs as the user who ran setup).
#>
param(
    [Parameter(Mandatory)][ValidateSet('Trust', 'Install')][string]$Step,
    [Parameter(Mandatory)][string]$Folder,
    [string]$PackageName = 'StephenStirling.Capta'
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'Capta-Setup.log'

# Setup can be started from a shell that left PowerShell 7's module folders in PSModulePath, which Windows
# PowerShell 5.1 then can't load its own modules from. Use the machine and user defaults.
$env:PSModulePath = (@([Environment]::GetEnvironmentVariable('PSModulePath', 'Machine'),
        [Environment]::GetEnvironmentVariable('PSModulePath', 'User')) | Where-Object { $_ }) -join ';'

try {
    if ($Step -eq 'Trust') {
        # The certificate store directly (no Cert: drive, which isn't always available).
        $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $Folder 'Capta.cer')
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPeople', 'LocalMachine')
        $store.Open('ReadWrite')
        try { $store.Add($certificate) } finally { $store.Close() }
    }
    else {
        Get-Process Capta -ErrorAction SilentlyContinue | Stop-Process -Force
        $installed = Get-AppxPackage $PackageName
        if ($installed) {
            # Same identity can't be installed twice. Keep settings and Recent captures when Windows allows.
            try { $installed | Remove-AppxPackage -PreserveApplicationData }
            catch { $installed | Remove-AppxPackage }
        }
        Add-AppxPackage -Path (Join-Path $Folder 'Capta.msixbundle')
        $package = Get-AppxPackage $PackageName
        if (-not $package) { throw 'Capta is not installed after Add-AppxPackage.' }
        if (-not $env:CI) { Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App" }
    }
}
catch {
    ('{0} [{1}] {2}' -f (Get-Date -Format s), $Step, ($_ | Out-String)) | Add-Content -Path $log
    exit 1
}
