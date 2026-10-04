; ============================================================================
;  MultisimLauncher.iss - installer script for Inno Setup 6
;
;  Build:
;     ISCC.exe /DSourceExe=<path to MultisimLauncher.exe> /DOutputDir=<dist> MultisimLauncher.iss
;  or simply use build.ps1 -MakeInstaller
;
;  No admin rights are requested on purpose. The launcher runs as the logged on
;  user, so it must be installed per-user; an elevated Multisim would use a
;  different profile and therefore a different component database.
; ============================================================================

#ifndef SourceExe
  #define SourceExe "..\dist\MultisimLauncher.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName        "Multisim Launcher"
#define AppVersion     "1.0.0"
#define AppPublisher   "Multisim Launcher contributors"
#define AppURL         "https://github.com/"
#define AppExeName     "MultisimLauncher.exe"

[Setup]
AppId={{7E3C1A64-5B2D-4E39-9C1F-2A8D6B0E4F31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

; per-user install: no UAC prompt, works for non-admins
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\MultisimLauncher
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=no

OutputDir={#OutputDir}
OutputBaseFilename=MultisimLauncher-Setup
VersionInfoVersion=1.0.0.0
VersionInfoProductVersion=1.0.0
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile=..\LICENSE.txt
InfoBeforeFile=..\docs\install-notes.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startmenu";  Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";     DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\LICENSE.txt";   DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; Start menu
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExeName}"; Tasks: startmenu
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; Tasks: startmenu
; Desktop shortcut
Name: "{userdesktop}\{#AppName}";        Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
; Optional: start with Windows (not enabled by default, see [Run] below)

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\MultisimLauncher"

[Code]
// ---------------------------------------------------------------------------
// A light pre-install check: warn when Multisim itself is not detected, so the
// user is not left wondering why a "Multisim launcher" has nothing to launch.
// Detection mirrors MultisimLocator.Find(): registry first, then common paths.
// ---------------------------------------------------------------------------
function MultisimFound(): Boolean;
var
  Master: String;
  Candidate: String;
  Versions: TArrayOfString;
  I: Integer;
begin
  Result := False;

  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\National Instruments\Circuit Design Suite\14.3\Common',
                         'Database_Master', Master) then
  begin
    if FileExists(Master) then begin Result := True; Exit; end;
  end;

  Versions := ['15.0','14.3','14.2','14.1','14.0','13.0','12.0'];
  for I := 0 to GetArrayLength(Versions) - 1 do
  begin
    Candidate := ExpandConstant('{pf32}\National Instruments\Circuit Design Suite ') + Versions[I] + '\multisim.exe';
    if FileExists(Candidate) then begin Result := True; Exit; end;
    Candidate := ExpandConstant('{pf}\National Instruments\Circuit Design Suite ') + Versions[I] + '\multisim.exe';
    if FileExists(Candidate) then begin Result := True; Exit; end;
  end;
end;

procedure InitializeWizard();
begin
  if not MultisimFound() then
  begin
    MsgBox('NI Multisim does not appear to be installed on this computer.' + #13#10 + #13#10 +
           'This program only launches Multisim - it cannot install it.' + #13#10 +
           'You can continue, but the launcher will report "Multisim not found".',
           mbInformation, MB_OK);
  end;
end;
