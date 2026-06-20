using System;
using System.Collections.Generic;
using System.IO;
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

        public async Task<string?> GetCoverUrlAsync(string gameName, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_apiKey)) return null;

            try 
            {
                // 1. Search Game ID
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(gameName)}");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
                
                var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
                {
                    int gameId = data[0].GetProperty("id").GetInt32();

                    // 2. Get Grids
                    using var gridRequest = new HttpRequestMessage(HttpMethod.Get, $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900,342x482");
                    gridRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
                    
                    var gridResponse = await _httpClient.SendAsync(gridRequest, ct);
                    if (gridResponse.IsSuccessStatusCode)
                    {
                         var gridJson = await gridResponse.Content.ReadAsStringAsync(ct);
                         using var gridDoc = JsonDocument.Parse(gridJson);
                         if (gridDoc.RootElement.TryGetProperty("data", out var grids) && grids.GetArrayLength() > 0)
                         {
                             return grids[0].GetProperty("url").GetString();
                         }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error fetching cover for {gameName}", ex);
            }
            return null;
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
