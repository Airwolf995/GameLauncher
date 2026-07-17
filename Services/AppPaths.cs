using System;
using System.IO;
namespace GameLauncher.Services
{
    public static class AppPaths
    {
        public static string GetDocumentsRoot()
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, "GameLauncher");
        }

        public static string GetArtworkRoot() =>
            Path.Combine(GetDocumentsRoot(), "Artwork");

        public static string GetDownloadedCoversDirectory() =>
            Path.Combine(GetArtworkRoot(), "DownloadedCovers");

        public static string GetExtractedIconsDirectory() =>
            Path.Combine(GetArtworkRoot(), "ExtractedIcons");

    }
}
