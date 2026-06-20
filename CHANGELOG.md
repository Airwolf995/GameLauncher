# Änderungsprotokoll

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

## [2.0.0] - 2026-05-30
### Neu
- **Lokalisierung (Deutsch/Englisch)**: Alle sichtbaren UI-Texte wurden auf eine zentrale Lokalisierung umgestellt. Englisch ist die Standardsprache, Deutsch kann im Einrichtungsassistenten oder in den Einstellungen gewählt werden; Logging bleibt bewusst Englisch.
  - **Einrichtungsassistent**: Die Sprachauswahl erscheint direkt auf der ersten Seite als zwei klare Buttons für Englisch und Deutsch statt als Dropdown.
  - **Einstellungen**: Eine geänderte Sprache wird erst beim Klick auf „Fertig" übernommen und gespeichert; beim Schließen des Fensters über `X` wird die Auswahl verworfen.
  - **Steam-Metadaten**: Beschreibung und Genres im Detailfenster werden passend zur Launcher-Sprache von Steam geladen und bei einem Sprachwechsel aktualisiert.
- **Scanner-Erweiterung**: Der GameLauncher unterstützt nun drei weitere große Plattformen out-of-the-box. Spiele von **Xbox / PC Game Pass**, **Ubisoft Connect** und der **EA App** werden beim Start automatisch erkannt und der Bibliothek hinzugefügt.
  - **Xbox / PC Game Pass**: Komplett neue, native C#-Implementierung. Xbox-Bibliotheken werden über bekannte bzw. konfigurierte Bibliothekspfade durchsucht und per Windows PackageManager mit App-Start-IDs angereichert, wodurch keine langsamen PowerShell-Prozeduren mehr nötig sind.
  - **EA App & Ubisoft Connect**: Registry-basierte Erkennung der installierten Spiele.
- **Benutzeroberfläche**: Der Erst-Setup-Assistent, der Status-Footer und das Filter-Dropdown wurden erweitert, um die neuen Plattformen vollständig zu unterstützen. Im Setup-Assistenten und in den Einstellungen können automatisch erkannte Steam-, Epic- und Xbox-Bibliothekspfade geprüft, korrigiert und manuell ergänzt werden.

### Geändert
- Framework-Upgrade: Das TargetFramework wurde auf `net8.0-windows10.0.19041.0` aktualisiert, um moderne Windows-spezifische APIs und WinRT/UWP-Schnittstellen (z. B. für den Windows-Paketmanager) nutzen zu können.
- **Einstellungen**: Änderungen im Einstellungsfenster werden nur noch als Live-Vorschau angewendet und erst beim Klick auf „Fertig" dauerhaft in die Konfiguration geschrieben.
- **Einstellungen**: Beim Öffnen der Einstellungen wird keine automatische Bibliothekssuche mehr gestartet; die Pfadfelder zeigen nur gespeicherte bzw. manuell eingetragene Pfade an.
- **Bibliothekszustand**: Die zuletzt gewählte Sortierung und der aktive Filter der Spielebibliothek werden nun in den `UISettings` gespeichert und beim nächsten Start automatisch wiederhergestellt.
- **Startup & Bibliothek**: Das Hauptfenster rendert früher, Cover-Bilder werden beim Laden der Spieleliste asynchron nachgeladen und die Startanimation der Karten bleibt mit reduzierter Verzögerung erhalten.
- **Startup & Kachelbilder**: Die Bibliothek zeigt beim Start zunächst einen Ladezustand, lädt Cover-Bilder vor der ersten Kachel-Animation gezielt für den sichtbaren Bereich plus erweiterten Start-Vorlauf vor und hält sie nur temporär in einem Startup-Cache, bis die sichtbaren Startkarten vollständig eingeblendet sind. Das Fade-In der Cover läuft nur noch beim initialen App-Start; Filter-, Sprach- und Ansichtswechsel lassen vorhandene Bilder stehen. Das erste Scrollen direkt nach dem Start wird dadurch in großen Bibliotheken spürbar ruhiger.
- **Bild-Cache**: Stark gehalten werden gezielt die aktuell sichtbaren Cover plus kleiner Vorlaufbereich, damit lange Listen beim Scrollen oder Plattformwechsel nicht ständig neu decodiert werden. Asynchrone Bild-Rebinds lassen Steam-Kacheln nach dem Startup nicht mehr kurz unsichtbar werden.
- **Artwork-Ordner**: Heruntergeladene Cover und extrahierte Icons werden nun in sprechenden Unterordnern unter `Artwork/` abgelegt. Bereits vorhandene Bilddateien aus dem alten `Cache`-Ordner werden beim Laden der Konfiguration automatisch migriert und referenzierte Pfade in der Config angepasst.
- **Bibliothek aktualisieren**: Der manuelle Refresh-Button zeigt wieder einen echten Ladezustand und baut die Kacheln gezielt neu auf, statt wie ein normaler UI-Wechsel behandelt zu werden.
- **Bibliothek & Kachelansicht**: Der Kartenmodus nutzt nun echte UI-Virtualisierung über ein virtualisierendes Wrap-Panel. Nach manuellem Refresh oder Sprachwechsel werden nur noch sichtbare Kacheln plus kleiner Pufferbereich aufgebaut, was das erste Scrollen in großen Bibliotheken deutlich glättet.
- **Overlay**: Hardware-Monitoring für das Overlay wird erst beim ersten Öffnen des Overlays initialisiert, um den App-Start zu entlasten.
- **Logging**: Scanner-, Einstellungs- und Systemlogs wurden vereinheitlicht und auf englische Log-Meldungen umgestellt. Zusätzlich protokolliert der Bild-Cache nun Preload-, Cache-Hit- und UI-Reload-Statistiken zur Startup-Diagnose.

### Behoben
- **Design-Konsistenz (TextBox)**: Die Fokus-Rahmenfarbe von Texteingabefeldern nutzt nun die dynamische Akzentfarbe des Themes anstelle eines hartcodierten Blaus (`#007ACC`).
- **Design-Konsistenz (CheckBox/Dialoge)**: Toggle-Schalter, `ModernMessageWindow` und `UpdateWindow` verwenden für Rahmen, Hintergründe und Akzente nun konsequent die dynamische Akzentfarbe statt hartcodiertem Blau (`#007ACC`).
- **ConfigService Thread-Safety**: Das `_pendingSave`-Flag ist nun threadsicher (`volatile`) deklariert.
- **Speicherlecks & Aufräumarbeiten**: `ConfigService` und `GameManager` implementieren nun `IDisposable`. Der interne Speichertimer wird beim Beenden sauber freigegeben und etwaige ausstehende Speichervorgänge werden beim Schließen sofort ausgeführt.
- **Verwaiste Bilder**: Beim Wechseln eines Cover-Bildes über „Bild ändern…“ wird das alte Bild nun automatisch aus dem `images/`-Ordner gelöscht, sofern es nicht noch von einem anderen Spiel verwendet wird.
- **Filter-Trennstrich**: Der visuelle Trennstrich (`──────────`) im Filter-Dropdown kann nun auch per Tastaturnavigation nicht mehr versehentlich ausgewählt werden.
- **Bibliotheks-Suche**: Die Suche berücksichtigt nun neben dem Spielnamen auch Tags, sodass Spiele direkt über Schlagwörter gefunden werden können.
- **Filter-Dropdown**: Das Filter-Menü bleibt beim erneuten Öffnen stabil und springt nicht mehr beim Bewegen des Mauszeigers in der Liste.

## [1.6.4] - 2026-05-21
### Neu
- **Spiel hinzufügen (Design-Relaunch)**: Der Dialog zum manuellen Hinzufügen von Spielen nutzt nun das native Windows-Fenster-Design mit automatischem System-Dark-Mode für die Titelleiste (analog zum Einstellungsfenster).
- **Symmetrische & moderne Buttons**: Die Schaltflächen „Speichern“ und „Abbrechen“ sowie alle Hilfsbuttons („Cover suchen“, „...“) sind nun optisch symmetrisch und einheitlich gestaltet, besitzen verfeinerte Hover- und Klick-Zustände sowie ein modernes Flat-Design.
- **Optimierte Hover-Effekte**: Hover-Effekte des primären Speicher-Buttons nutzen eine dynamische Helligkeitsanpassung (Opazität) statt eines hartcodierten Blautons. Dadurch bleibt das gewählte UI-Thema immer erhalten.

### Behoben
- **Bild-Löschung bei Duplikaten**: Beim Löschen eines manuellen Spiels wird nun vorher geprüft, ob die verknüpfte Cover-Bilddatei noch von einem anderen manuellen Spiel oder über ein Bild-Override für ein anderes Spiel verwendet wird, um ein versehentliches Löschen geteilter Bilder zu verhindern. Der Pfadvergleich erfolgt plattformgerecht case-insensitiv.
- **Stabilität der Konfiguration**: `SaveConfigImmediate` schreibt Einstellungen nun zuerst in eine temporäre Datei (`.tmp`) und überschreibt die Originaldatei atomar per `File.Move`, was Beschädigungen der Konfigurationsdatei bei Abstürzen verhindert.
- **Cover-Suche (Spiel hinzufügen)**: Der „Cover suchen"-Button setzte bei Erfolg die Hintergrundfarbe programmatisch (`Brushes.Green`), was WPF-Trigger dauerhaft überschrieb. Der Erfolgs-Zustand wechselt jetzt sauber auf den `PrimaryButton`-Style mit Akzentfarbe.

### Geändert
- **Performance-Schonung bei der Spielzeiterfassung**:
  - Bekannte Systemprozesse (z. B. `svchost`, `explorer`, `dwm`) und Webbrowser werden direkt übersprungen.
  - Mit `TryMatchProcessByName` wird der Prozessname gegen den Index der ausführbaren Spieledateien geprüft, bevor auf das teurere `process.MainModule` zugegriffen wird.
  - Manuelle Spiele wurden aus dem automatischen Prozess-Tracking entfernt, um Fehlmessungen zu vermeiden.
- **API-Stabilität & Cover-Cache**:
  - Dem Metadaten-HttpClient wurde ein Timeout von 10 Sekunden hinzugefügt.
  - Cache-Dateinamen werden deterministisch per FNV-1a Hash aus der Bild-URL erzeugt (`safeName_cover_HASH.png`), statt Ticks zu verwenden. Vor dem Download wird geprüft, ob die Datei bereits im Cache existiert.
- **MVVM-Architektur & Tag-Filterung**:
  - Generierung der Filteroptionen wurde aus dem Code-Behind vollständig in das `MainViewModel` verlagert.
  - Der Trennstrich (`──────────`) im Filter-Dropdown wird über einen WPF DataTrigger automatisch visuell abgeblendet und deaktiviert, um nicht auswählbar zu sein.

## [1.6.3] - 2026-05-07
### Geändert
- **Performance (Speicher)**: Die maximale Auflösung von Spiele-Covern wird nun beim Laden auf 400px (Breite) begrenzt. Dies reduziert den RAM-Verbrauch bei großen Bibliotheken erheblich, ohne die sichtbare Qualität zu beeinträchtigen.
- **Performance (UI)**: Hardware-Sensor-Abfragen für das Overlay erfolgen nun asynchron in einem Hintergrund-Thread. Dies eliminiert Mikroruckler in der Benutzeroberfläche.
- **Performance (Liste)**: Der Listen-Modus nutzt nun echte UI-Virtualisierung (`VirtualizingStackPanel`).
- **Performance (Overlay)**: Der Hardware-Polling-Timer pausiert nun automatisch, wenn das Overlay versteckt ist.
- **Performance (Bilder)**: Remote-Bilder (z.B. Steam-Cover) werden nun nach dem Download eingefroren (`Freeze()`), was die Rendering-Geschwindigkeit erhöht.
- **Design (Layout)**: Das Kachel-Layout wurde exakt auf das Steam-Banner-Verhältnis (110x51px) optimiert. Steam-Spiele werden nun perfekt flächendeckend ohne Ränder angezeigt.
- **Design (Optik)**: Blur-Hintergründe wurden durch einheitliche dunkle Hintergründe ersetzt, um ein ruhigeres Gesamtbild und bessere Performance zu erzielen.
- **Design (Schärfe)**: Implementierung von `Fant`-Scaling und `UseLayoutRounding` für eine pixelgenaue und klare Darstellung der Cover.
- **Bugfix (UI)**: Korrektur des "Cover ?" Texts im AddGame-Dialog zu "Cover ✔" nach erfolgreicher Suche.
- **Bugfix (Layout)**: Entfernung hartcodierter Größenwerte im Layout-Service; nutzt nun konsequent die zentralen Konstanten.
- **CPU-Optimierung**: Signifikante Reduktion von periodischen CPU-Spikes durch effizienteres Ressourcen-Management.
- **Neu (Tags)**: Der Tag "Survival" steht nun im Detail-Fenster standardmäßig zur Auswahl.
- **Bugfix (Start)**: Absturzvermeidung (ArgumentNullException) beim Starten von Spielen mit beschädigten Konfigurationen (leere Pfade/Argumente).
- **Code-Hygiene**: Toten Code (`_lastInputTime`) in der Detailansicht entfernt.

## [1.6.2] - 2026-04-26
### Neu
- **Auto-Detect Steam & Epic (Fix)**: Die automatische Erkennung von Steam- und Epic-Bibliotheken über die Windows-Registry war bisher nicht implementiert — der Launcher hat die Pfade schlicht nicht gesucht. Das ist nun korrekt umgesetzt.
- **Einrichtungsassistent überarbeitet**: Der Pfad-Schritt wurde durch eine übersichtliche Info-Seite ersetzt, die erklärt welche Plattformen automatisch erkannt werden. Kein manuelles Eintragen mehr nötig.

### Geändert
- **Code-Hygiene (SetupWizard)**: Duplizierter P/Invoke-Code (`DwmSetWindowAttribute`) entfernt — nutzt nun den zentralen `DarkModeHelper`.
- **Code-Hygiene (SetupWizard)**: Duplizierte `GetColorCodeForTheme`-Methode entfernt — nutzt nun `Constants.UI.GetColorCodeForTheme()`.
- **Code-Hygiene (SetupWizard)**: ThemeBox liest den HEX-Code jetzt direkt aus dem XAML `Tag`-Attribut statt durch erneuten String-Switch.
- **Code-Hygiene (SetupWizard)**: Doppelter `SaveConfig()`-Aufruf beim Abschließen entfernt — wird jetzt nur noch einmal am Ende gespeichert.
- **Einstellungen (Pfade)**: Steam- und Epic-Pfade zeigen in den Einstellungen nun die tatsächlich erkannten Pfade an — auch wenn sie automatisch ermittelt wurden.

### Behoben
- **SetupWizard Farbe**: „Viel Spaß!"-Text auf der letzten Seite folgt nun der gewählten Akzentfarbe statt immer blau zu sein.
- **SetupWizard Taskleiste**: Das Einrichtungsfenster erscheint nicht mehr in der Taskleiste und kann nicht mehr von dort geschlossen werden (`ShowInTaskbar="False"`).
- **SetupWizard „Überspringen"**: Der sinnlose „Überspringen"-Button wurde entfernt — der Assistent führt nun klar durch alle 4 Schritte.

## [1.6.1] - 2026-04-25
### Entfernt
- **Migrations-Code**: Sämtlicher Code für die Konvertierung von Spielzeiten (Minuten zu Sekunden) und die automatische Erkennung von Updates von Versionen vor v1.5.0 wurde entfernt, da alle aktiven Installationen diesen Prozess bereits durchlaufen haben sollten.
- **Konfigurations-Struktur**: Das nun obsolete Flag `PlayTimeMigratedToSeconds` wurde aus dem Datenmodell entfernt. Bestehende Konfigurationsdateien werden beim nächsten Speichervorgang automatisch bereinigt.
- **Rückwärtskompatibilität**: Bereinigung der `ConfigService`-Logik von Altlasten, was den Code schlanker und wartungsfreundlicher macht.
- **Dead Code**: Unbenutzte private Methode `RefreshView()` aus `MainViewModel` entfernt.
- **Dead Code**: `PlaySessionBatchUpdater` entfernt — duplizierte Logik, die nur in Tests referenziert wurde. Die gleiche Funktionalität ist bereits in `GameStateService.UpdatePlaySessions()` implementiert.

### Geändert
- **Performance (ConfigService)**: `JsonSerializerOptions` werden nun als statisches Feld gecacht statt bei jedem Speichervorgang neu instanziiert zu werden.
- **Wartbarkeit (GameManager)**: `MetadataService` wird einmalig im Konstruktor erstellt statt bei jedem `LoadAllGamesAsync()`-Aufruf.
- **Robustheit (GameManager)**: Der Background-Task für Steam-Metadaten ist nun gegen unbeobachtete Exceptions abgesichert.
- **Wartbarkeit (UpdateService)**: Versionsvergleich nutzt nun `System.Version` statt manueller String-Analyse — kürzer und robuster.
- **Code-Hygiene (UpdateService)**: `IDisposable` implementiert für saubere `HttpClient`-Freigabe bei App-Beendigung.
- **Wartbarkeit (UpdateCoordinator)**: `UpdateService` wird als Klassen-Feld gehalten statt bei jedem Update-Check neu instanziiert. Implementiert nun ebenfalls `IDisposable`.
- **Dokumentation (Game.cs)**: Veralteten Migrations-Kommentar bei `PlayTime` bereinigt.
- **UI (Overlay)**: „SYSTEM HUD"-Überschrift aus dem Overlay entfernt und Fensterhöhe entsprechend angepasst.

## [1.6.0] - 2026-04-25
### Geändert
- **Performance (Logger)**: Implementierung von **Batch-Logging**. Logs werden im RAM gepuffert und erst nach 20 Zeilen oder spätestens alle 30 Sekunden als Block auf die Festplatte geschrieben, um I/O-Last zu minimieren.
- **Performance (Suchzugriffe)**: Die Konfigurationsfelder für Favoriten und versteckte Spiele (`Favorites`, `HiddenGames`) wurden von `List<string>` auf `HashSet<string>` umgestellt, um die Lese-/Schreibzugriffe von O(n) auf O(1) zu beschleunigen. Kompatibel mit alten `config.json`-Dateien.
- **Robustheit (Hintergrund-Tasks)**: Durchgängige Implementierung von `CancellationToken`. Ladevorgänge, Metadaten-Suchen und Downloads werden nun sofort hart abgebrochen und Ressourcen freigegeben, wenn der Launcher währenddessen geschlossen wird.
- **Architektur-Refactoring**: Grundlegende Modernisierung der Codebasis zur Verbesserung der Wartbarkeit und Einhaltung des Single-Responsibility-Prinzips.
- **Game-Model**: Redundante `INotifyPropertyChanged` Implementierungen entfernt; nutzt nun `ObservableObject` als Basisklasse.
- **Typsicherheit**: Magic Strings für UI-Konfigurationen (`CardSize`, `ViewMode`, `SortMode`) durch typsichere Enums ersetzt.
- **GameManager-Fassade**: Der monolithische `GameManager` wurde in spezialisierte Services aufgeteilt (`ConfigService`, `GameStateService`, `GameImageService`), fungiert aber weiterhin als abwärtskompatible Fassade.
- **MainWindow-Code-Behind**: Über 130 Zeilen komplexe Animationslogik (`AnimateItemsStaggered`) in einen dedizierten `AnimationService` ausgelagert.
- **Listenansicht Hover-Overflow**: Der Hover-Vergrößerungseffekt (`ScaleTransform`) in der Listenansicht wurde durch eine sanfte Verschiebung (`TranslateTransform`) nach rechts ersetzt, um zu verhindern, dass die Elemente über den Rand hinausschießen.
- **Technisches Refactoring (Code-Qualität)**:
    - **RunOnUI Extension**: Einführung einer zentralen `Dispatcher`-Erweiterung, um UI-Aktualisierungen aus Hintergrund-Threads sauberer und lesbarer zu gestalten.
    - **Bild-Caching (Details)**: Das `GameDetailsWindow` nutzt nun den globalen `BitmapCacheConverter`, was zu schnelleren Ladezeiten und geringerem RAM-Verbrauch führt.
    - **ActiveGameTracker**: Performance-Optimierung im Tracking-Loop (Vermeidung unnötiger Listen-Allokationen).
    - **Config-Robustheit**: Verbesserte Null-Checks und Absicherungen beim Laden der Konfiguration, um Abstürze durch unvollständige Dateien zu verhindern.
- **UI-Architektur (Settings)**: Entkoppelung der Live-Vorschau von der Festplatten-I/O. Einstellungen werden nun erst beim Klick auf "Fertig" gesammelt in die `config.json` geschrieben.
- **Neu (Einstellungen)**: "Auf Standard zurücksetzen"-Funktion hinzugefügt. Setzt alle UI-Einstellungen sofort auf Werkswerte zurück; sensible Daten wie API-Keys bleiben dabei geschützt.
- **Live-Vorschau**: Änderungen an Kartengröße, Ansichtsmodus oder Schriftgröße werden nun in Echtzeit im Hauptfenster reflektiert, während das Einstellungsmenü geöffnet ist.
- **Globales Flat-Design (Buttons)**: Sämtliche Buttons wurden auf ein modernes "Flat Design" ohne Schatten und Farbverläufe umgestellt, um ein cleanereres Erscheinungsbild zu erzielen und visuelle Artefakte ("Box-Effekt") zu vermeiden.
- **UI-Tweak (Main)**: Der "Spiel hinzufügen"-Button wurde kompakter gestaltet und das Plus-Icon entfernt.
- **Header-Layout (Main)**: Alle Steuerelemente (Suche, Sortierung, Filter, Icons) wurden auf einer exakten horizontalen Linie ausgerichtet. Die Beschriftungen für Sortierung/Filter wurden platzsparend neben die Dropdowns verschoben.
- **System-Icons**: Umstellung auf `Segoe MDL2 Assets` für Refresh- und Einstellungs-Icons für eine konsistente Windows-Optik.
- **UI-Polishing (Settings)**: Buttons für Bilderauswahl, Updates und im Footer wurden verkleinert.
- **Neu (Main)**: Manueller **🔄 Refresh-Button** im Header hinzugefügt, um die Spielebibliothek jederzeit neu scannen zu können.

### Behoben
- **Log-Rotation**: Ein Fehler wurde behoben, durch den trotz Einstellung auf 4 Dateien nur 3 Log-Dateien im Verzeichnis aufbewahrt wurden. Es werden nun korrekt die aktuelle Sitzung plus die 3 vorherigen Historien gespeichert.
- **Hotkey-Registrierung**: Doppelte Registrierung des globalen Hotkeys beim Programmstart entfernt.

## [1.5.0] - 2026-04-23
### Neu
- **Modernes App-Icon**: Das Standard-Icon wurde durch ein neues Logo (`Game-Launcher.png`) ersetzt, das nun systemweit (Taskleiste, Explorer, Fenster) korrekt angezeigt wird.

### Geändert
- **Robuste Icon-Einbindung**: Umstellung auf das WPF Pack-URI Format (`pack://application:,,,/game.ico`) in allen Fenster-Definitionen für eine stabilere Ressourcen-Auflösung beim Programmstart.
- **Projekt-Ressourcen**: `game.ico` wird nun explizit als `Resource` in der Projektdatei geführt, um Ladefehler (BAML-Ausnahmen) zu verhindern.
- **HardwareMonitorService: Sensor-Cache**: Unnötige Hardware-Kategorien (Storage, Controller, PSU) deaktiviert. Sensoren werden beim ersten Aufruf einmalig gesucht und gecacht — danach nur noch direkte Reads statt vollständiger Sensor-Iteration pro Tick.
- **FpsCounter: Bedarfsgesteuert**: Der FPS-Zähler stoppt automatisch bei Fenster-Minimierung und setzt bei Wiederherstellung fort. Eliminiert ~60 unnötige Event-Handler-Aufrufe pro Sekunde im minimierten Zustand.
- **PlayTimeService: Event-basiert**: Die periodische String-basierte Änderungserkennung (`BuildTrackingSignature`) wurde durch ein event-basiertes Dirty-Flag via `GameManager.GamesUpdated` ersetzt. Der Match-Index wird nur noch neu gebaut, wenn sich tatsächlich etwas geändert hat.
- **LibreHardwareMonitorLib**: Update von 0.9.5 auf 0.9.7 für verbesserte Hardware-Unterstützung.
- **Overlay: Spielname gecacht**: Der aktive Spielname wird nur noch bei Spielwechsel aktualisiert statt jede Sekunde neu gesetzt.
- **StatusText: Single-Loop**: Plattform-Statistiken werden in einem einzigen Durchlauf berechnet statt in vier separaten LINQ-Aufrufen.
- **Steam-Metadata: Throttled**: Metadaten-Abruf wird über `SemaphoreSlim` auf maximal 3 gleichzeitige Requests begrenzt statt alle parallel zu feuern.
- **Refactoring: DarkModeHelper**: Auslagerung des 3-fach duplizierten P/Invoke-Codes (`dwmapi.dll`) für die Dark-Mode Titelleisten in einen zentralen, wiederverwendbaren Service.
- **Performance: UI Brush Caching**: Hex-Farbwerte für Plattform-Badges (`GameDetailsWindow`) werden nicht mehr bei jedem Aufruf neu geparst, sondern stammen aus einem performanten, statisch gecachten Dictionary.
- **Logger**: Die Anzahl der aufbewahrten alten Log-Dateien wurde von 2 auf 4 erhöht.

### Entfernt
- **Dead Code**: Die durch das Event-System obsolet gewordene Methode `BuildTrackingSignature` im `PlayTimeService` sowie die zugehörige, verwaiste Unit-Test-Datei wurden restlos entfernt, um den Code zu verschlanken.

### Behoben
- **Startup-Stabilität**: Null-Prüfungen in `MainWindow.OnClosing` hinzugefügt. Dies verhindert Folgeabstürze (NullReferenceException), falls die Initialisierung des Fensters (z.B. durch Ressourcen-Fehler) fehlschlägt.


## [1.4.5] - 2026-03-28
### Neu
- **Overlay-Hotkey konfigurierbar**: In den Einstellungen kann die Tastenkombination für das Overlay jetzt über Modifier (`Strg`, `Alt`, `Shift`, `Win`) und Taste angepasst werden. Ungültige Kombinationen ohne Modifier werden automatisch auf einen gültigen Zustand zurückgeführt.

### Geändert
- **Event-Leaks behoben**: Anonyme Event-Handler in `MainWindow` und `MainViewModel` durch benannte Methoden ersetzt und in `OnClosing` sauber abgemeldet.
- **`PlayTimeService` IDisposable**: Timer wird beim Schließen korrekt gestoppt und disposed.
- **`MainViewModel` IDisposable**: Meldet sich von `GamesUpdated` ab, wird in `OnClosing` disposed.
- **`Game.ImageUrl` gecacht**: Pfadauflösung wird nur einmal berechnet, bei Änderung invalidiert und meldet bei echtem Wechsel nun `PropertyChanged`, damit Cover-Wechsel im UI sofort sichtbar werden.
- **`PlayTimeMatchIndex` optimiert**: Index wird nur noch bei Aenderungen an tracking-relevanten Feldern (`Id`, `IsManual`, `ExecutableName`, `InstallDirectory`) neu gebaut statt bei jedem Tick.
- **`BitmapCacheConverter` gecacht**: Bitmaps werden per `WeakReference`-Cache wiederverwendet statt bei jedem Binding neu geladen. `Invalidate(path)` ermöglicht gezieltes Leeren nach Bildänderungen.
- **`DisplayPlayTime` gecacht**: Formatierung wird nur bei Änderung von `PlayTime` oder `LastPlayed` neu berechnet.
- **Filter-Debounce**: Suche filtert erst 150ms nach dem letzten Tastendruck statt bei jedem Zeichen.

## [1.4.4] - 2026-03-05
### Neu
- **MainWindow-Refactoring (Phase 1)**: Große Teile der Fensterlogik wurden in dedizierte Services ausgelagert (`TrayController`, `FpsCounter`, `OverlayController`, `GameCardLayoutService`, `StatusMessageService`, `UpdateCoordinator`).
- **Playtime-Matching-Index**: Neues indexbasiertes Matching für Prozess-Erkennung (`PlayTimeMatchIndex`) statt vollständigem O(Prozesse × Spiele)-Durchlauf.
- **Active-Game-Tracker**: Deterministische Auswahl des aktiven Spiels bei mehreren laufenden Titeln (`ActiveGameTracker`).
- **PlaySession-Batch-Updates**: Neuer Update-Pfad über `PlaySessionUpdate`, `PlaySessionBatchUpdater` und `GameManager.UpdatePlaySessions(...)` für gebündelte Persistenz.
- **Neue Unit-Tests**: Zusätzliche Tests für FPS-Berechnung, ViewMode-Policy, Status-Handling, Match-Index, Active-Game-Auswahl und PlaySession-Batch-Logik.

### Geändert
- **`MainWindow.xaml.cs` verschlankt**: Das Fenster agiert stärker als Orchestrator, Verhalten/UX und bestehende Bindings bleiben erhalten.
- **Playtime-Scan optimiert**: Prozessvergleich läuft über vorberechnete Executable-/Installpfad-Indizes; manuelle Spiele bleiben wie bisher vom Prozess-Tracking ausgenommen.
- **Overlay-ActiveGame-Logik**: Bei mehreren Treffern wird stabil das zuletzt gestartete laufende Spiel verwendet.
- **Playtime-Persistenz gebündelt**: Pro Tick werden Spielzeit + `LastPlayed` gesammelt und über einen Batch-Aufruf gespeichert statt über getrennte Einzel-Updates pro Spiel.
- **Playtime-Logging reduziert**: Tick-Logs sind kompakter und periodisch zusammengefasst statt pro Spiel bei jedem Tick.

### Behoben
- **Config-Erstanlage & Spielstart-Exit**: Fehlende Konfigurationsdateien werden beim ersten Start nun sofort direkt angelegt, und `Beim Spielstart schließen` beendet den Launcher jetzt zuverlässig auch dann, wenn `Beim Schließen in den Tray minimieren` aktiviert ist.

## [1.4.3] - 2026-02-05
### Neu
- **Overlay RAM & VRAM**: Anzeige von RAM- und VRAM-Auslastung inkl. genutztem/gesamtem Speicher im System-HUD.

### Geändert
- **Statusleiste**: "Starte Spiel..." wird nun zuverlässig zurückgesetzt (Binding bleibt intakt).
- **Overlay Spielzeit**: Bei keinem aktiven Spiel wird die Zeit korrekt auf `00:00:00` zurückgesetzt.
- **Hardware-Monitoring**: Sensor-Matching aufgeräumt und robuster organisiert.

## [1.4.2] - 2026-01-27
### Sicherheit
- **API-Key Verschlüsselung**: Der SteamGridDB API-Key wird nun mit Windows DPAPI verschlüsselt gespeichert. Der Schlüssel ist an den Windows-Benutzer gebunden und kann nicht mehr im Klartext aus der Config-Datei gelesen werden.

### Technisch
- Neue Datei: `Services/SecurityService.cs` (DPAPI Encrypt/Decrypt)
- `UISettings.cs`: Neues Feld `EncryptedSteamGridDbApiKey`, Legacy-Feld entfernt.

### Behoben
- **43 Build-Warnings behoben**: Alle Nullable-Warnungen (CS8618, CS8600, CS8602, CS8603, CS8604) in `SettingsViewModel`, `MainWindow`, `AddGameWindow`, `GameDetailsWindow` und `HotkeyService` korrigiert.


## [1.4.1] - 2026-01-24
### Neu
- **UISettingsService**: Theme-, Font- und Background-Logik in separaten Service ausgelagert (~130 Zeilen).
- **AsyncRelayCommand**: Sicherer async-Befehl mit automatischem Error-Handling und Button-Sperre während der Ausführung.
- **Präzises Spielzeit-Tracking**: Spielzeit wird jetzt in Sekunden erfasst (15-Sekunden-Intervall) statt Minuten.
    - Anzeige: "X Std. Y Min." oder "X Min. Y Sek."
    - Automatische Migration bestehender Spielzeiten beim ersten Start.

### Geändert
- **Code-Optimierung**:
    - `MainWindow.xaml.cs` von ~1074 auf ~980 Zeilen reduziert (~95 Zeilen entfernt).
    - Doppelte `GetColorCodeForTheme`-Methode konsolidiert in `Constants.UI.ThemeColors`.
    - Magic Numbers für Kartengrößen durch `Constants.UI` ersetzt.
- **SettingsViewModel**: `CheckUpdatesCommand` nutzt nun `AsyncRelayCommand` statt problematischem `async void`.

### Technisch
- Neue Datei: `Services/UISettingsService.cs`
- Neue Datei: `Core/AsyncRelayCommand.cs`
- `Constants.cs` erweitert: `ThemeColors`, `TitleFontSize*`, `PlatformFontSize*`
- `GameConfig.cs` erweitert: `PlayTimeMigratedToSeconds`-Flag

### Behoben
- **Overlay Spielerkennung**: Spiele wurden nicht als aktiv erkannt, weil `LoadGamesAsync` die Collection ersetzt hat statt sie zu befüllen. PlayTimeService hatte dadurch eine Referenz auf eine leere Collection.

## [1.4.0] - 2026-01-22
### Neu
- **Kategorien & Tags System 🏷️**:
    - Manage Tags direkt im Spiel-Detail-Fenster.
    - Neues Filter-Dropdown im Hauptfenster zum Filtern nach Tags.
    - Automatische Tag-Vorschläge (Action, RPG, Strategie, etc.) und Erweiterung durch verwendete Tags.
- **Scanner-Architektur**: Die Plattform-Scanner (Steam, Epic, GOG) wurden in separate Services extrahiert für bessere Wartbarkeit.

### Geändert
- **Code-Optimierung**: Massive Aufräumarbeiten im `GameManager` (ca. 250 Zeilen Code entfernt/ausgelagert).
- **UI-Verbesserungen**: Optimiertes Tag-Layout mit automatischem Umbruch und verbessertem ComboBox-Design im Dark Mode.

## [1.3.1] - 2026-01-18
### Neu
- **MVVM Refactoring**: Die Anwendungslogik wurde konsequent in ViewModels (`MainViewModel`, `SettingsViewModel`) ausgelagert.
- **Zentralisierte Styles**: Alle XAML-Styles wurden in separate Dateien (`Colors.xaml`, `Buttons.xaml`, `Controls.xaml`) verschoben.

### Geändert
- **Performance-Boost**: Die Spieleliste wird nun per Bulk-Update geladen, was die UI-Performance erheblich verbessert.
- **UI-Konsistenz**: Spieldetails-Fenster verwendet nun Standard-Titelleiste mit dunklem Design.
- **Gegenseitige Ausschlüsse**: Die Einstellungen "Beim Spielstart minimieren" und "Beim Spielstart schließen" sind nun gegenseitig ausschließend.

### Behoben
- **Tray-Minimierung**: Anwendung verschwindet nicht mehr unsichtbar beim Spielstart.
- **Build-Stabilität**: Code-behind Fehler wurden im Zuge des MVVM-Umbaus korrigiert.

## [1.3.0] - 2026-01-15
### Neu
- **System Overlay (Alt+G)**: Ein neues HUD zeigt FPS, CPU- & GPU-Auslastung sowie die aktuelle Spielzeit direkt über dem Spiel an.
- **Prozess-Ausschlüsse**: In den Einstellungen können nun Programme (z.B. Discord, Steam) definiert werden, die nicht als "aktives Spiel" gezählt werden sollen.
- **Hardware-Sensing 2.0**: Optimierte Erkennung von Temperatur-Sensoren für AMD Ryzen und moderne Intel CPUs.
- **Robustes UI**: Das Overlay zeigt nun "--°C" an, falls Hardware-Sensoren durch System-Sicherheitsfunktionen blockiert werden, statt falsche Werte zu liefern.

### Geändert
- **Update-System**: Der Update-Vorgang wurde vereinfacht und die Verifikation optimiert.
- **Playtime-Tracking**: Die Erfassung der Spielzeit wurde präzisiert und die Erkennung von Launcher-Hintergrundprozessen verbessert.

## [1.2.5] - 2026-01-03
### Neu
- **Interne Optimierungen & Performance**:
    - **Parallele Plattform-Scans**: Steam, GOG und Epic werden nun gleichzeitig gescannt (schnellerer Start).
    - **Sicheres Speichern**: Alle Änderungen werden nun garantiert beim Schließen der Anwendung gespeichert (Safe-Exit).
    - **Effiziente Statistik-Berechnung**: Die Bibliotheks-Statistiken werden nun in einem einzigen Durchlauf berechnet.
    - **Zentrale Konstanten**: Alle wichtigen Werte wurden in einer neuen `Constants.cs` zusammengefasst.
    - **Batch-Speicherung**: Einstellungen werden nun gebündelt gespeichert, um Festplattenzugriffe zu minimieren.
- **XAML-Struktur**: Styles wurden zentralisiert, was die Fenster-Dateien schlanker und übersichtlicher macht.

### Geändert
- **Filter-Logik**: Der redundante Filter "Favoriten" wurde entfernt (Sortierung nach Favoriten reicht aus).

## [1.2.1] - 2026-01-02
### Behoben
- **Setup-Assistent**: Wird nun korrekt übersprungen bei Updates, wenn bereits Spiele/Pfade vorhanden sind (intelligente Migration-Erkennung)
- **Update-Button**: Feste Mindestbreite verhindert Layout-Sprünge beim Wechsel zu "Prüfe..."
- **Einstellungen**: "Bild auswählen" und "Leeren"-Buttons verwenden nun das globale Premium-Design

## [1.2.0] - 2026-01-02
### Neu
- **Thematisierte deaktivierte Buttons**: Buttons behalten nun auch im deaktivierten Zustand (z.B. während der Update-Prüfung) ihre Themenfarbe, erscheinen aber dezent blasser.
- **Einrichtungs-Assistent**: Ein neuer Assistent hilft beim ersten Start, Bibliotheken zu finden und das Design festzulegen.
- **Globales Dynamisches Design**: Die gesamte Anwendung passt sich nun konsistent der gewählten Akzentfarbe an (Hauptfenster, Einstellungen, Assistent, Details).
- **Premium Button Stil**: Ein komplett überarbeitetes Button-Design mit dezenten Verläufen, sanften Schatten und interaktiven 3D-Effekten.
- **Ghost Button Stil**: Ein neuer minimalistischer, transparenter Stil für sekundäre Aktionen wie "Zurück" oder "Favorit", um visuelle Unruhe zu reduzieren.

### Geändert
- **Toggle-Switches**: Checkboxen verwenden nun ein standardmäßiges Blau, um eine konsistente UI-Sprache unabhängig vom gewählten Thema beizubehalten.
- **UI-Feinschliff**: Abstände, Innenabstände und Schriftstärken wurden in der gesamten Anwendung vereinheitlicht.

### Behoben
- **Design-Konsistenz**: Alle verbleibenden festcodierten Farbwerte (z.B. `#007ACC`) wurden entfernt, die die Themenanwendung in Unterfenstern blockierten.
- **Spieldetails**: Das "schwere" Aussehen von sekundären Buttons wurde durch den neuen Ghost-Stil behoben.
