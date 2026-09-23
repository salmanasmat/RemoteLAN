; Inno Setup 6 Script for RemoteLAN
; Compliant with AGENTS.md requirements

#define MyAppName "RemoteLAN"
#define MyAppVersion "1.3.4"
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

[Dirs]
Name: "{commonappdata}\{#MyAppName}"; Permissions: users-full

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Configure automatic Windows startup in background mode
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --background --server"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser unchecked

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
    // If autostart task was chosen, create elevated system Task Scheduler job for boot and lock screen access
    if WizardIsTaskSelected('autostart') then
    begin
      Exec('schtasks.exe', '/Create /F /TN "RemoteLAN_Autostart" /TR ' + Chr(34) + '\"' + ExpandConstant('{app}\{#MyAppExeName}') + '\" --background --server' + Chr(34) + ' /SC ONSTART /RU "NT AUTHORITY\SYSTEM" /RL HIGHEST', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec('powershell.exe', '-NoProfile -ExecutionPolicy Bypass -Command "$t = Get-ScheduledTask -TaskName ''RemoteLAN_Autostart'' -ErrorAction SilentlyContinue; if ($t) { $t.Settings.DisallowStartIfOnBatteries = $false; $t.Settings.StopIfGoingOnBatteries = $false; $t.Settings.ExecutionTimeLimit = ''PT0S''; try { $b = New-ScheduledTaskTrigger -AtStartup; $l = New-ScheduledTaskTrigger -AtLogOn; $t.Triggers = @($b, $l) } catch {}; Set-ScheduledTask $t }"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

// If user finishes setup without launching the UI, ensure background instance is running
procedure DeinitializeSetup();
var
  ResultCode: Integer;
begin
  if not CheckForMutexes('Local\RemoteLAN_SingleInstance_Mutex') then
  begin
    ShellExec('open', ExpandConstant('{app}\{#MyAppExeName}'), '--background --server', '', SW_HIDE, ewNoWait, ResultCode);
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
