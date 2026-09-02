using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services
{
    public class MetadataService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private const int MaxSteamGridDbCovers = 12;
        private const int MaxSteamStoreCovers = 6;

        /// <summary>
        /// Zeitlimit einer einzelnen Verfügbarkeitsprüfung. Deutlich kürzer als das
        /// Zeitlimit des HttpClient, weil alle Prüfungen gemeinsam abgewartet werden.
        /// </summary>
        private static readonly TimeSpan CoverAvailabilityTimeout = TimeSpan.FromSeconds(2);

        private readonly string _apiKey;

        public MetadataService(string apiKey = "")
        {
            _apiKey = apiKey;
        }

        public async Task<bool> FetchSteamMetadataAsync(Game game, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(game.Id) || !game.Id.StartsWith("steam:")) return false;

            string appId = game.Id.Replace("steam:", "");
            string url = BuildSteamAppDetailsUrl(appId, LocalizationService.Instance.CurrentLanguage);

            try
            {
                var response = await _httpClient.GetStringAsync(url, ct);
                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    if (doc.RootElement.TryGetProperty(appId, out var appElement))
                    {
                        if (appElement.TryGetProperty("success", out var success) && success.GetBoolean())
                        {
                            var data = appElement.GetProperty("data");

                            // Description
                            if (data.TryGetProperty("short_description", out var desc))
                            {
                                game.Description = desc.GetString() ?? "";
                            }

                            // Release Date
                            if (data.TryGetProperty("release_date", out var releaseDateElem))
                            {
                                if (releaseDateElem.TryGetProperty("date", out var date))
                                {
                                    game.ReleaseDate = date.GetString() ?? "";
                                }
                            }

                            // Developers
                            if (data.TryGetProperty("developers", out var devs))
                            {
                                var devList = new List<string>();
                                foreach (var dev in devs.EnumerateArray())
                                {
                                    var developer = dev.GetString();
                                    if (!string.IsNullOrWhiteSpace(developer))
                                    {
                                        devList.Add(developer);
                                    }
                                }
                                game.Developer = string.Join(", ", devList);
                            }

                            // Publishers
                            if (data.TryGetProperty("publishers", out var pubs))
                            {
                                var pubList = new List<string>();
                                foreach (var pub in pubs.EnumerateArray())
                                {
                                    var publisher = pub.GetString();
                                    if (!string.IsNullOrWhiteSpace(publisher))
                                    {
                                        pubList.Add(publisher);
                                    }
                                }
                                game.Publisher = string.Join(", ", pubList);
                            }

                            // Genres
                            if (data.TryGetProperty("genres", out var genres))
                            {
                                var localizedGenres = new List<string>();
                                foreach (var genre in genres.EnumerateArray())
                                {
                                    if (genre.TryGetProperty("description", out var genreName))
                                    {
                                        var genreDescription = genreName.GetString();
                                        if (!string.IsNullOrWhiteSpace(genreDescription))
                                        {
                                            localizedGenres.Add(genreDescription);
                                        }
                                    }
                                }
                                game.Genres = localizedGenres;
                            }

                            game.RefreshMetadataProperties();

                            Logger.Log($"Fetched metadata for {game.Name}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error fetching metadata for {game.Name}", ex);
            }

            return false;
        }

        internal static string BuildSteamAppDetailsUrl(string appId, AppLanguage language)
        {
            string steamLanguage = language == AppLanguage.German ? "german" : "english";
            return $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&l={steamLanguage}";
        }

        /// <summary>
        /// Sucht Titelbilder zu einem Spielnamen. Ausgewertet werden SteamGridDB,
        /// sofern ein Schlüssel hinterlegt ist, und zusätzlich immer die öffentliche
        /// Steam-Suche. Dadurch liefert die Suche auch ohne SteamGridDB-Schlüssel
        /// brauchbare Ergebnisse.
        /// </summary>
        public async Task<List<CoverCandidate>> GetCoverCandidatesAsync(string gameName, CancellationToken ct = default)
        {
            var candidates = new List<CoverCandidate>();
            if (string.IsNullOrWhiteSpace(gameName))
            {
                return candidates;
            }

            candidates.AddRange(await GetSteamGridDbCoversAsync(gameName, ct));
            candidates.AddRange(await GetSteamStoreCoversAsync(gameName, ct));
            return await FilterAvailableCoversAsync(candidates, IsCoverAvailableAsync, ct);
        }

        /// <summary>
        /// Verwirft Kandidaten, deren Bild gar nicht abrufbar ist. Die Steam-Suche
        /// liefert auch unveröffentlichte Titel, deren Adresse allein aus der AppId
        /// gebildet wird - dort liegt dann kein Bild. Ohne diese Prüfung erschienen
        /// leere Kacheln, die sich auswählen und übernehmen ließen. Die Prüfungen
        /// laufen gemeinsam, die Reihenfolge der Treffer bleibt erhalten.
        /// </summary>
        internal static async Task<List<CoverCandidate>> FilterAvailableCoversAsync(
            IReadOnlyList<CoverCandidate> candidates,
            Func<string, CancellationToken, Task<bool>> isAvailable,
            CancellationToken ct = default)
        {
            if (candidates.Count == 0)
            {
                return [];
            }

            bool[] results = await Task.WhenAll(candidates.Select(c => isAvailable(c.ImageUrl, ct)));

            var available = new List<CoverCandidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (results[i])
                {
                    available.Add(candidates[i]);
                }
                else
                {
                    Logger.Log($"Titelbild verworfen, da nicht abrufbar: {candidates[i].ImageUrl}");
                }
            }

            return available;
        }

        /// <summary>
        /// Fragt nur den Kopf der Antwort ab, das Bild selbst wird dabei nicht geladen.
        /// Verworfen wird ausschließlich bei einer eindeutigen Absage des Servers; bei
        /// einem Netzfehler bleibt der Treffer erhalten, statt ihn zu Unrecht zu
        /// entfernen. Die Prüfung erhält ein eigenes, kurzes Zeitlimit: Da die Auswahl
        /// erst nach der langsamsten Prüfung erscheint, würde ein nicht antwortendes
        /// CDN den Dialog sonst bis zum Zeitlimit des HttpClient aufhalten. Ein
        /// Zeitablauf zählt wie ein Netzfehler, das Bild bleibt also erhalten.
        /// </summary>
        private static async Task<bool> IsCoverAvailableAsync(string url, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CoverAvailabilityTimeout);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                // Nur diese beiden Antworten sagen aus, dass es das Bild nicht gibt.
                // Eine Sperre (403) etwa durch die Drosselung des CDN bedeutet das
                // gerade nicht - der Treffer bliebe sonst grundlos aus der Auswahl.
                return response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Der Dialog wurde geschlossen - das gilt weiterhin für die ganze Suche.
                throw;
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"Prüfung des Titelbilds abgebrochen, da zu langsam: {url}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Verfügbarkeit des Titelbilds konnte nicht geprüft werden: {url}", ex);
                return true;
            }
        }

        private async Task<List<CoverCandidate>> GetSteamGridDbCoversAsync(string gameName, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return [];
            }

            try
            {
                string? searchJson = await GetSteamGridDbJsonAsync(
                    $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(gameName)}", ct);
                if (searchJson == null || !TryReadSteamGridDbGameId(searchJson, out int gameId))
                {
                    return [];
                }

                string? gridJson = await GetSteamGridDbJsonAsync(
                    $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900,342x482", ct);
                return gridJson == null ? [] : ParseSteamGridDbGrids(gridJson);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"SteamGridDB-Suche für {gameName} fehlgeschlagen", ex);
                return [];
            }
        }

        private async Task<string?> GetSteamGridDbJsonAsync(string url, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }

        private async Task<List<CoverCandidate>> GetSteamStoreCoversAsync(string gameName, CancellationToken ct)
        {
            try
            {
                string json = await _httpClient.GetStringAsync(BuildSteamStoreSearchUrl(gameName), ct);
                return ParseSteamStoreCovers(json);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"Steam-Titelbildsuche für {gameName} fehlgeschlagen", ex);
                return [];
            }
        }

        internal static string BuildSteamStoreSearchUrl(string gameName) =>
            $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(gameName)}&l=english&cc=US";

        /// <summary>
        /// Titelbild eines Steam-Titels. Verwendet wird dasselbe Bild, das der
        /// Steam-Scanner für installierte Spiele nutzt.
        /// </summary>
        internal static string BuildSteamHeaderImageUrl(int appId) =>
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

        internal static List<CoverCandidate> ParseSteamGridDbGrids(string json)
        {
            var candidates = new List<CoverCandidate>();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("data", out var grids) ||
                    grids.ValueKind != JsonValueKind.Array)
                {
                    return candidates;
                }

                foreach (var grid in grids.EnumerateArray())
                {
                    if (candidates.Count >= MaxSteamGridDbCovers)
                    {
                        break;
                    }

                    if (grid.TryGetProperty("url", out var url))
                    {
                        string? imageUrl = url.GetString();
                        if (!string.IsNullOrWhiteSpace(imageUrl))
                        {
                            candidates.Add(new CoverCandidate(imageUrl, "SteamGridDB"));
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                Logger.Error("SteamGridDB-Antwort konnte nicht gelesen werden", ex);
            }

            return candidates;
        }

        internal static List<CoverCandidate> ParseSteamStoreCovers(string json)
        {
            var candidates = new List<CoverCandidate>();
            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("items", out var items) ||
                    items.ValueKind != JsonValueKind.Array)
                {
                    return candidates;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (candidates.Count >= MaxSteamStoreCovers)
                    {
                        break;
                    }

                    if (!item.TryGetProperty("id", out var id) || !id.TryGetInt32(out int appId))
                    {
                        continue;
                    }

                    string name = item.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString() ?? string.Empty
                        : string.Empty;

                    candidates.Add(new CoverCandidate(
                        BuildSteamHeaderImageUrl(appId),
                        string.IsNullOrWhiteSpace(name) ? "Steam" : $"Steam: {name}"));
                }
            }
            catch (JsonException ex)
            {
                Logger.Error("Steam-Suchantwort konnte nicht gelesen werden", ex);
            }

            return candidates;
        }

        private static bool TryReadSteamGridDbGameId(string json, out int gameId)
        {
            gameId = 0;
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array &&
                    data.GetArrayLength() > 0 &&
                    data[0].TryGetProperty("id", out var id))
                {
                    return id.TryGetInt32(out gameId);
                }
            }
            catch (JsonException ex)
            {
                Logger.Error("SteamGridDB-Suchantwort konnte nicht gelesen werden", ex);
            }

            return false;
        }

        public async Task<string?> DownloadImageAsync(string url, string gameName, CancellationToken ct = default)
        {
            try
            {
                string cacheDir = AppPaths.GetDownloadedCoversDirectory();
                if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

                string safeName = string.Join("_", gameName.Split(Path.GetInvalidFileNameChars()));
                string urlHash = GetDeterministicHash(url);
                string filePath = Path.Combine(cacheDir, $"{safeName}_cover_{urlHash}.png");

                if (File.Exists(filePath))
                {
                    Logger.Log($"Cover for {gameName} is already cached locally.");
                    return filePath;
                }

                var bytes = await _httpClient.GetByteArrayAsync(url, ct);
                await File.WriteAllBytesAsync(filePath, bytes, ct);
                return filePath;
            }
            catch (Exception ex)
            {
                 Logger.Error($"Error downloading image {url}", ex);
                 return null;
            }
        }

        private static string GetDeterministicHash(string input)
        {
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash.ToString("X8");
        }
    }
}
