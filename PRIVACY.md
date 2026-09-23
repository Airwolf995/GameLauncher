# Datenschutzerklärung (Privacy Policy)

Diese Datenschutzerklärung erklärt, welche Daten beim Nutzen des Game Launchers verarbeitet werden.

## 1. Lokale Datenverarbeitung
Der Game Launcher arbeitet grundsätzlich lokal auf deinem Rechner. Er benötigt kein Benutzerkonto, und es findet **keine Übertragung** deiner installierten Spiele, Spielzeiten oder sonstigen persönlichen Daten an den Entwickler statt.

- **Spiele-Scan:** Die Anwendung liest lokal die Installationsdaten von Steam (einschließlich der dort eingetragenen Nicht-Steam-Spiele), Epic Games, GOG Galaxy, Xbox / Game Pass, EA App und Ubisoft Connect, um deine Bibliothek anzuzeigen. Beim Import werden außerdem die Verknüpfungen im Startmenü und auf dem Desktop gelesen.
- **Konfiguration:** Einstellungen, Favoriten, Schlagwörter, ausgeblendete Spiele, eigene Einträge und Spielzeiten werden in deinem Dokumente-Ordner gespeichert (`Dokumente\GameLauncher\game_launcher_config.json`).
- **Bilder und Zwischenspeicher:** Titelbilder, aus Programmdateien gelesene Symbole und Steam-Spieldetails werden ebenfalls unter `Dokumente\GameLauncher` zwischengespeichert.
- **Protokolle:** Zur Fehlersuche schreibt die Anwendung Protokolldateien nach `Dokumente\GameLauncher\Logs`. Sie enthalten unter anderem Namen und Pfade erkannter Spiele sowie Fehlermeldungen. Es werden nur die vier neuesten Protokolle aufbewahrt; sie verlassen deinen Rechner nicht.
- **SteamGridDB-API-Schlüssel:** Trägst du einen eigenen Schlüssel ein, wird er mit der Windows-Datenschutz-API (DPAPI) für dein Benutzerkonto verschlüsselt in der Konfiguration abgelegt.
- **Sicherung:** Die Funktion „Sicherung“ schreibt deine Konfiguration nur in eine Datei, die du selbst auswählst, und liest sie nur von dort wieder ein.
- **Autostart:** Aktivierst du „Mit Windows starten“, trägt die Anwendung sich unter deinem Benutzerkonto in den Windows-Autostart ein (`HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`). Beim Deaktivieren wird der Eintrag wieder entfernt.

## 2. Spielzeiterfassung
Die Erfassung der Spielzeit erfolgt rein lokal. Dazu prüft die Anwendung in regelmäßigen Abständen die Namen und Programmpfade der laufenden Prozesse und gleicht sie mit deiner Bibliothek ab. Gespeichert werden nur die Spielzeit je Spiel und der Zeitpunkt der letzten Sitzung. Prozesse, die du unter „Ignorierte Programme“ einträgst, werden dabei übergangen. Diese Daten verlassen niemals deinen PC.

## 3. Externe Verbindungen & Dienste
Um bestimmte Funktionen bereitzustellen, kommuniziert die Anwendung mit den Servern von Drittanbietern. Bei jeder dieser Verbindungen wird technisch bedingt deine IP-Adresse an den jeweiligen Anbieter übertragen.

### a) GitHub (Update-Prüfung und Update-Download)
Die Anwendung fragt beim Start – sofern „Automatisch nach Updates suchen“ aktiviert ist – oder auf Knopfdruck über die GitHub-API ab, ob eine neue Version verfügbar ist. Entscheidest du dich für ein Update, wird der Installer von GitHub heruntergeladen.
- **Dienstanbieter:** GitHub Inc., USA.
- **Zweck:** Bereitstellung von Software-Updates.

### b) Steam / Valve (Titelbilder, Spieldetails und Cover-Suche)
- **Spieldetails:** Für Steam-Spiele ruft die Anwendung Beschreibung, Entwickler, Publisher, Genres und Erscheinungsdatum über die öffentliche Steam-Shop-Schnittstelle ab. Übertragen werden dabei die Steam-Kennung des Spiels und die eingestellte Sprache.
- **Titelbilder:** Liegt für ein Steam-Spiel kein Titelbild im lokalen Steam-Zwischenspeicher, lädt die Anwendung es aus dem Steam-Bildspeicher (`cdn.cloudflare.steamstatic.com`) nach. Dieser wird über das Content Delivery Network von Cloudflare ausgeliefert.
- **Cover-Suche:** Nutzt du „Cover suchen“, wird der eingegebene Spielname an die Steam-Shop-Suche gesendet – auch ohne SteamGridDB-Schlüssel.
- **Dienstanbieter:** Valve Corporation, USA; für den Bildspeicher zusätzlich Cloudflare, Inc., USA.
- **Zweck:** Anzeige von Titelbildern und Spieldetails in der Bibliothek.

### c) SteamGridDB (Cover-Suche)
Nur wenn du einen eigenen API-Schlüssel hinterlegt hast und „Cover suchen“ nutzt, werden der Spielname und dein API-Schlüssel an SteamGridDB gesendet und die gefundenen Bilder von dort geladen.
- **Dienstanbieter:** SteamGridDB.
- **Zweck:** Abrufen von Cover-Bildern für deine Bibliothek.

### d) LibreHardwareMonitor (lokal, Sensorwerte)
Für die Temperaturen im Overlay fragt die Anwendung den Webserver einer auf deinem Rechner laufenden LibreHardwareMonitor-Installation ab (`http://localhost:8085`). Diese Verbindung verlässt deinen Rechner nicht. Läuft LibreHardwareMonitor nicht, schlägt die Abfrage lediglich fehl.

## 4. Links im Programm
Einige Knöpfe und die Versionshinweise im Updatefenster öffnen Webseiten, etwa die Projektseite auf GitHub oder die Downloadseite von LibreHardwareMonitor. Diese Seiten werden in deinem Standardbrowser geöffnet; dort gelten die Datenschutzbestimmungen der jeweiligen Anbieter.

## 5. Hosting & Quellcode
Dieses Projekt wird über GitHub bereitgestellt. Installationsdateien und Quellcode können über GitHub gehostet oder verteilt werden. Informationen zur Datenerhebung durch GitHub findest du im [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).


---
*Stand: 23. September 2026*
