; In Ex Xen all-in-one client installer (ClassicUO + Razor CE + shard UO data).
; Build: ISCC.exe InExXen.iss   -> output\InExXen-Setup.exe
; Stage folder is built from the reference install (see README.md).

#define AppName "In Ex Xen"
#define AppVersion "1.1"
#define ShardHost "play.inexxen.com"
#define ShardPort "2593"
#define ClientVersion "5.0.6.5"

[Setup]
AppId={{6F1C2E7A-4B8D-4E2A-9C3F-1E5A7D0B9C21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=In Ex Xen
AppPublisherURL=https://inexxen.com
DefaultDirName={localappdata}\InExXen
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install: ClassicUO writes settings, profiles and logs next to its exe,
; so it must not live in Program Files.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=InExXen-Setup
UninstallDisplayIcon={app}\ClassicUO.exe
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "stage\InExXen\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Razor starting profile (title-bar reagent/bandage counters). Not in the update manifest,
; so a player's own Razor settings are never overwritten.
Source: "razor-default.xml"; DestDir: "{app}\Data\Plugins\Razor\Profiles"; DestName: "default.xml"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ClassicUO.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ClassicUO.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ClassicUO.exe"; WorkingDir: "{app}"; Description: "Play {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Files ClassicUO and Razor generate at runtime. Player profiles (Data\Profiles,
; Razor profiles) are removed too so an uninstall leaves nothing behind.
Type: filesandordirs; Name: "{app}\Data"
Type: filesandordirs; Name: "{app}\Logs"
Type: files; Name: "{app}\settings.json"
Type: dirifempty; Name: "{app}"

[Code]
procedure WriteSettings();
var
  Path, UODir, Json: String;
begin
  Path := ExpandConstant('{app}\settings.json');
  { Keep an existing settings.json on reinstall/upgrade (saved account, window size). }
  if FileExists(Path) then
    exit;
  UODir := ExpandConstant('{app}\UO');
  StringChangeEx(UODir, '\', '/', True);
  Json :=
    '{' + #13#10 +
    '  "username": "",' + #13#10 +
    '  "password": "",' + #13#10 +
    '  "ip": "{#ShardHost}",' + #13#10 +
    '  "port": {#ShardPort},' + #13#10 +
    '  "ultimaonlinedirectory": "' + UODir + '",' + #13#10 +
    '  "profilespath": "",' + #13#10 +
    '  "clientversion": "{#ClientVersion}",' + #13#10 +
    '  "lang": "ENU",' + #13#10 +
    '  "lastservernum": 1,' + #13#10 +
    '  "fps": 60,' + #13#10 +
    '  "is_win_maximized": false,' + #13#10 +
    '  "saveaccount": true,' + #13#10 +
    '  "autologin": false,' + #13#10 +
    '  "reconnect": true,' + #13#10 +
    '  "reconnect_time": 3,' + #13#10 +
    '  "login_music": true,' + #13#10 +
    '  "login_music_volume": 70,' + #13#10 +
    '  "fixed_time_step": true,' + #13#10 +
    '  "use_verdata": false,' + #13#10 +
    '  "encryption": 0,' + #13#10 +
    '  "plugins": [ "Razor/Razor.exe" ]' + #13#10 +
    '}' + #13#10;
  SaveStringToFile(Path, Json, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteSettings();
end;
