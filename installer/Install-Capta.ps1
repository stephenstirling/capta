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

try {
    if ($Step -eq 'Trust') {
        Import-Certificate -FilePath (Join-Path $Folder 'Capta.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
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
