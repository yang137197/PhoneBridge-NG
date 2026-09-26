#ifndef AppVersion
  #error AppVersion must be supplied by Build-LocalDelivery.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by Build-LocalDelivery.ps1
#endif
#ifndef WinFspMsi
  #error WinFspMsi must be supplied by Build-LocalDelivery.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-LocalDelivery.ps1
#endif
#ifndef AppIcon
  #error AppIcon must be supplied by Build-LocalDelivery.ps1
#endif

#define AppExe "PhoneBridge.Desktop.exe"
#define WinFspFile "winfsp-2.1.25156.msi"

[Setup]
AppId={{937C0A0D-1CE2-41C6-B750-0C7137829D0A}
AppName=PhoneBridge NG
AppVersion={#AppVersion}
AppVerName=PhoneBridge NG {#AppVersion}
AppPublisher=PhoneBridge NG contributors
DefaultDirName={localappdata}\Programs\PhoneBridge NG
DefaultGroupName=PhoneBridge NG
UninstallDisplayName=PhoneBridge NG
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile={#AppIcon}
OutputDir={#OutputDir}
OutputBaseFilename=PhoneBridge-NG-Setup-{#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=PhoneBridge NG
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription=PhoneBridge NG installer
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.22000
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
CloseApplications=no
RestartApplications=no
AppMutex=Local\PhoneBridge-NG-Installer-Guard
SetupLogging=yes
DisableProgramGroupPage=yes
AllowNoIcons=yes
LicenseFile={#PublishDir}\LICENSE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.WinFspInstallFailed=WinFsp installation did not complete (exit code %1). PhoneBridge NG was not installed.
english.WinFspMissingAfterInstall=WinFsp still could not be detected after its installer completed. PhoneBridge NG was not installed.
chinesesimplified.WinFspInstallFailed=WinFsp 安装未完成（退出代码 %1）。PhoneBridge NG 未安装。
chinesesimplified.WinFspMissingAfterInstall=WinFsp 安装程序完成后仍未检测到驱动。PhoneBridge NG 未安装。

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WinFspMsi}"; DestName: "{#WinFspFile}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\PhoneBridge NG"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,PhoneBridge NG}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValue = 'PhoneBridge-NG';
  StartupShortcutName = 'PhoneBridge NG.lnk';

function WinFspInstalled(): Boolean;
var
  InstallDir: String;
begin
  Result := RegQueryStringValue(HKLM32, 'SOFTWARE\WinFsp', 'InstallDir', InstallDir) and
    FileExists(AddBackslash(InstallDir) + 'bin\winfsp-x64.dll');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  MsiPath: String;
begin
  Result := '';
  if WinFspInstalled() then
    exit;

  ExtractTemporaryFile('{#WinFspFile}');
  MsiPath := ExpandConstant('{tmp}\{#WinFspFile}');
  if not ShellExec('runas', ExpandConstant('{sys}\msiexec.exe'),
    '/i "' + MsiPath + '" /passive /norestart', '', SW_SHOWNORMAL,
    ewWaitUntilTerminated, ResultCode) then
  begin
    Result := FmtMessage(CustomMessage('WinFspInstallFailed'), [IntToStr(ResultCode)]);
    exit;
  end;

  if (ResultCode = 1641) or (ResultCode = 3010) then
    NeedsRestart := True
  else if ResultCode <> 0 then
  begin
    Result := FmtMessage(CustomMessage('WinFspInstallFailed'), [IntToStr(ResultCode)]);
    exit;
  end;

  if not WinFspInstalled() then
    Result := CustomMessage('WinFspMissingAfterInstall');
end;

function StartupShortcutMatchesInstall(): Boolean;
var
  Shell: Variant;
  Shortcut: Variant;
  ShortcutPath: String;
begin
  Result := False;
  ShortcutPath := ExpandConstant('{userstartup}\') + StartupShortcutName;
  if not FileExists(ShortcutPath) then
    exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    Shortcut := Shell.CreateShortcut(ShortcutPath);
    Result := (CompareText(Shortcut.TargetPath, ExpandConstant('{app}\{#AppExe}')) = 0) and
      (CompareText(Shortcut.Arguments, '--startup') = 0);
  except
    Result := False;
  end;
end;

procedure MigrateLegacyRunStartup();
var
  ExistingValue: String;
  ExpectedValue: String;
  ExePath: String;
  ShortcutPath: String;
  CreatedShortcutPath: String;
begin
  ExePath := ExpandConstant('{app}\{#AppExe}');
  ExpectedValue := '"' + ExePath + '" --startup';
  if not RegQueryStringValue(HKCU, RunKey, RunValue, ExistingValue) or
    (CompareText(ExistingValue, ExpectedValue) <> 0) then
    exit;

  ShortcutPath := ExpandConstant('{userstartup}\') + StartupShortcutName;
  if FileExists(ShortcutPath) then
    exit;
  CreatedShortcutPath := CreateShellLink(ShortcutPath, 'PhoneBridge NG', ExePath, '--startup',
    ExpandConstant('{app}'), ExePath, 0, SW_SHOWNORMAL);
  if (CompareText(CreatedShortcutPath, ShortcutPath) = 0) and StartupShortcutMatchesInstall() then
    RegDeleteValue(HKCU, RunKey, RunValue);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    MigrateLegacyRunStartup();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExistingValue: String;
  ExpectedValue: String;
  ShortcutPath: String;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  ExpectedValue := '"' + ExpandConstant('{app}\{#AppExe}') + '" --startup';
  if RegQueryStringValue(HKCU, RunKey, RunValue, ExistingValue) and
    (CompareText(ExistingValue, ExpectedValue) = 0) then
    RegDeleteValue(HKCU, RunKey, RunValue);

  ShortcutPath := ExpandConstant('{userstartup}\') + StartupShortcutName;
  if StartupShortcutMatchesInstall() then
    DeleteFile(ShortcutPath);
end;
