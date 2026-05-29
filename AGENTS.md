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
- Debug: `bin\Debug\net8.0-windows10.0.19041.0\`
- Release: `bin\Release\net8.0-windows10.0.19041.0\`

## Installer
- Inno Setup Script: `installer.iss`
- Erwartet vorherigen Release-Build (liest Version aus `bin\Release\net8.0-windows10.0.19041.0\GameLauncher.exe`)

## Wichtige Bereiche
- UI: `MainWindow.xaml`, `OverlayWindow.xaml`
- Logik: `MainWindow.xaml.cs`, `ViewModels/`
- Hardware‑Monitoring: `Services/HardwareMonitorService.cs`
- Spielzeit: `Services/PlayTimeService.cs`

## Konventionen
- Keine wilden Eingriffe in bestehende UX/Design ohne Absprache.
- Änderungen an Status/Overlay immer gegen Binding prüfen.
- Neue Dateien bevorzugt unter `Services/`, `ViewModels/`, `Models/`.
- Pushes nur auf `main`, nicht auf `master`.
