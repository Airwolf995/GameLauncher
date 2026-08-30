using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Liest die in Steam eingetragenen Nicht-Steam-Spiele aus shortcuts.vdf.
    /// Wer ein Spiel bereits in Steam gepflegt hat, findet es damit auch hier
    /// wieder, ohne es ein zweites Mal von Hand einzutragen.
    /// </summary>
    internal static class SteamShortcutsReader
    {
        private const byte TypeMap = 0x00;
        private const byte TypeString = 0x01;
        private const byte TypeInt32 = 0x02;
        private const byte TypeEnd = 0x08;

        private const string RootKey = "shortcuts";

        /// <summary>
        /// Schutzgrenze gegen beschädigte Dateien; die Datei enthält üblicherweise
        /// nur wenige Kilobyte.
        /// </summary>
        private const long MaxFileSize = 8 * 1024 * 1024;

        /// <summary>
        /// Liest die Verknüpfungen aller Steam-Benutzerprofile unterhalb des
        /// angegebenen Steam-Ordners.
        /// </summary>
        public static List<Game> ReadShortcutGames(string steamRootDirectory)
        {
            var games = new List<Game>();
            string userDataDirectory = Path.Combine(steamRootDirectory, "userdata");
            if (!Directory.Exists(userDataDirectory))
            {
                return games;
            }

            foreach (string userDirectory in Directory.EnumerateDirectories(userDataDirectory))
            {
                string shortcutsPath = Path.Combine(userDirectory, "config", "shortcuts.vdf");
                if (!File.Exists(shortcutsPath))
                {
                    continue;
                }

                foreach (var shortcut in ReadFile(shortcutsPath))
                {
                    var game = TryCreateGame(shortcut);
                    if (game != null)
                    {
                        games.Add(game);
                    }
                }
            }

            if (games.Count > 0)
            {
                Logger.Log($"Steam-Verknüpfungen gelesen: {games.Count} Nicht-Steam-Spiel(e).");
            }

            return games;
        }

        private static List<SteamShortcut> ReadFile(string shortcutsPath)
        {
            try
            {
                var fileInfo = new FileInfo(shortcutsPath);
                if (fileInfo.Length > MaxFileSize)
                {
                    Logger.Log($"Steam-Verknüpfungsdatei wird übersprungen, Datei zu groß: {fileInfo.Length} Byte");
                    return [];
                }

                return Parse(File.ReadAllBytes(shortcutsPath));
            }
            catch (Exception ex)
            {
                Logger.Error($"Steam-Verknüpfungsdatei {shortcutsPath} konnte nicht gelesen werden", ex);
                return [];
            }
        }

        private static Game? TryCreateGame(SteamShortcut shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut.Name) || string.IsNullOrWhiteSpace(shortcut.ExecutablePath))
            {
                return null;
            }

            // In Steam ausgeblendete Eintraege bleiben auch hier aussen vor.
            if (shortcut.IsHidden)
            {
                return null;
            }

            if (!File.Exists(shortcut.ExecutablePath))
            {
                Logger.Log($"Steam-Verknüpfung übersprungen, Programm nicht gefunden: {shortcut.ExecutablePath}");
                return null;
            }

            string id = $"steamshortcut_{shortcut.AppId}";

            // Bewusst direkt gestartet statt ueber steam://rungameid: der Pfad steht
            // in der Datei und funktioniert unabhaengig davon, ob Steam laeuft.
            return new Game
            {
                Id = id,
                Name = shortcut.Name.Trim(),
                Platform = Constants.Platforms.Steam,
                Source = "Steam Shortcuts",
                Path = shortcut.ExecutablePath,
                Args = shortcut.LaunchOptions,
                LaunchType = "exe",
                IsManual = false,
                ImageUrl = IconExtractor.GetIconFromExe(shortcut.ExecutablePath, id),
                InstallDirectory = string.IsNullOrWhiteSpace(shortcut.StartDirectory)
                    ? Path.GetDirectoryName(shortcut.ExecutablePath) ?? ""
                    : shortcut.StartDirectory
            };
        }

        /// <summary>
        /// Wertet das binäre VDF-Format aus: 0x00 leitet eine Unterstruktur ein,
        /// 0x01 eine Zeichenkette, 0x02 eine 32-Bit-Zahl, 0x08 schliesst die
        /// aktuelle Struktur ab. Schlüssel und Zeichenketten sind nullterminiert.
        /// </summary>
        internal static List<SteamShortcut> Parse(byte[] data)
        {
            var shortcuts = new List<SteamShortcut>();
            int position = 0;

            if (!TryReadMapHeader(data, ref position, out string? rootKey) ||
                !string.Equals(rootKey, RootKey, StringComparison.OrdinalIgnoreCase))
            {
                return shortcuts;
            }

            while (position < data.Length && data[position] != TypeEnd)
            {
                if (!TryReadMapHeader(data, ref position, out _))
                {
                    break;
                }

                var fields = ReadFields(data, ref position);
                if (fields.Count > 0)
                {
                    shortcuts.Add(CreateShortcut(fields));
                }
            }

            return shortcuts;
        }

        private static SteamShortcut CreateShortcut(Dictionary<string, string> fields)
        {
            return new SteamShortcut(
                ReadField(fields, "appid"),
                ReadField(fields, "AppName"),
                TrimQuotes(ReadField(fields, "Exe")),
                TrimQuotes(ReadField(fields, "StartDir")),
                ReadField(fields, "LaunchOptions"),
                ReadField(fields, "IsHidden") == "1");
        }

        private static string ReadField(Dictionary<string, string> fields, string key) =>
            fields.TryGetValue(key, out string? value) ? value : string.Empty;

        /// <summary>
        /// Liest die Felder eines Eintrags bis zu dessen Abschluss. Verschachtelte
        /// Strukturen wie die Schlagwortliste werden übersprungen.
        /// </summary>
        private static Dictionary<string, string> ReadFields(byte[] data, ref int position)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (position < data.Length)
            {
                byte type = data[position];
                position++;

                if (type == TypeEnd)
                {
                    break;
                }

                if (!TryReadNullTerminatedString(data, ref position, out string key))
                {
                    break;
                }

                switch (type)
                {
                    case TypeString:
                        if (!TryReadNullTerminatedString(data, ref position, out string stringValue))
                        {
                            return fields;
                        }

                        fields[key] = stringValue;
                        break;

                    case TypeInt32:
                        if (position + 4 > data.Length)
                        {
                            return fields;
                        }

                        fields[key] = BitConverter.ToUInt32(data, position).ToString();
                        position += 4;
                        break;

                    case TypeMap:
                        SkipNestedMap(data, ref position);
                        break;

                    default:
                        // Unbekannter Feldtyp: der Rest des Eintrags ist nicht
                        // zuverlaessig deutbar.
                        return fields;
                }
            }

            return fields;
        }

        private static void SkipNestedMap(byte[] data, ref int position)
        {
            int depth = 1;
            while (position < data.Length && depth > 0)
            {
                byte type = data[position];
                position++;

                if (type == TypeEnd)
                {
                    depth--;
                    continue;
                }

                if (!TryReadNullTerminatedString(data, ref position, out _))
                {
                    return;
                }

                switch (type)
                {
                    case TypeString:
                        TryReadNullTerminatedString(data, ref position, out _);
                        break;
                    case TypeInt32:
                        position = Math.Min(position + 4, data.Length);
                        break;
                    case TypeMap:
                        depth++;
                        break;
                    default:
                        return;
                }
            }
        }

        private static bool TryReadMapHeader(byte[] data, ref int position, out string? key)
        {
            key = null;
            if (position >= data.Length || data[position] != TypeMap)
            {
                return false;
            }

            position++;
            if (!TryReadNullTerminatedString(data, ref position, out string mapKey))
            {
                return false;
            }

            key = mapKey;
            return true;
        }

        private static bool TryReadNullTerminatedString(byte[] data, ref int position, out string value)
        {
            value = string.Empty;
            int start = position;

            while (position < data.Length && data[position] != 0)
            {
                position++;
            }

            if (position >= data.Length)
            {
                return false;
            }

            value = Encoding.UTF8.GetString(data, start, position - start);
            position++;
            return true;
        }

        private static string TrimQuotes(string value)
        {
            string trimmed = value.Trim();
            return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
                ? trimmed[1..^1].Trim()
                : trimmed;
        }
    }

    /// <summary>
    /// Ein Eintrag aus shortcuts.vdf.
    /// </summary>
    internal sealed record SteamShortcut(
        string AppId,
        string Name,
        string ExecutablePath,
        string StartDirectory,
        string LaunchOptions,
        bool IsHidden);
}
