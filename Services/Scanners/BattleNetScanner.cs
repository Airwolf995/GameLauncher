using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Findet installierte Battle.net-Spiele über ihre Deinstallationseinträge.
    /// Battle.net trägt jedes Spiel und den Client selbst mit dem gemeinsamen
    /// "Blizzard Uninstaller.exe" ein; dessen Parameter --uid benennt das Produkt,
    /// etwa "zeus" für Call of Duty: Black Ops Cold War.
    /// </summary>
    public partial class BattleNetScanner : IPlatformScanner
    {
        public string PlatformName => Constants.Platforms.BattleNet;

        private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string ClientUid = "battle.net";
        private const string ClientExecutableName = "Battle.net.exe";

        internal sealed record UninstallEntry(string Uid, string DisplayName, string InstallLocation, string DisplayIcon);

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var entries = new List<UninstallEntry>();

            RegistryScanUtility.ForEachSubKey(UninstallRegistryPath, (_, appKey) =>
            {
                ct.ThrowIfCancellationRequested();

                string? uid = TryReadUid(appKey.GetValue("UninstallString") as string);
                if (uid == null)
                {
                    return;
                }

                entries.Add(new UninstallEntry(
                    uid,
                    appKey.GetValue("DisplayName") as string ?? "",
                    appKey.GetValue("InstallLocation") as string ?? "",
                    appKey.GetValue("DisplayIcon") as string ?? ""));
            });

            var games = new List<Game>();

            // Gestartet wird über den Client; ohne ihn hätten die Spiele
            // keinen Startweg.
            var client = entries.FirstOrDefault(entry => entry.Uid == ClientUid);
            string clientPath = client == null ? "" : Path.Combine(client.InstallLocation, ClientExecutableName);
            if (!File.Exists(clientPath))
            {
                if (entries.Count > 0)
                {
                    Logger.Log("Battle.net-Spiele gefunden, aber kein Battle.net-Client; sie werden übersprungen.");
                }

                return games;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.Uid == ClientUid || !Directory.Exists(entry.InstallLocation))
                {
                    continue;
                }

                var game = CreateGame(entry, clientPath);
                if (File.Exists(entry.DisplayIcon))
                {
                    game.ImageUrl = IconExtractor.GetIconFromExe(entry.DisplayIcon, game.Id);
                }

                games.Add(game);
                Logger.Log($"Found Battle.net game: {game.Name} (UID: {entry.Uid})");
            }

            return games;
        }

        /// <summary>
        /// Liest die Produktkennung aus dem Deinstallationsaufruf. Einträge anderer
        /// Hersteller liefern null.
        /// </summary>
        internal static string? TryReadUid(string? uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString) ||
                !uninstallString.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var match = UidPattern().Match(uninstallString);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// "launch_uid" öffnet die Seite des Spiels in Battle.net; gestartet wird
        /// dort mit "Spielen". Einen Direktstart bietet nur "launch", und der
        /// verlangt den exakten Produktcode ("ZEUS" startet, "zeus" nicht). Der
        /// Code steht nirgends verlässlich auf dem Rechner und lässt sich nicht
        /// immer aus der uid ableiten (Diablo IV: "fenris" gegenüber "Fen").
        /// </summary>
        internal static Game CreateGame(UninstallEntry entry, string clientPath)
        {
            string name = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? new DirectoryInfo(entry.InstallLocation).Name
                : entry.DisplayName.Trim();

            return new Game
            {
                Id = $"bnet_{entry.Uid}",
                Name = name,
                Path = clientPath,
                Args = $"--exec=\"launch_uid {entry.Uid}\"",
                Platform = Constants.Platforms.BattleNet,
                LaunchType = "exe",
                InstallDirectory = entry.InstallLocation
            };
        }

        [GeneratedRegex(@"--uid=([^\s""]+)", RegexOptions.IgnoreCase)]
        private static partial Regex UidPattern();
    }
}
