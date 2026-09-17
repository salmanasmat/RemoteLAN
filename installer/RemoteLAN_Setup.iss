; Inno Setup 6 Script for RemoteLAN
; Compliant with AGENTS.md requirements

#define MyAppName "RemoteLAN"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Salman Asmat"
#define MyAppExeName "RemoteLAN.exe"
#define MyAppAssocName MyAppName + " Remote Connection"

[Setup]
; Unique application identifier
AppId={{E1B64F88-3E2A-4D78-9B21-823902347281}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=RemoteLAN_Setup_v{#MyAppVersion}
SetupIconFile=..\src\RemoteLAN\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=force
CloseApplicationsFilter=RemoteLAN.exe
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=RemoteLAN Installer

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start RemoteLAN automatically with Windows (in background)"; GroupDescription: "Windows Startup:"

[Files]
; Published application binaries from bin\publish
Source: "..\bin\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Configure automatic Windows startup in background mode
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: unchecked nowait postinstall skipifsilent

[Code]
// Forcefully terminate any running instance of RemoteLAN
function KillProcess(const ExeName: string): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM ' + ExeName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Pre-installation cleanup for clean upgrades
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // 1. Forcefully close any running instance
  KillProcess('{#MyAppExeName}');
  // 2. Ensure all application processes have terminated
  Sleep(1000);
end;

// Clean out existing application files in destination directory
procedure CleanOldInstallation(const AppDir: string);
begin
  if DirExists(AppDir) then
  begin
    // Remove all previous installation files and subdirectories cleanly
    DelTree(AppDir + '\*', False, True, True);
  end;
end;

// Handle installation steps
procedure CurStepChanged(CurStep: TSetupStep);
var
  AppDir: string;
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    AppDir := ExpandConstant('{app}');
    CleanOldInstallation(AppDir);
  end
  else if CurStep = ssPostInstall then
  begin
    // If autostart task was chosen, create elevated Task Scheduler job for lock screen access
    if WizardIsTaskSelected('autostart') then
    begin
      Exec('schtasks.exe', '/Create /F /TN "RemoteLAN_Autostart" /TR """' + ExpandConstant('{app}\{#MyAppExeName}') + '"" --background" /SC ONLOGON /RL HIGHEST', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;

    // Start automatically in the background without displaying the main application window
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--background', '', SW_HIDE, ewNoWait, ResultCode);
  end;
end;

// Ensure process is terminated before uninstalling
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('schtasks.exe', '/Delete /F /TN "RemoteLAN_Autostart"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;
