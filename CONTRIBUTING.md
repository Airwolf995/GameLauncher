# Contributing

Danke fuer dein Interesse an Beitragen zu Game Launcher.

## Voraussetzungen

- Windows 10/11
- .NET 8 SDK
- Optional: Inno Setup zum Bauen des Installers

## Lokaler Ablauf

```powershell
dotnet build .\GameLauncher.csproj -c Debug
dotnet test .\GameLauncher.Tests\GameLauncher.Tests.csproj -c Debug --no-restore
```

Fuer einen sauberen Release-Publish:

```powershell
.\build-release.ps1
```

## Richtlinien

- Bitte kleine, nachvollziehbare Pull Requests bevorzugen.
- Kommentare, Logging und neue Texte moeglichst auf Deutsch halten.
- Keine Build-Artefakte wie `bin/`, `obj/`, `publish/` oder `installer_output/` einchecken.
- Bei UI-Aenderungen nach Moeglichkeit Screenshots beilegen.
- Bei funktionalen Aenderungen passende Tests ergaenzen, wenn das lokal sinnvoll moeglich ist.

## Pull Requests

- Beschreibe kurz Motivation, Aenderung und Risiken.
- Notiere, was lokal getestet wurde.
- Wenn Verhalten sichtbar geaendert wurde, erwaehne das explizit.
