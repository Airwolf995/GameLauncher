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

## Lizenzen und Drittanbieter
- Das Projekt steht unter GPLv3; `LICENSE` und `COPYRIGHT.txt` dürfen nicht entfernt oder durch abweichende Texte ersetzt werden.
- Bei neuen oder aktualisierten NuGet-Abhängigkeiten vor dem Release prüfen, ob `THIRD-PARTY-NOTICES.txt` sowie die passenden vollständigen Lizenztexte unter `licenses\` ergänzt werden müssen.
- `build-release.ps1` kopiert Lizenz- und Hinweisdateien in den Publish-Ordner. Releases immer aus diesem Ordner paketieren, damit der Installer sie mitliefert.

## Wichtige Bereiche
- UI: `Views/MainWindow.xaml`, `Views/OverlayWindow.xaml`
- Logik: `Views/MainWindow.xaml.cs`, `ViewModels/`
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
- `Views/`: WPF-Fenster und deren Code-behind-Dateien.
- `ViewModels/`: UI-Zustand und Einstellungslogik.
- `Models/`: Konfiguration, Spielmodelle, Konstanten und zentrale Zustandsobjekte.
- `GameLauncher.Tests/`: Unit- und Integrationstests.

## Konventionen
- Keine wilden Eingriffe in bestehende UX/Design ohne Absprache.
- Änderungen an Status/Overlay immer gegen Binding prüfen.
- Neue Dateien bevorzugt unter `Services/`, `ViewModels/`, `Models/`.
- Antworten, Kommentare, Logging-, UI-Texte und Commit-Messages grundsätzlich mit korrekten deutschen Umlauten schreiben, sofern keine technische Einschränkung dagegen spricht. Lässt ein Werkzeug die Umlaute nicht zuverlässig durch, ist die ASCII-Ersatzschreibung nur für den betroffenen Text zulässig.
- Neue UI-Texte immer in beide Sprachen von `Services/Localization/LocalizedTextCatalog.cs` eintragen; der Katalog muss in Englisch und Deutsch deckungsgleich bleiben.
- Wird ein UI-Text ersetzt oder entfällt sein Aufrufer, den zugehörigen Schlüssel aus dem Katalog entfernen.
- Für Releases bevorzugt den Publish-Workflow nutzen, nicht direkt aus `bin\Release\` paketieren.
- `main` ist der stabile Ziel-Branch; nicht mit `master` arbeiten.

## YAGNI und Einfachheit
- Das YAGNI-Prinzip („You Aren't Gonna Need It“) befolgen: Nur Funktionen, Abstraktionen und Erweiterungspunkte implementieren, für die es einen aktuellen, konkreten Anwendungsfall gibt.
- Keine vorsorglichen Interfaces, Wrapper, Konfigurationsoptionen, generischen Frameworks oder Architektur-Schichten für lediglich mögliche spätere Anforderungen einführen.
- Vor einer neuen Abstraktion muss benennbar sein, welches gegenwärtige Problem sie löst und welche aktuellen Aufrufer oder Implementierungen sie benötigen.
- Wenn Änderungen Aufrufer oder Funktionen ersetzen, anschließend zurückgebliebene unerreichbare Methoden, ungenutzte Felder und redundante Weiterleitungen entfernen.
- Abstraktionen für konkrete Anforderungen wie Testbarkeit, Framework-Grenzen oder tatsächlich mehrere Implementierungen bleiben zulässig; der Grund soll im Code oder in der Änderung nachvollziehbar sein.
- YAGNI ist keine Rechtfertigung für große, unstrukturierte Dateien: Bestehende Verantwortlichkeiten weiterhin sinnvoll auf `Services/`, `ViewModels/` und `Models/` verteilen.
- YAGNI gilt nicht nur für Abstraktionen, sondern auch für Sonderlogik innerhalb einer Funktion: zusätzliche Zustandsfelder, Sonderfallzweige und Grenzwertregeln zählen ebenso als Komplexität wie ein neues Interface.
- Vor einer Korrektur benennen, wie groß der behobene Fehler tatsächlich ist. Steht die Auswirkung in keinem Verhältnis zum Aufwand, ist „nicht ändern“ das bessere Ergebnis und wird als solches begründet.
- Zieht eine Korrektur weitere Sonderregeln nach sich, um selbst nicht zu schaden, ist das ein Hinweis auf ein schlechtes Verhältnis: dann die einfache bestehende Lösung behalten.

## Tests
- Änderungen am Verhalten der Logik mit Tests in `GameLauncher.Tests/` absichern; reine Fenster- und Bedienlogik ist davon ausgenommen.
- Tests gegen die fachliche Aussage schreiben, nicht gegen die Implementierung. Ändert sich ein Verhalten bewusst, den betroffenen Test ersetzen statt seine Erwartung abzuschwächen.
- Vor dem Commit `dotnet test .\GameLauncher.Tests\GameLauncher.Tests.csproj` ausführen; der Testlauf gehört zur Änderung, nicht zum Release.
- Tests dürfen keine Spuren im Benutzerprofil hinterlassen. Wo eine Funktion Dateien anlegt, entweder ein temporäres Verzeichnis verwenden oder die reine Berechnung prüfen.

## Annahmen prüfen
- Undokumentierte Fremdformate wie Registry-Strukturen, Konfigurationsdateien der Spieleplattformen oder Binärformate vor der Umsetzung an einer echten Datei auf dem Zielsystem ansehen, statt sie aus dem Gedächtnis nachzubilden.
- Aussagen zu Laufzeit und Aufwand messen statt schätzen; die Messwerte in der Commit-Message festhalten, damit die Entscheidung später nachvollziehbar bleibt.
- Steht keine echte Datenquelle zur Verfügung, die getroffene Annahme in der Änderung ausdrücklich benennen.

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
- Commit-Messages müssen aussagekräftig sein und das tatsächliche Thema der Änderung klar benennen, nicht nur allgemein „Update“, „Fix“ oder ähnlich.
- In Commit-Messages soll nach Möglichkeit erkennbar sein, was konkret geändert wurde und welcher Bereich betroffen ist, z. B. Overlay, Hardware-Auslese, Installer, Scanner oder UI.
- Wenn ein Commit mehrere technische Anpassungen bündelt, soll die Message den gemeinsamen fachlichen Zusammenhang beschreiben, damit beim späteren Lesen der Historie klar bleibt, warum der Commit existiert.
