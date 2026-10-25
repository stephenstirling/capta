; Capta setup (Inno Setup 6): a thin installer around the self-signed MSIX bundle.
;
; It doesn't copy Capta anywhere itself. It trusts the certificate that signed this release's bundle (as an
; administrator), then installs the bundle for the user who ran setup. See docs/releasing.md.
;
; Build: ISCC /DAppVersion=1.1.0 /DBundlePath=<signed .msixbundle> /DCertPath=<.cer> /O<dir> installer\Capta.iss
; (.github/workflows/release.yml does this.)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PackageName
  #define PackageName "StephenStirling.Capta"
#endif
#ifndef BundlePath
  #error Pass /DBundlePath=<the signed .msixbundle>
#endif
#ifndef CertPath
  #error Pass /DCertPath=<the .cer that signed it>
#endif

[Setup]
AppId={{5E2E4DB3-3976-4DE2-B9CB-C09231475273}
AppName=Capta
AppVersion={#AppVersion}
AppVerName=Capta {#AppVersion}
AppPublisher=Stephen Stirling
AppPublisherURL=https://github.com/stephenstirling/capta
AppSupportURL=https://github.com/stephenstirling/capta/issues
CreateAppDir=no
DisableProgramGroupPage=yes
DisableDirPage=yes
Uninstallable=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallMode=x64compatible
MinVersion=10.0.22621
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\Stirling.Shared\Assets\Capta\Capta.ico
OutputBaseFilename=Capta-Setup-{#AppVersion}-x64

[Messages]
WelcomeLabel1=Install Capta {#AppVersion}
WelcomeLabel2=Capta is a fast, HDR-correct screen-capture app.%n%nThis setup isn't signed by a certificate Windows already trusts. It first adds this release's one-time certificate to this PC's Trusted People store, then installs Capta for your account.%n%nOnly continue if you downloaded it from github.com/stephenstirling/capta.
FinishedHeadingLabel=Capta is installed
FinishedLabel=Capta is running in the notification area. Press Print Screen to take a capture. To remove it later, use Installed apps in Windows Settings.

[Files]
Source: "{#BundlePath}"; DestDir: "{tmp}"; DestName: "Capta.msixbundle"; Flags: ignoreversion
Source: "{#CertPath}"; DestDir: "{tmp}"; DestName: "Capta.cer"; Flags: ignoreversion
Source: "Install-Capta.ps1"; DestDir: "{tmp}"; Flags: ignoreversion

[Code]
// Runs one step of Install-Capta.ps1: 'Trust' as the administrator running setup, 'Install' as the user who
// started it (the package is installed for that user, not for the administrator account).
function RunStep(const Step: String; AsOriginalUser: Boolean): Integer;
var
  Params: String;
  ResultCode: Integer;
  Started: Boolean;
begin
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Install-Capta.ps1') + '"' +
    ' -Step ' + Step + ' -Folder "' + ExpandConstant('{tmp}') + '" -PackageName "{#PackageName}"';
  if AsOriginalUser then
    Started := ExecAsOriginalUser('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
  else
    Started := Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not Started then
    ResultCode := -1;
  Log('Install-Capta.ps1 ' + Step + ' finished with code ' + IntToStr(ResultCode));
  Result := ResultCode;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep <> ssPostInstall then
    Exit;

  WizardForm.StatusLabel.Caption := 'Trusting this release''s certificate...';
  Code := RunStep('Trust', False);
  if Code <> 0 then
  begin
    SuppressibleMsgBox('Capta could not be installed: the certificate could not be trusted (code ' +
      IntToStr(Code) + '). Details: %TEMP%\Capta-Setup.log', mbCriticalError, MB_OK, IDOK);
    Abort;
  end;

  WizardForm.StatusLabel.Caption := 'Installing Capta...';
  Code := RunStep('Install', True);
  if Code <> 0 then
  begin
    SuppressibleMsgBox('Capta could not be installed (code ' + IntToStr(Code) + '). Details: %TEMP%\Capta-Setup.log',
      mbCriticalError, MB_OK, IDOK);
    Abort;
  end;
end;
