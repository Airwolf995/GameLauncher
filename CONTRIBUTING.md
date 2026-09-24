# Contributing

Danke für dein Interesse an Beiträgen zu Game Launcher.

## Voraussetzungen

- Windows 10/11
- .NET 10 SDK – die Version legt [`global.json`](./global.json) fest (10.0.100 oder ein neueres Feature-Release)
- Optional: [Inno Setup](https://jrsoftware.org/isinfo.php) zum Bauen des Installers

## Lokaler Ablauf

```powershell
dotnet build .\GameLauncher.csproj -c Debug
dotnet test .\GameLauncher.Tests\GameLauncher.Tests.csproj
```

Für einen sauberen Release-Publish:

```powershell
.\build-release.ps1
```

Das Skript erzeugt `publish\win-x64`; daraus baut Inno Setup mit [`installer.iss`](./installer.iss) den Installer.

## Arbeitsablauf

- Jede Änderung beginnt auf einem eigenen Branch, z. B. `feature/xbox-scan` oder `fix/overlay-temperatur`. Ein Branch behandelt ein Thema.
- Änderungen gehen per Pull Request nach `main`, nicht direkt auf `main`.
- Jeder Pull Request wird automatisch gebaut und getestet (siehe [`.github/workflows/ci.yml`](./.github/workflows/ci.yml)). Gemergt wird erst, wenn die Prüfung grün ist.
- Bitte thematisch getrennte Commits mit aussagekräftigen Nachrichten, die den betroffenen Bereich erkennen lassen – etwa Overlay, Scanner, Installer oder Oberfläche – statt „Update“ oder „Fix“.

## Richtlinien

### Sprache und Texte

- Kommentare, Protokollmeldungen, Oberflächentexte und Commit-Nachrichten auf Deutsch, mit korrekten Umlauten.
- Neue Oberflächentexte immer in **beide** Sprachen von [`Services/Localization/LocalizedTextCatalog.cs`](./Services/Localization/LocalizedTextCatalog.cs) eintragen; Englisch und Deutsch müssen dieselben Schlüssel enthalten. Entfällt ein Text, seinen Schlüssel ebenfalls entfernen.

### Einfach bleiben

- Nur umsetzen, wofür es einen aktuellen, konkreten Anwendungsfall gibt. Keine vorsorglichen Schnittstellen, Einstellungen oder Erweiterungspunkte für mögliche spätere Anforderungen.
- Ersetzt eine Änderung Code, den zurückgebliebenen, nicht mehr genutzten Code mit entfernen.
- Bestehendes Aussehen und Bedienverhalten nicht ohne Absprache umgestalten.

### Tests

- Änderungen an der Logik mit Tests in `GameLauncher.Tests` absichern; reine Fenster- und Bedienlogik ist davon ausgenommen.
- Tests prüfen das fachliche Verhalten, nicht die Umsetzung. Ändert sich ein Verhalten bewusst, den betroffenen Test ersetzen statt seine Erwartung abzuschwächen.
- Tests dürfen keine Spuren im Benutzerprofil hinterlassen. Wo Dateien entstehen, ein temporäres Verzeichnis verwenden.
- Vor dem Pull Request `dotnet test` lokal ausführen.

### Annahmen prüfen

- Formate der Spieleplattformen – Registry-Einträge, Konfigurations- und Manifestdateien – sind meist nicht dokumentiert. Bitte an einer echten Installation nachsehen, statt sie aus dem Gedächtnis nachzubilden, und im Pull Request nennen, woran geprüft wurde.
- Aussagen zu Laufzeit oder Speicherbedarf messen statt schätzen und die Messwerte angeben.

### Abhängigkeiten und Lizenz

- Das Projekt steht unter der GPLv3. `LICENSE` und `COPYRIGHT.txt` bitte nicht entfernen oder durch abweichende Texte ersetzen.
- Bei neuen oder aktualisierten NuGet-Paketen [`THIRD-PARTY-NOTICES.txt`](./THIRD-PARTY-NOTICES.txt) und die vollständigen Lizenztexte unter `licenses\` ergänzen.

### Nicht einchecken

- Build-Artefakte wie `bin/`, `obj/`, `publish/` oder `installer_output/`.
- Persönliche Dateien wie eine eigene `game_launcher_config.json` oder Protokolle.

## Pull Requests

- Beschreibe kurz Motivation, Änderung und Risiken.
- Notiere, was lokal getestet wurde.
- Wenn Verhalten sichtbar geändert wurde, erwähne das explizit und lege bei Änderungen an der Oberfläche nach Möglichkeit Screenshots bei.

## Sicherheitslücken

Sicherheitslücken bitte **nicht** als öffentliches Issue melden, sondern wie in der [Sicherheitsrichtlinie](./SECURITY.md) beschrieben.

Weitere Einzelheiten zu Aufbau und Konventionen stehen in [AGENTS.md](./AGENTS.md).
