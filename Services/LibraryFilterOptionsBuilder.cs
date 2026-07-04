using System;
using System.Collections.Generic;
using System.Linq;
using GameLauncher.Services.Localization;

namespace GameLauncher.Services
{
    internal static class LibraryFilterOptionsBuilder
    {
        public static List<LocalizedOption> Build(
            LocalizationService localization,
            IReadOnlyCollection<string> usedTags)
        {
            var options = new List<LocalizedOption>
            {
                CreateOption(Constants.Filters.All, localization.Get("Filter.All")),
                CreateOption(Constants.Platforms.Steam, Constants.Platforms.Steam),
                CreateOption(Constants.Platforms.Epic, Constants.Platforms.Epic),
                CreateOption(Constants.Platforms.GOG, Constants.Platforms.GOG),
                CreateOption(Constants.Platforms.UbisoftConnect, Constants.Platforms.UbisoftConnect),
                CreateOption(Constants.Platforms.EAApp, Constants.Platforms.EAApp),
                CreateOption(Constants.Platforms.Xbox, Constants.Platforms.Xbox),
                CreateOption(Constants.Filters.Manual, localization.Get("Filter.Manual")),
                CreateOption(Constants.Filters.Hidden, localization.Get("Filter.Hidden"))
            };

            if (usedTags.Count > 0 || Constants.Tags.DefaultTags.Length > 0)
            {
                options.Add(new LocalizedOption { Key = "__separator__", DisplayName = "──────────", IsSeparator = true });
            }

            foreach (var tag in Constants.Tags.DefaultTags)
            {
                options.Add(CreateTagOption(localization, tag));
            }

            foreach (var tag in usedTags)
            {
                if (!Constants.Tags.DefaultTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    options.Add(CreateTagOption(localization, tag));
                }
            }

            return options;
        }

        private static LocalizedOption CreateTagOption(LocalizationService localization, string tag) =>
            new()
            {
                Key = LibraryFilterService.CreateTagFilterKey(tag),
                DisplayName = string.Format(localization.CurrentCulture, localization.Get("Filter.TagPrefix"), tag)
            };

        private static LocalizedOption CreateOption(string key, string displayName) =>
            new()
            {
                Key = key,
                DisplayName = displayName
            };
    }
}
