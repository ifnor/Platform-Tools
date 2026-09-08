#define MyAppName "Platform Tools"
#define MyAppVersion GetEnv("PLATFORMTOOLS_VERSION")
#define MySource GetEnv("PLATFORMTOOLS_SOURCE")
#define MyOutput GetEnv("PLATFORMTOOLS_OUTPUT")
[Setup]
AppId={{8CE9EF66-C54B-4DE5-9BD7-A4D9C466E081}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Platform Tools
DefaultGroupName=Platform Tools
OutputDir={#MyOutput}
OutputBaseFilename=PlatformTools-{#MyAppVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\PlatformTools.exe
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Files]
Source: "{#MySource}\*"; DestDir: "{app}"; Excludes: "config\*,.cloudflared\*"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\Platform Tools"; Filename: "{app}\PlatformTools.exe"
Name: "{autodesktop}\Platform Tools"; Filename: "{app}\PlatformTools.exe"; Tasks: desktopicon
[Registry]
Root: HKA; Subkey: "Software\Classes\.ptlink"; ValueType: string; ValueData: "PlatformTools.Share"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\PlatformTools.Share"; ValueType: string; ValueData: "Platform Tools Share File"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\PlatformTools.Share\shell\open\command"; ValueType: string; ValueData: """{app}\PlatformTools.exe"" ""%1"""
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标："; Flags: unchecked
[Run]
Filename: "{app}\PlatformTools.exe"; Description: "启动 Platform Tools"; Flags: nowait postinstall skipifsilent
