; Inno Setup Script for Game Launcher

#define MyAppName "Game Launcher"
#define MyAppPublisher "Airwolf99"
#define MyAppExeName "GameLauncher.exe"
#define MyAppBuildDir "publish\win-x64"
#define MyAppVersion GetVersionNumbersString(AddBackslash(MyAppBuildDir) + MyAppExeName)

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
SetupIconFile=Resources\Images\game.ico
Compression=lzma
CompressionThreads=auto
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
DisableProgramGroupPage=yes
InfoBeforeFile=installer_info.rtf

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Mit Windows starten"; GroupDescription: "Weitere Optionen:"; Flags: unchecked

[Files]
; IMPORTANT: Install only the curated publish output.
Source: "{#MyAppBuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml"
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
; Ohne .NET-Runtime würde der Start nur eine knappe Windows-Meldung erzeugen.
Filename: "{app}\{#MyAppExeName}"; \
Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
Flags: nowait postinstall; Check: IsDotNetDesktopRuntimeInstalled

[Code]
const
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/8.0';

// Der Launcher wird ohne eigene .NET-Runtime veröffentlicht und verlangt laut
// runtimeconfig.json die Desktop Runtime 8.x; 9 und 10 nimmt er nicht.
// Geprüft wird der Installationsordner statt der Registry: auf dem
// Entwicklungsrechner standen die Einträge nur in der 32-Bit-Ansicht
// (WOW6432Node) und führten Versionen, die längst deinstalliert waren.
function IsDotNetDesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

// Die Installation selbst braucht die Runtime nicht, nur der Start. Deshalb
// wird nicht abgebrochen, sondern auf die Downloadseite verwiesen; die
// Runtime lässt sich auch nach dem Launcher installieren.
function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNetDesktopRuntimeInstalled then
  begin
    if MsgBox('Game Launcher benötigt die .NET 8 Desktop Runtime von Microsoft. ' +
              'Sie wurde auf diesem Rechner nicht gefunden; ohne sie startet der Launcher nicht.' + #13#10#13#10 +
              'Soll die Downloadseite geöffnet werden? Dort unter ".NET Desktop Runtime" ' +
              'den Installer für Windows x64 wählen.' + #13#10#13#10 +
              'Die Installation des Launchers läuft danach weiter.',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
