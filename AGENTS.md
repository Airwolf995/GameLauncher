# AGENTS.md

Kurzleitfaden für Arbeiten am Projekt.

## Projekt
- WPF‑Launcher für Spiele (Windows, .NET 8)
- Hauptprojekt: `GameLauncher.csproj`

## Build
```powershell
dotnet build .\GameLauncher.csproj -c Debug
dotnet build .\GameLauncher.csproj -c Release
```

## Output-Ordner
- Debug-Build: `bin\Debug\net8.0-windows10.0.19041.0\`
- Release-Build: `bin\Release\net8.0-windows10.0.19041.0\`
- Installer-/Publish-Output: `publish\win-x64\`

## Publish
```powershell
.\build-release.ps1
```

## Installer
- Inno Setup Script: `installer.iss`
- Erwartet vorherigen Publish nach `publish\win-x64\`
- Liest Version und Dateien aus `publish\win-x64\`

## Wichtige Bereiche
- UI: `MainWindow.xaml`, `OverlayWindow.xaml`
- Logik: `MainWindow.xaml.cs`, `ViewModels/`
- Hardware‑Monitoring: `Services/HardwareMonitorService.cs`
- Spielzeit: `Services/PlayTimeService.cs`
- Fenster-/UI-Orchestrierung: `Services/MainWindow/`
- Virtualisierte Bibliotheksansicht: `Controls/VirtualizingWrapPanel.cs`
- Einstellungen & Pfadpflege: `ViewModels/SettingsViewModel.cs`

## Orientierungsstruktur
- `Services/MainWindow/`: Hauptfenster-spezifische UI-Orchestrierung wie Animationen, Tray, Overlay, Statusmeldungen und Update-Ablauf.
- `Services/Scanners/`: Plattform-Scanner für Steam, Epic, GOG, Xbox / Game Pass, EA und Ubisoft.
- `Services/Localization/`: Sprachlogik und lokalisierte Texte.
- `Controls/`: Spezielle WPF-Controls, insbesondere Virtualisierung für die Bibliotheksansicht.
- `ViewModels/`: UI-Zustand und Einstellungslogik.
- `Models/`: Konfiguration, Spielmodelle, Konstanten und zentrale Zustandsobjekte.
- `GameLauncher.Tests/`: Unit- und Integrationstests.

## Konventionen
- Keine wilden Eingriffe in bestehende UX/Design ohne Absprache.
- Änderungen an Status/Overlay immer gegen Binding prüfen.
- Neue Dateien bevorzugt unter `Services/`, `ViewModels/`, `Models/`.
- Für Releases bevorzugt den Publish-Workflow nutzen, nicht direkt aus `bin\Release\` paketieren.
- Pushes nur auf `main`, nicht auf `master`.

## Git-Workflow
- Änderungen thematisch getrennt committen, nicht blind alle geänderten Dateien in einen Sammel-Commit ziehen.
- Für neue Arbeiten bevorzugt auf einem Branch arbeiten und per Pull Request nach `main` mergen.
- Doku-, Installer-/Build- und Feature-/Bugfix-Änderungen nach Möglichkeit in getrennten Commits oder Branches halten.
- Vor dem Push immer kurz `git status` prüfen und nur die bewusst gemeinten Dateien committen.
- Nur lokal geführte Hilfs- oder Archivdateien nicht committen, z. B. eine private lokal ausgeblendete `CHANGELOG.md`.
