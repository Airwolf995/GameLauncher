using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Steam library folders for installed games.
    /// </summary>
    public class SteamScanner : IPlatformScanner
    {
        private const string SteamRegistryPath = @"SOFTWARE\Valve\Steam";

        private static readonly string[] DefaultInstallPaths =
        [
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam"
        ];

        /// <summary>
        /// Bit 4 der StateFlags kennzeichnet eine vollständig installierte App.
        /// </summary>
        private const int StateFullyInstalled = 4;

        /// <summary>
        /// Steam legt für Laufzeitumgebungen und Redistributables ebenfalls
        /// App-Manifeste an. Diese sind keine spielbaren Titel.
        /// </summary>
        private static readonly HashSet<string> NonGameAppIds = new(StringComparer.Ordinal)
        {
            "228980",  // Steamworks Common Redistributables
            "1070560", // Steam Linux Runtime
            "1391110", // Steam Linux Runtime - Soldier
            "1628350", // Steam Linux Runtime - Sniper
            "1493710"  // Proton Experimental
        };

        private readonly List<string> _libraryPaths;

        public string PlatformName => "Steam";

        public SteamScanner(List<string> libraryPaths)
        {
            // Wenn keine Pfade konfiguriert sind, automatisch erkennen
            if (libraryPaths == null || libraryPaths.Count == 0)
            {
                _libraryPaths = GetAutoDetectedPaths();
                if (_libraryPaths.Count > 0)
                    Logger.Log($"Steam auto-detect: found {_libraryPaths.Count} path(s).");
                else
                    Logger.Log("Steam auto-detect: no Steam installation found.");
            }
            else
            {
                _libraryPaths = libraryPaths;
            }
        }

        /// <summary>
        /// Versucht Steam-Bibliothekspfade automatisch zu erkennen:
        /// 1. Windows-Registry über HKLM (64/32 Bit) und HKCU
        /// 2. Bekannte Standard-Installationspfade als Ergänzung
        /// Zu jedem gefundenen Steam-Ordner werden die in libraryfolders.vdf
        /// eingetragenen Zusatzbibliotheken mit aufgenommen.
        /// </summary>
        public static List<string> GetAutoDetectedPaths()
        {
            var found = new List<string>();

            // Steam trägt den Installationsordner je nach Registry-Standort unter
            // unterschiedlichen Wertnamen ein.
            var installPaths = RegistryScanUtility.ReadStrings(SteamRegistryPath, "InstallPath")
                .Concat(RegistryScanUtility.ReadStrings(SteamRegistryPath, "SteamPath"))
                .Concat(DefaultInstallPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string installPath in installPaths)
            {
                try
                {
                    string steamAppsPath = Path.Combine(installPath, "steamapps");
                    ScannerPathUtility.AddExistingDirectory(found, steamAppsPath);
                    AddLibraryFoldersFromVdf(found, Path.Combine(steamAppsPath, "libraryfolders.vdf"));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Steam-Installationspfad {installPath} konnte nicht ausgewertet werden", ex);
                }
            }

            return found;
        }

        /// <summary>
        /// Ergänzt die in libraryfolders.vdf eingetragenen Zusatzbibliotheken.
        /// </summary>
        private static void AddLibraryFoldersFromVdf(ICollection<string> found, string vdfPath)
        {
            if (!File.Exists(vdfPath))
            {
                return;
            }

            try
            {
                foreach (string line in File.ReadAllLines(vdfPath))
                {
                    var match = Regex.Match(line, "\"path\"\\s+\"([^\"]+)\"");
                    if (match.Success)
                    {
                        string extraPath = match.Groups[1].Value.Replace("\\\\", "\\");
                        ScannerPathUtility.AddExistingDirectory(found, Path.Combine(extraPath, "steamapps"));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Steam-Bibliotheksdatei {vdfPath} konnte nicht gelesen werden", ex);
            }
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();
            string? steamRoot = null;

            // Try to find Steam root from library paths
            foreach (var path in _libraryPaths)
            {
                if (!ScannerPathUtility.TryNormalize(path, out var normalizedPath))
                {
                    continue;
                }

                if (normalizedPath.EndsWith("steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Directory.GetParent(normalizedPath);
                    if (parent != null && File.Exists(Path.Combine(parent.FullName, "steam.exe")))
                    {
                        steamRoot = parent.FullName;
                        break;
                    }
                }
            }

            foreach (var libPath in _libraryPaths)
            {
                if (!Directory.Exists(libPath))
                {
                    Logger.Log($"Skipping missing library path: {libPath}");
                    continue;
                }

                Logger.Log($"Scanning Steam library: {libPath}");

                try
                {
                    var acfFiles = Directory.GetFiles(libPath, "appmanifest_*.acf");
                    foreach (var file in acfFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            string content = File.ReadAllText(file);

                            string? name = TryReadManifestValue(content, "name");
                            string? appid = TryReadManifestValue(content, "appid");
                            string? installDir = TryReadManifestValue(content, "installdir");

                            if (!string.IsNullOrWhiteSpace(name) &&
                                !string.IsNullOrWhiteSpace(appid) &&
                                appid.All(char.IsAsciiDigit))
                            {
                                if (NonGameAppIds.Contains(appid))
                                {
                                    Logger.Log($"Steam-Manifest übersprungen, kein spielbarer Titel: {name} ({appid})");
                                    continue;
                                }

                                if (!IsFullyInstalled(content))
                                {
                                    Logger.Log($"Steam-Manifest übersprungen, nicht vollständig installiert: {name} ({appid})");
                                    continue;
                                }

                                string imageUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/header.jpg";

                                // Check local cache
                                if (!string.IsNullOrEmpty(steamRoot))
                                {
                                    string localCache = Path.Combine(steamRoot, "appcache", "librarycache", $"{appid}_header.jpg");
                                    if (File.Exists(localCache))
                                    {
                                        imageUrl = localCache;
                                    }
                                }

                                string fullInstallPath = "";
                                if (!string.IsNullOrEmpty(installDir))
                                {
                                    fullInstallPath = Path.Combine(libPath, "common", installDir);
                                }

                                games.Add(new Game
                                {
                                    Id = $"steam:{appid}",
                                    Name = name,
                                    Platform = "Steam",
                                    Source = "Steam Library",
                                    Path = $"steam://rungameid/{appid}",
                                    LaunchType = "uri",
                                    IsManual = false,
                                    ImageUrl = imageUrl,
                                    InstallDirectory = fullInstallPath
                                });
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error parsing Steam manifest {file}", ex);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error scanning Steam lib {libPath}", ex);
                }
            }

            return games;
        }

        /// <summary>
        /// Prüft anhand der StateFlags, ob der Titel vollständig installiert ist.
        /// Manifeste ohne verwertbaren Status bleiben sichtbar, damit ein
        /// unerwartetes Format keine Spiele aus der Bibliothek entfernt.
        /// </summary>
        internal static bool IsFullyInstalled(string manifestContent)
        {
            string? stateFlags = TryReadManifestValue(manifestContent, "StateFlags");
            if (!int.TryParse(stateFlags, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flags))
            {
                return true;
            }

            return (flags & StateFullyInstalled) != 0;
        }

        internal static string? TryReadManifestValue(string content, string key)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var match = Regex.Match(
                content,
                $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"",
                RegexOptions.CultureInvariant);
            return match.Success ? UnescapeManifestValue(match.Groups[1].Value) : null;
        }

        private static string UnescapeManifestValue(string value)
        {
            var result = new StringBuilder(value.Length);
            bool escaped = false;
            foreach (char character in value)
            {
                if (escaped)
                {
                    result.Append(character);
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else
                {
                    result.Append(character);
                }
            }

            if (escaped)
            {
                result.Append('\\');
            }

            return result.ToString();
        }
    }
}
