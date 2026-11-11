# Releasing Capta

Capta ships two ways, with different package identities:

| | Signed by | Installed from | Updates |
| --- | --- | --- | --- |
| **Microsoft Store** | Microsoft | the Store | the Store; Capta makes no network calls |
| **GitHub release** | SignPath Foundation (free, once approved) | the `.msixbundle` on the Releases page | Capta's optional daily check for a newer release |
| **GitHub release, until SignPath approves** | a certificate made for that build (self-signed) | `Capta-Setup-<version>-x64.exe` on the Releases page | the same optional daily check |

The Store package gets its identity from **Associate App with the Store** (see
[store-submission.md](store-submission.md)). The GitHub package must name SignPath Foundation's certificate as its
publisher, so the release workflow swaps that one attribute in for its own build (`SIGNPATH_PUBLISHER`, below).
Nothing is committed for it.

## Cutting a release

1. Update `CHANGELOG.md` and the `Version` in `src/Capta/Package.appxmanifest` (`1.1.0.0` for release `v1.1.0`).
2. Merge to `main` once the **Build** check is green.
3. Tag and push: `git tag v1.1.0 && git push origin v1.1.0`.
4. The **Release package** workflow runs the tests and builds the x64 + ARM64 `.msixbundle`, uploading it as the
   `capta-unsigned-msixbundle` artifact. The unsigned bundle can't be installed, so it is never put on a release.
5. Once signing is set up (below), the `sign` job submits the bundle to SignPath, waits for you to approve the
   request, and attaches the signed bundle to a **draft** release for the tag. Don't create that draft yourself
   first: the workflow creates it. Review it, add the changelog text and the attribution line
   (`Free code signing provided by SignPath.io, certificate by SignPath Foundation.`), and publish.
6. For the Store, build the bundle from the same tag with the Store identity and upload it in Partner Center.

## The self-signed setup program (until SignPath approves)

The `installer` job in the release workflow builds `Capta-Setup-<version>-x64.exe` on every tag and attaches it to
the draft release (a manual run of the workflow keeps it as the `capta-setup` artifact instead).

- **What it is.** `tools/selfsign/Build-SelfSigned.ps1 -SkipInstall` builds the Release x64 bundle and signs it with a
  certificate (see below for which). `installer/Capta.iss` (Inno Setup) wraps the bundle and the public `.cer` in a
  setup program. Setup checks whether the PC already trusts the certificate; if not, it adds it to Trusted People
  (Windows asks for administrator approval, for that step only), then installs the bundle as the user who ran it
  (`installer/Install-Capta.ps1`). It doesn't install files of its own, so there's no uninstaller of its own: Capta
  is removed from Installed apps like any package.
- **Which certificate.** With the repository secrets `CAPTA_SIGNING_PFX` and `CAPTA_SIGNING_PFX_PASSWORD` set, every
  release is signed with that one certificate. A PC that trusted it once is never asked again, so setup runs without
  an approval prompt, which is what lets Integra update Capta quietly. Without the secrets the job makes a
  throwaway certificate for each release (the PC is asked every time).
- **Setting up the one certificate.** On your own PC, in a normal PowerShell, run
  `.\tools\selfsign\New-SigningCertificate.ps1`. It writes a PFX and prints exactly what to save as the two
  repository secrets (Settings > Secrets and variables > Actions), then how to delete the files. The next tagged
  release uses it. To replace it (it lasts 10 years, or if the key may have leaked), run the script again, save the
  new secrets, and remove the old certificate from Trusted People on PCs that have it
  ([selfsigned-install.md](selfsigned-install.md) has the commands).
- **What the one certificate trusts.** Windows accepts any package signed by a certificate in Trusted People. With
  one certificate for every release, the private key lives in a GitHub secret, so whoever can change this
  repository's workflows or secrets could sign a package that PCs trusting it install without a warning. Keep the
  repository's access small, turn on two-factor sign-in, and don't hand the PFX to anyone. Throwaway certificates
  avoid this, at the cost of an approval prompt on each install.
- **Why throwaway keys were safe.** The key existed only on that runner and was gone when the job ended, so a
  trusted certificate couldn't sign anything else. Packages from different releases still update each other:
  Windows matches them by the publisher name in the manifest (`CN=StephenStirling`), not by the certificate.
- **What users see.** SmartScreen warns about an unrecognised publisher, because the setup program itself isn't
  signed by a certificate Windows trusts. The release notes and README say so. Some antivirus products are also wary
  of unsigned installers; if one flags it, report it to the vendor.
- **The trust it leaves behind.** The certificate stays in Trusted People after Capta is removed. The commands to
  remove it are in [selfsigned-install.md](selfsigned-install.md).
- **When SignPath approves.** Follow the setup below, tag a release, and the `sign` job attaches the signed bundle.
  Then drop the `installer` job (or keep it until the signed bundle has been out for a release) and update the
  README's Install and Code signing policy sections.

## One-time SignPath setup (after approval)

SignPath Foundation emails the details when the project is approved. Then:

1. In SignPath, create the project with the slug `capta` and a signing policy with the slug `release-signing` that
   **requires manual approval**. Set up an artifact configuration for the `.msixbundle` that restricts the product
   name to `Capta` and keeps the product version the same in every file (their requirement for signed packages).
2. Link SignPath to this repository as a trusted build system (GitHub Actions), so it can verify that a package was
   built from here.
3. In the repository settings, add:
   - **Secret** `SIGNPATH_API_TOKEN`: a SignPath API token for a submitter user.
   - **Variable** `SIGNPATH_ORGANIZATION_ID`: your SignPath organization ID. This is also what switches the
     `sign` job on.
   - **Variable** `SIGNPATH_PUBLISHER`: the certificate subject exactly as SignPath shows it (for example
     `CN=SignPath Foundation, O=SignPath Foundation, …`). It becomes the package publisher for the signed build.
4. Flip the status line in the README's *Code signing policy* section from "hasn't approved the project yet" to
   active, and add the attribution line to the release notes of every release page.

## Not yet tested

The `package` job builds and runs on every change to `release.yml`. The `sign` job and the manifest publisher swap
haven't run, because they need an approved SignPath project. Expect to adjust them on the first real release:
check the artifact configuration against SignPath's MSIX documentation, and check that the signed `.msixbundle`
installs (`Add-AppxPackage`) before publishing it.
