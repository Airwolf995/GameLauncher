# Game Launcher for Windows

Ein moderner Windows-Launcher für lokale Spielbibliotheken mit Fokus auf ein schnelles WPF-UI, lokale Datenhaltung und integriertes Hardware-Overlay.

## Features

- Erkennt Spiele aus mehreren Quellen, darunter Steam, Epic, GOG und Xbox/Game Pass.
- Unterstützt manuelle Einträge für EXE-Dateien und URI-Starts.
- Bietet ein Overlay mit CPU-, GPU-, RAM- und VRAM-Werten sowie Sitzungsdauer.
- Verwaltert Tags, Filter, Sortierung und unterschiedliche Karten-/Listenansichten.
- Nutzt lokale Cover-/Artwork-Caches und lädt optionale Metadaten nach.
- Bringt einen Windows-Installer und einen Release-Publish-Prozess mit.

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
