# Contributing

Danke für dein Interesse an Beiträgen zu Game Launcher.

## Voraussetzungen

- Windows 10/11
- .NET 8 SDK
- Optional: Inno Setup zum Bauen des Installers

## Lokaler Ablauf

```powershell
dotnet build .\GameLauncher.csproj -c Debug
dotnet test .\GameLauncher.Tests\GameLauncher.Tests.csproj -c Debug --no-restore
```

Für einen sauberen Release-Publish:

```powershell
.\build-release.ps1
```

## Richtlinien

- Bitte kleine, nachvollziehbare Pull Requests bevorzugen.
- Kommentare, Logging und neue Texte möglichst auf Deutsch halten.
- Keine Build-Artefakte wie `bin/`, `obj/`, `publish/` oder `installer_output/` einchecken.
- Bei UI-Änderungen nach Möglichkeit Screenshots beilegen.
- Bei funktionalen Änderungen passende Tests ergänzen, wenn das lokal sinnvoll möglich ist.

## Pull Requests

- Beschreibe kurz Motivation, Änderung und Risiken.
- Notiere, was lokal getestet wurde.
- Wenn Verhalten sichtbar geändert wurde, erwähne das explizit.
