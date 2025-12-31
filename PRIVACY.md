# Datenschutzerklärung (Privacy Policy)

Der **Game Launcher** ist ein Freeware-Projekt. Diese Datenschutzerklärung erklärt, welche Daten beim Nutzen der Anwendung verarbeitet werden.

## 1. Lokale Datenverarbeitung
Der Game Launcher ist so konzipiert, dass er primär lokal auf deinem Rechner arbeitet. 
- **Spiele-Scan:** Die Anwendung scannt lokal installierte Spieleplattformen (Steam, Epic Games, GOG Galaxy), um deine Bibliothek anzuzeigen.
- **Konfiguration:** Deine Einstellungen, Favoriten und Spielzeiten werden lokal in deinem Dokumente-Ordner gespeichert (`Dokumente\GameLauncher\game_launcher_config.json`).
- **KEIN Senden von Daten:** Es findet keine Übertragung deiner installierten Spiele, Spielzeiten oder sonstigen persönlichen Profile an den Entwickler statt.

## 2. Externe Verbindungen & Dienste
Um bestimmte Funktionen bereitzustellen, kommuniziert die Anwendung mit den Servern von Drittanbietern:

### a) GitHub (Update-Prüfung)
Die Anwendung prüft beim Start (oder manuell) über die GitHub-API, ob eine neue Version verfügbar ist. Hierbei wird technisch bedingt deine IP-Adresse an GitHub übertragen.
- **Dienstanbieter:** GitHub Inc., USA.
- **Zweck:** Bereitstellung von Software-Updates.

### b) SteamGridDB (Cover-Suche)
Wenn du die Funktion "Cover suchen" nutzt, wird der Name des Spiels und dein persönlicher API-Key an SteamGridDB gesendet.
- **Dienstanbieter:** SteamGridDB.
- **Zweck:** Abrufen von Cover-Bildern für deine Bibliothek.

## 3. Playtime Tracking
Die Erfassung der Spielzeit erfolgt rein lokal durch das Überwachen der Prozess-IDs der gestarteten Spiele. Diese Daten verlassen niemals deinen PC.

## 4. Hosting & Geschlossener Quellcode
Dieses Repository dient der Bereitstellung von Dokumentation und Installationsdateien. Der Quellcode der Anwendung ist nicht öffentlich zugänglich. Die Installationsdateien werden auf GitHub gehostet. Informationen zur Datenerhebung durch GitHub findest du in der [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement).


---
*Stand: 31. Dezember 2025*
