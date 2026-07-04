using System;

namespace GameLauncher.Services
{
    internal static class LibraryFilterService
    {
        private const string TagDisplayPrefix = "🏷️ ";

        public static string NormalizeFilterKey(string? filter) => filter switch
        {
            null or "" => Constants.Filters.All,
            "Alle" => Constants.Filters.All,
            "all" => Constants.Filters.All,
            "Favoriten" => Constants.Filters.Favorites,
            "favorites" => Constants.Filters.Favorites,
            "Ausgeblendet" => Constants.Filters.Hidden,
            "Versteckt" => Constants.Filters.Hidden,
            "hidden" => Constants.Filters.Hidden,
            "Manuell" => Constants.Filters.Manual,
            "Manual" => Constants.Filters.Manual,
            _ when filter.StartsWith(TagDisplayPrefix, StringComparison.Ordinal) =>
                $"{Constants.Filters.TagPrefix}{filter.Substring(TagDisplayPrefix.Length)}",
            _ => filter
        };

        public static string CreateTagFilterKey(string tag) => $"{Constants.Filters.TagPrefix}{tag}";
    }
}
