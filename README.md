# Game Launcher for Windows

Game Launcher ist ein Windows-Launcher für lokale Spielbibliotheken. Das Projekt bündelt Spiele aus mehreren Plattformen in einer gemeinsamen Oberfläche, ergänzt Cover und Metadaten, merkt sich deinen Bibliothekszustand lokal und bringt zusätzlich ein integriertes Hardware-Overlay für das Spielen mit.

Der Fokus liegt bewusst auf einer schnellen nativen Desktop-Anwendung statt auf einem Web-Frontend oder Cloud-Account-Zwang: Bibliothek, Einstellungen, Spielzeiten und lokale Anpassungen bleiben auf deinem Rechner.

## Was der Launcher kann

- Führt Spiele aus unterschiedlichen Quellen in einer gemeinsamen Bibliothek zusammen.
- Unterstützt automatische Scanner für mehrere Plattformen und manuelle Einträge für Sonderfälle.
- Bietet Filter, Sortierung, Tags, Favoriten und ausgeblendete Spiele für größere Sammlungen.
- Zeigt Cover, Logos und zusätzliche Metadaten an und hält diese lokal im Cache.
- Enthält ein Overlay mit Hardwarewerten und Sitzungsdauer während des Spielens.
- Nutzt einen klassischen Windows-Installer und einen sauberen Publish-Workflow für Releases.

## Features

- Erkennt Spiele aus mehreren Quellen, darunter Steam, Epic, GOG, Xbox / Game Pass, EA und Ubisoft.
- Unterstützt manuelle Einträge für EXE-Dateien und URI-Starts.
- Bietet ein Overlay mit CPU-, GPU-, RAM- und VRAM-Werten sowie Sitzungsdauer.
- Verwaltert Tags, Filter, Sortierung und unterschiedliche Karten-/Listenansichten.
- Nutzt lokale Cover-/Artwork-Caches und lädt optionale Metadaten nach.
- Bringt einen Einrichtungsassistenten, einen Windows-Installer und einen Release-Publish-Prozess mit.

## Typische Einsatzfälle

- Du willst Steam-, Epic-, GOG- und Xbox-Titel nicht mehr in getrennten Clients zusammensuchen.
- Du möchtest manuell gestartete Spiele oder Launcher-URIs zusätzlich in dieselbe Bibliothek aufnehmen.
- Du willst ein leichtgewichtiges lokales Tool, das ohne Account-Bindung arbeitet.
- Du möchtest beim Spielen per Overlay schnell CPU-, GPU- oder RAM-Werte im Blick behalten.

## Voraussetzungen

- Windows 10/11
- .NET 8 SDK

## Entwicklung

```powershell
dotnet build .\GameLauncher.csproj -c Debug
dotnet test .\GameLauncher.Tests\GameLauncher.Tests.csproj -c Debug --no-restore
```

## Release-Build

Sauberer Publish für den Installer:

```powershell
.\build-release.ps1
```

Der Installer liest anschließend aus `publish\win-x64`.

## Installer

- Inno Setup Script: [installer.iss](./installer.iss)
- Release-Output: `publish\win-x64`

## Dokumentation

- Datenschutz: [PRIVACY.md](./PRIVACY.md)
- Mitarbeit: [CONTRIBUTING.md](./CONTRIBUTING.md)
- Sicherheit: [SECURITY.md](./SECURITY.md)

## Lizenz

Dieses Projekt steht unter der [MIT-Lizenz](./LICENSE).
