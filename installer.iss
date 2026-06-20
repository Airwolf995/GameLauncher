; Inno Setup Script for Game Launcher

#define MyAppName "Game Launcher"
#define MyAppPublisher "Airwolf99"
#define MyAppExeName "GameLauncher.exe"
#define MyAppBuildDir "publish\win-x64"
#define MyAppVersion GetFileVersion(AddBackslash(MyAppBuildDir) + MyAppExeName)

[Setup]
AppId={{8F4A2E1D-9B3C-4F7A-A8E2-5D6C9B1A3F4E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=installer_output
OutputBaseFilename=GameLauncher_Setup_{#MyAppVersion}
SetupIconFile=game.ico
Compression=lzma
CompressionThreads=auto
SolidCompression=yes
WizardStyle=modern
WizardResizable=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Mit Windows starten"; GroupDescription: "Weitere Optionen:"; Flags: unchecked

[Files]
; IMPORTANT: Install only the curated publish output.
Source: "{#MyAppBuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"
; Install README to Documents folder
Source: "ANLEITUNG_CONFIG.txt"; DestDir: "{userdocs}\GameLauncher"; Flags: ignoreversion


[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Autostart via HKCU Run key (cleaner than Startup folder shortcut)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; \
Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; \
Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
Flags: nowait postinstall
