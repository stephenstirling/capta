# Self-signed install, for your own PC

Windows only installs an MSIX package whose signer it trusts. Until Capta's releases are signed through SignPath
Foundation (see [releasing.md](releasing.md)) or the Store, you can sign a build with a certificate you make
yourself and tell **your own PC** to trust it. This installs a real Release package, the same one the release
workflow builds, instead of the Developer Mode build you get from Visual Studio.

**Only do this on PCs you control.** Trusting the certificate means that PC will accept any package signed with it.
The private key stays in your user account on the PC that made it; don't copy the `.cer` to other people's PCs and
don't publish the `.msixbundle`. For other people, use the Store or the signed GitHub release.

## Steps

You need Visual Studio (or the Build Tools) with the .NET desktop workload and the Windows SDK, as in the README's
Build section.

1. In a **normal** PowerShell window (not administrator, so a mapped drive like `Z:` is visible):

   ```powershell
   cd Z:\Data\sites\capta
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   .\tools\selfsign\Build-SelfSigned.ps1
   ```

   The `Set-ExecutionPolicy` line is needed if Windows says "running scripts is disabled on this system". It only
   applies to that PowerShell window and changes nothing permanent. Use it again in each new window (including the
   `-InstallOnly` run in step 3).

   It creates the certificate (the first time), builds the Release x64 bundle, signs it, and then stops with one
   command to run, because trusting a certificate needs an administrator.

2. In an **administrator** PowerShell, paste the command it printed. It looks like this:

   ```powershell
   Import-Certificate -FilePath "$env:USERPROFILE\.stirling-build\capta-selfsigned\Capta-selfsigned.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Back in the normal window:

   ```powershell
   .\tools\selfsign\Build-SelfSigned.ps1 -InstallOnly
   ```

   It removes the Capta that's installed (keeping its settings and Recent captures when Windows allows), installs the
   signed one and starts it. Next time, step 1 alone is enough: the certificate is already trusted, so it builds,
   signs and installs.

The signed bundle and the `.cer` are in `%USERPROFILE%\.stirling-build\capta-selfsigned`, outside the repository.

## Things to know

- **The certificate's subject has to equal the manifest's `Publisher`** (`CN=StephenStirling`). The script reads it
  from `Package.appxmanifest`, so if you later associate the app with the Store (which changes the publisher),
  it makes a new certificate and you trust that one instead.
- **This package and a Developer Mode build can't be installed together.** They share one identity. The script
  removes whichever is installed. Pressing F5 in Visual Studio later replaces this package the same way.
- **Windows updates it only if the version number goes up.** The script removes and reinstalls instead, so
  re-running it with the same version works.
- **Capta's update check will still say "You have the latest version"** until a real release is published. It only
  reads GitHub Releases.
- The certificate lasts 5 years. After that the script makes a new one and you trust it again.

## Taking it back out

In an administrator PowerShell, then in a normal one:

```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=StephenStirling' | Remove-Item
```

```powershell
Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=StephenStirling' | Remove-Item
Get-AppxPackage StephenStirling.Capta | Remove-AppxPackage
```
