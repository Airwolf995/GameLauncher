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
- Antworten, Kommentare, Logging- und UI-Texte grundsätzlich mit korrekten deutschen Umlauten schreiben, sofern keine technische Einschränkung dagegen spricht.
- Für Releases bevorzugt den Publish-Workflow nutzen, nicht direkt aus `bin\Release\` paketieren.
- `main` ist der stabile Ziel-Branch; nicht mit `master` arbeiten.

## Git-Workflow
- Änderungen thematisch getrennt committen, nicht blind alle geänderten Dateien in einen Sammel-Commit ziehen.
- Neue Arbeiten zuerst lokal auf einem eigenen Branch beginnen, z. B. `codex/update-fix` oder `feature/xbox-scan`.
- Mehrere parallele Branches sind in Ordnung, solange jeder Branch nur ein klares Thema oder Ticket behandelt.
- Branches erst pushen, wenn der Stand sinnvoll testbar oder reviewbar ist; unfertige Experimente können lokal bleiben.
- Der normale Ablauf ist: auf einem Feature- oder Fix-Branch arbeiten, diesen Branch auf Remote pushen und anschließend einen Pull Request nach `main` erstellen.
- Änderungen nicht direkt auf `main` pushen, solange es nicht ausdrücklich so gewünscht ist. `main` bleibt der stabile Integrations-Branch.
- Vor dem Wechsel zwischen Branches möglichst committen oder staschen, damit keine ungeplanten lokalen Mischstände entstehen.
- Gepushte Arbeits-Branches per Pull Request nach `main` mergen, statt neue Arbeit direkt auf `main` zu starten.
- Doku-, Installer-/Build- und Feature-/Bugfix-Änderungen nach Möglichkeit in getrennten Commits oder Branches halten.
- Vor dem Push immer kurz `git status` prüfen und nur die bewusst gemeinten Dateien committen.
- Nur lokal geführte Hilfs- oder Archivdateien nicht committen, z. B. eine private lokal ausgeblendete `CHANGELOG.md`.
