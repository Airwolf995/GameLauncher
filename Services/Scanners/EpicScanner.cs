using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Epic Games library for installed games.
    /// </summary>
    public class EpicScanner : IPlatformScanner
    {
        private readonly List<string> _libraryPaths;

        public string PlatformName => Constants.Platforms.Epic;

        public EpicScanner(List<string> libraryPaths)
        {
            // Wenn keine Pfade konfiguriert sind, automatisch erkennen
            if (libraryPaths == null || libraryPaths.Count == 0)
            {
                _libraryPaths = GetAutoDetectedPaths();
                if (_libraryPaths.Count > 0)
                    Logger.Log($"Epic auto-detect: found {_libraryPaths.Count} path(s).");
                else
                    Logger.Log("Epic auto-detect: no Epic installation found.");
            }
            else
            {
                _libraryPaths = libraryPaths;
            }
        }

        /// <summary>
        /// Versucht den Epic-Manifest-Pfad automatisch zu erkennen:
        /// 1. Windows-Registry über HKLM (64/32 Bit) und HKCU
        /// 2. Bekannter Standard-ProgramData-Pfad als Ergänzung
        /// </summary>
        public static List<string> GetAutoDetectedPaths()
        {
            var found = new List<string>();

            // 1. Registry
            foreach (string appDataPath in RegistryScanUtility.ReadStrings(
                         @"SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath"))
            {
                try
                {
                    ScannerPathUtility.AddExistingDirectory(found, Path.Combine(appDataPath, "Manifests"));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Epic-Manifestpfad {appDataPath} konnte nicht ausgewertet werden", ex);
                }
            }

            // 2. Standard-ProgramData-Pfad ergänzen
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            ScannerPathUtility.AddExistingDirectory(found, fallback);

            return found;
        }

        /// <summary>
        /// Kategorien, die der Launcher zwar mitverwaltet, die aber keine Spiele
        /// sind: die Unreal Engine selbst und die Plugins dazu.
        /// </summary>
        private static readonly string[] NonGameCategories = ["engines", "plugins"];

        /// <summary>
        /// Epic legt für Engine und Plugins dieselben .item-Manifeste an wie für
        /// Spiele. Ohne diese Prüfung landeten sie als Spiele in der Bibliothek.
        /// Schwerer wiegt, dass Plugins in MainGameAppName auf ihre Engine
        /// zeigen: der Scanner bevorzugt dieses Feld, sodass Engine und Plugins
        /// dieselbe Id bekamen. Da Favoriten, Spielzeit, Tags und ausgeblendete
        /// Einträge über die Id geführt werden, teilten sich mehrere Einträge
        /// deren Zustand.
        ///
        /// Aussortiert wird bewusst nur, was sich nachweislich als Engine oder
        /// Plugin ausweist. Ein Manifest ohne Kategorien bleibt erhalten - fehlt
        /// das Feld in einer künftigen Fassung, verschwindet lieber kein Spiel
        /// aus der Bibliothek.
        /// </summary>
        internal static bool IsGameManifest(JsonElement manifest)
        {
            if (!manifest.TryGetProperty("AppCategories", out var categories) ||
                categories.ValueKind != JsonValueKind.Array)
            {
                return true;
            }

            foreach (var category in categories.EnumerateArray())
            {
                // GetString() wirft, sobald ein Eintrag kein Text ist. Ohne diese
                // Prüfung verließe die Ausnahme die Methode, das Manifest fiele in
                // die Fehlerbehandlung des Scanners und das Spiel verschwände -
                // genau das Gegenteil der obigen Zusicherung.
                if (category.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? value = category.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                foreach (string nonGameCategory in NonGameCategories)
                {
                    if (value.Equals(nonGameCategory, StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith(nonGameCategory + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var games = new List<Game>();

            foreach (var libPath in _libraryPaths)
            {
                if (!Directory.Exists(libPath))
                {
                    Logger.Log($"Skipping missing Epic path: {libPath}");
                    continue;
                }

                Logger.Log($"Scanning Epic library: {libPath}");

                try
                {
                    var itemFiles = Directory.GetFiles(libPath, "*.item");
                    foreach (var file in itemFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            string json = File.ReadAllText(file);
                            using (JsonDocument doc = JsonDocument.Parse(json))
                            {
                                var root = doc.RootElement;

                                if (!IsGameManifest(root))
                                {
                                    continue;
                                }

                                string? displayName = null;
                                string? appName = null;

                                if (root.TryGetProperty("DisplayName", out var nameProp)) displayName = nameProp.GetString();
                                if (root.TryGetProperty("MainGameAppName", out var appProp)) appName = appProp.GetString();
                                if (string.IsNullOrEmpty(appName) && root.TryGetProperty("AppName", out var appNameProp)) appName = appNameProp.GetString();

                                if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(appName))
                                {
                                    string imageUrl = "";
                                    string? installLoc = null;
                                    string? launchExe = null;

                                    if (root.TryGetProperty("InstallLocation", out var locProp)) installLoc = locProp.GetString();
                                    if (root.TryGetProperty("LaunchExecutable", out var exeProp)) launchExe = exeProp.GetString();

                                    if (!string.IsNullOrEmpty(installLoc) && !string.IsNullOrEmpty(launchExe))
                                    {
                                        string fullExePath = Path.Combine(installLoc, launchExe);
                                        imageUrl = IconExtractor.GetIconFromExe(fullExePath, $"epic_{appName}");
                                    }

                                    games.Add(new Game
                                    {
                                        Id = $"epic:{appName}",
                                        Name = displayName,
                                        Platform = Constants.Platforms.Epic,
                                        Source = "Epic Library",
                                        Path = $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true",
                                        LaunchType = "uri",
                                        IsManual = false,
                                        ImageUrl = imageUrl,
                                        InstallDirectory = installLoc ?? ""
                                    });
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Error parsing Epic manifest {file}", ex);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error scanning Epic dir {libPath}", ex);
                }
            }

            return games;
        }
    }
}
