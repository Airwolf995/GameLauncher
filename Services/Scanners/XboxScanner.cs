using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using GameLauncher.Models;
using Windows.Management.Deployment;

namespace GameLauncher.Services.Scanners
{
    /// <summary>
    /// Scans Xbox / Game Pass libraries from configured folders and enriches them with AUMIDs from Windows packages.
    /// </summary>
    public class XboxScanner : IPlatformScanner
    {
        private const string ContentDirectoryName = "Content";
        private const string GameConfigFileName = "MicrosoftGame.Config";
        private const string AppManifestFileName = "appxmanifest.xml";
        private static readonly string[] KnownLibraryFolderNames = ["Xbox", "XboxGames"];

        private readonly List<string> _libraryPaths;

        public string PlatformName => "Xbox";

        public XboxScanner(List<string>? libraryPaths = null)
        {
            _libraryPaths = NormalizePaths(libraryPaths);

            if (_libraryPaths.Count == 0)
            {
                _libraryPaths = GetAutoDetectedPaths();
                Logger.Log(_libraryPaths.Count > 0
                    ? $"Xbox scanner: auto-detected {_libraryPaths.Count} library path(s)."
                    : "Xbox scanner: no Xbox library path auto-detected.");
            }
            else
            {
                Logger.Log($"Xbox scanner: using {_libraryPaths.Count} configured library path(s).");
            }
        }

        public static List<string> GetAutoDetectedPaths()
        {
            var found = new List<string>();

            foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
            {
                foreach (var folderName in KnownLibraryFolderNames)
                {
                    AddIfExists(found, Path.Combine(drive.RootDirectory.FullName, folderName));
                }
            }

            foreach (var package in GetPackageInfos())
            {
                var libraryRoot = TryGetLibraryRootFromInstallPath(package.InstallLocation);
                if (!string.IsNullOrWhiteSpace(libraryRoot))
                {
                    AddIfExists(found, libraryRoot);
                }
            }

            return found;
        }

        public Task<List<Game>> ScanAsync(CancellationToken ct = default)
        {
            return Task.Run(() => Scan(ct), ct);
        }

        private List<Game> Scan(CancellationToken ct)
        {
            var candidates = FindGameCandidates(ct);
            var packageIdentities = candidates
                .Select(candidate => candidate.Metadata.IdentityName)
                .OfType<string>()
                .Where(identity => !string.IsNullOrWhiteSpace(identity))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageInstallRoots = candidates
                .SelectMany(candidate => new[] { candidate.GameDirectory, candidate.ContentDirectory })
                .Select(NormalizePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packages = candidates.Count == 0
                ? new List<XboxPackageInfo>()
                : GetPackageInfos(packageIdentities, packageInstallRoots);
            var games = new List<Game>();
            Logger.Log($"Xbox scan started: {_libraryPaths.Count} library path(s), {packages.Count} package app entries for AUMID enrichment.");

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var game = TryCreateGameFromCandidate(candidate, packages);
                if (game == null)
                {
                    continue;
                }

                AddGameIfMissing(games, game);
                Logger.Log($"Xbox scan: detected game: {game.Name} ({game.LaunchType})");
            }

            Logger.Log($"Xbox scan completed: {games.Count} game(s) found.");
            return games;
        }

        private List<XboxGameCandidate> FindGameCandidates(CancellationToken ct)
        {
            var candidates = new List<XboxGameCandidate>();
            foreach (var libraryPath in _libraryPaths)
            {
                ct.ThrowIfCancellationRequested();

                if (!Directory.Exists(libraryPath))
                {
                    Logger.Log($"Xbox scan: library path not found, skipping: {libraryPath}");
                    continue;
                }

                Logger.Log($"Xbox scan: scanning library path: {libraryPath}");
                foreach (var gameDirectory in EnumerateGameDirectories(libraryPath))
                {
                    ct.ThrowIfCancellationRequested();

                    AddCandidateIfMissing(candidates, TryCreateCandidateFromDirectory(gameDirectory));
                }
            }

            return candidates;
        }

        internal static Game? TryCreateGameFromDirectory(string gameDirectory) =>
            TryCreateGameFromDirectory(gameDirectory, Array.Empty<XboxPackageInfo>());

        internal static Game? TryCreateGameFromDirectory(string gameDirectory, IReadOnlyList<XboxPackageInfo> packages)
        {
            var candidate = TryCreateCandidateFromDirectory(gameDirectory);
            return candidate == null ? null : TryCreateGameFromCandidate(candidate, packages);
        }

        private static XboxGameCandidate? TryCreateCandidateFromDirectory(string gameDirectory)
        {
            string contentDirectory = Path.Combine(gameDirectory, ContentDirectoryName);
            if (!Directory.Exists(contentDirectory))
            {
                return null;
            }

            string gameConfigPath = Path.Combine(contentDirectory, GameConfigFileName);
            string manifestPath = Path.Combine(contentDirectory, AppManifestFileName);
            if (!File.Exists(gameConfigPath) || !File.Exists(manifestPath))
            {
                return null;
            }

            var metadata = ReadDirectoryMetadata(gameDirectory, gameConfigPath, manifestPath);
            if (string.IsNullOrWhiteSpace(metadata.ApplicationId))
            {
                Logger.Log($"Xbox scan: skipped non-launchable package without application id: {metadata.DisplayName}");
                return null;
            }

            string stableSource = metadata.IdentityName ?? metadata.DisplayName;
            string stableId = ComputeStableId(stableSource);
            string? logoPath = ResolveLogoPath(contentDirectory, metadata.LogoPath);

            return new XboxGameCandidate(
                NormalizePath(gameDirectory),
                NormalizePath(contentDirectory),
                metadata,
                stableId,
                logoPath);
        }

        private static Game? TryCreateGameFromCandidate(XboxGameCandidate candidate, IReadOnlyList<XboxPackageInfo> packages)
        {
            var package = FindMatchingPackage(candidate.Metadata, candidate.GameDirectory, candidate.ContentDirectory, packages);
            if (string.IsNullOrWhiteSpace(package?.AppUserModelId))
            {
                Logger.Log($"Xbox scan: skipped package without launch AUMID: {candidate.Metadata.DisplayName}");
                return null;
            }

            return new Game
            {
                Id = $"xbox_{candidate.StableId}",
                Name = candidate.Metadata.DisplayName,
                Path = $"shell:AppsFolder\\{package.AppUserModelId}",
                Args = "",
                Platform = "Xbox",
                LaunchType = "uri",
                ImageUrl = candidate.LogoPath ?? "",
                InstallDirectory = candidate.GameDirectory
            };
        }

        private static List<XboxPackageInfo> GetPackageInfos(
            IReadOnlySet<string>? relevantIdentityNames = null,
            IReadOnlySet<string>? relevantInstallRoots = null)
        {
            var packageInfos = new List<XboxPackageInfo>();

            try
            {
                var packageManager = new PackageManager();
                foreach (var package in packageManager.FindPackagesForUser(string.Empty))
                {
                    try
                    {
                        if (package.IsFramework || package.IsResourcePackage || package.IsStub)
                            continue;

                        if (package.SignatureKind != Windows.ApplicationModel.PackageSignatureKind.Store &&
                            package.SignatureKind != Windows.ApplicationModel.PackageSignatureKind.Enterprise)
                            continue;

                        string installLocation = package.InstalledLocation?.Path ?? "";
                        if (string.IsNullOrWhiteSpace(installLocation))
                            continue;

                        string normalizedInstallLocation = NormalizePath(installLocation);
                        if (relevantIdentityNames != null || relevantInstallRoots != null)
                        {
                            bool identityMatches = relevantIdentityNames?.Contains(package.Id.Name) == true;
                            bool installLocationMatches = relevantInstallRoots?.Contains(normalizedInstallLocation) == true;
                            if (!identityMatches && !installLocationMatches)
                            {
                                continue;
                            }
                        }

                        var appEntries = package.GetAppListEntries()?
                            .Where(entry => !string.IsNullOrWhiteSpace(entry.AppUserModelId))
                            .OrderBy(entry => entry.DisplayInfo.DisplayName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(entry => entry.AppUserModelId, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (appEntries is { Count: > 0 })
                        {
                            foreach (var appEntry in appEntries)
                            {
                                packageInfos.Add(new XboxPackageInfo(
                                    package.Id.Name,
                                    normalizedInstallLocation,
                                    appEntry.AppUserModelId));
                            }
                        }
                        else
                        {
                            packageInfos.Add(new XboxPackageInfo(
                                package.Id.Name,
                                normalizedInstallLocation,
                                null));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Xbox PackageManager: skipped package: {package.Id.FullName} ({ex.GetType().Name})");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Xbox PackageManager detection failed. Xbox packages without a launch AUMID will be skipped.", ex);
            }

            return packageInfos;
        }

        private static XboxPackageInfo? FindMatchingPackage(
            XboxDirectoryMetadata metadata,
            string gameDirectory,
            string contentDirectory,
            IReadOnlyList<XboxPackageInfo> packages)
        {
            if (metadata.IdentityName != null)
            {
                var identityMatches = packages
                    .Where(package => string.Equals(package.IdentityName, metadata.IdentityName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (identityMatches.Count > 0)
                {
                    return SelectBestPackageMatch(identityMatches, metadata.ApplicationId);
                }
            }

            string gameRoot = NormalizePath(gameDirectory);
            string contentRoot = NormalizePath(contentDirectory);
            var pathMatches = packages.Where(package =>
            {
                string installRoot = NormalizePath(package.InstallLocation);
                return string.Equals(installRoot, gameRoot, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(installRoot, contentRoot, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            return pathMatches.Count == 0 ? null : SelectBestPackageMatch(pathMatches, metadata.ApplicationId);
        }

        private static XboxPackageInfo SelectBestPackageMatch(IReadOnlyList<XboxPackageInfo> candidates, string? applicationId)
        {
            if (!string.IsNullOrWhiteSpace(applicationId))
            {
                var appIdMatch = candidates.FirstOrDefault(package =>
                    package.AppUserModelId?.EndsWith($"!{applicationId}", StringComparison.OrdinalIgnoreCase) == true);
                if (appIdMatch != null)
                {
                    return appIdMatch;
                }
            }

            return candidates.FirstOrDefault(package => !string.IsNullOrWhiteSpace(package.AppUserModelId))
                   ?? candidates[0];
        }

        private static XboxDirectoryMetadata ReadDirectoryMetadata(string gameDirectory, string gameConfigPath, string manifestPath)
        {
            string fallbackName = new DirectoryInfo(gameDirectory).Name;
            string displayName = fallbackName;
            string? identityName = null;
            string? applicationId = null;
            string? logoPath = null;

            TryReadGameConfig(gameConfigPath, ref displayName, ref identityName, ref applicationId, ref logoPath);
            TryReadAppManifest(manifestPath, ref displayName, ref identityName, ref applicationId, ref logoPath);

            return new XboxDirectoryMetadata(displayName, identityName, applicationId, logoPath);
        }

        private static void TryReadGameConfig(
            string path,
            ref string displayName,
            ref string? identityName,
            ref string? applicationId,
            ref string? logoPath)
        {
            try
            {
                var document = XDocument.Load(path);
                var identity = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Identity");
                identityName ??= identity?.Attribute("Name")?.Value;

                var executable = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Executable");
                applicationId ??= executable?.Attribute("Id")?.Value;

                var shellVisuals = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "ShellVisuals");
                displayName = shellVisuals?.Attribute("DefaultDisplayName")?.Value ?? displayName;
                logoPath ??= shellVisuals?.Attribute("Square150x150Logo")?.Value
                             ?? shellVisuals?.Attribute("Square44x44Logo")?.Value
                             ?? shellVisuals?.Attribute("StoreLogo")?.Value;
            }
            catch (Exception ex)
            {
                Logger.Log($"Xbox MicrosoftGame.Config could not be read: {path} ({ex.GetType().Name})");
            }
        }

        private static void TryReadAppManifest(
            string path,
            ref string displayName,
            ref string? identityName,
            ref string? applicationId,
            ref string? logoPath)
        {
            try
            {
                var document = XDocument.Load(path);
                var identity = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Identity");
                identityName ??= identity?.Attribute("Name")?.Value;

                var application = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Application");
                applicationId ??= application?.Attribute("Id")?.Value;

                var properties = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Properties");
                var manifestDisplayName = properties?.Elements().FirstOrDefault(element => element.Name.LocalName == "DisplayName")?.Value;
                if (!string.IsNullOrWhiteSpace(manifestDisplayName))
                {
                    displayName = manifestDisplayName;
                }

                var visualElements = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "VisualElements");
                logoPath ??= visualElements?.Attribute("Square150x150Logo")?.Value
                             ?? visualElements?.Attribute("Square44x44Logo")?.Value
                             ?? properties?.Elements().FirstOrDefault(element => element.Name.LocalName == "Logo")?.Value;
            }
            catch (Exception ex)
            {
                Logger.Log($"Xbox appxmanifest.xml could not be read: {path} ({ex.GetType().Name})");
            }
        }

        private static string? ResolveLogoPath(string contentDirectory, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            string fullPath = Path.Combine(contentDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(fullPath) ? fullPath : null;
        }

        private static void AddGameIfMissing(List<Game> games, Game game)
        {
            if (games.Any(existing =>
                    string.Equals(existing.Id, game.Id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.InstallDirectory, game.InstallDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            games.Add(game);
        }

        private static void AddCandidateIfMissing(List<XboxGameCandidate> candidates, XboxGameCandidate? candidate)
        {
            if (candidate == null)
            {
                return;
            }

            if (candidates.Any(existing =>
                    string.Equals(existing.StableId, candidate.StableId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.GameDirectory, candidate.GameDirectory, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(candidate);
        }

        private static string? TryGetLibraryRootFromInstallPath(string? installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
            {
                return null;
            }

            var directory = new DirectoryInfo(installPath);
            if (directory.Name.Equals(ContentDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Parent != null &&
                File.Exists(Path.Combine(directory.FullName, GameConfigFileName)))
            {
                return directory.Parent.Parent.FullName;
            }

            foreach (var parent in EnumerateParents(directory))
            {
                if (KnownLibraryFolderNames.Any(folderName => parent.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase)))
                {
                    return parent.FullName;
                }
            }

            return null;
        }

        private static IEnumerable<DirectoryInfo> EnumerateParents(DirectoryInfo directory)
        {
            for (DirectoryInfo? current = directory; current != null; current = current.Parent)
            {
                yield return current;
            }
        }

        private static IEnumerable<string> EnumerateGameDirectories(string libraryPath)
        {
            try
            {
                return Directory.GetDirectories(libraryPath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Xbox library path could not be read: {libraryPath}", ex);
                return Array.Empty<string>();
            }
        }

        private static void AddIfExists(List<string> list, string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            string normalizedPath = NormalizePath(path);
            if (!list.Any(existing => string.Equals(existing, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(normalizedPath);
            }
        }

        private static List<string> NormalizePaths(IEnumerable<string>? paths) =>
            paths?
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? new List<string>();

        private static string NormalizePath(string path) =>
            Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        internal static string ComputeStableId(string source)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
            return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
        }

        internal sealed record XboxPackageInfo(
            string IdentityName,
            string InstallLocation,
            string? AppUserModelId);

        private sealed record XboxDirectoryMetadata(
            string DisplayName,
            string? IdentityName,
            string? ApplicationId,
            string? LogoPath);

        private sealed record XboxGameCandidate(
            string GameDirectory,
            string ContentDirectory,
            XboxDirectoryMetadata Metadata,
            string StableId,
            string? LogoPath);
    }
}
