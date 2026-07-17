using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.ViewModels.Settings
{
    public sealed class LibrarySettingsViewModel : ObservableObject
    {
        private string _steamGridDbApiKey = "";
        private string _ignoredProcessesText = "";
        private string _steamPathsText = "";
        private string _epicPathsText = "";
        private string _xboxPathsText = "";
        private string _gogPathsText = "";
        private string _ubisoftPathsText = "";
        private string _eaPathsText = "";

        public string SteamGridDbApiKey { get => _steamGridDbApiKey; set => SetProperty(ref _steamGridDbApiKey, value); }
        public string IgnoredProcessesText { get => _ignoredProcessesText; set => SetProperty(ref _ignoredProcessesText, value); }
        public string SteamPathsText { get => _steamPathsText; set => SetProperty(ref _steamPathsText, value); }
        public string EpicPathsText { get => _epicPathsText; set => SetProperty(ref _epicPathsText, value); }
        public string XboxPathsText { get => _xboxPathsText; set => SetProperty(ref _xboxPathsText, value); }
        public string GogPathsText { get => _gogPathsText; set => SetProperty(ref _gogPathsText, value); }
        public string UbisoftPathsText { get => _ubisoftPathsText; set => SetProperty(ref _ubisoftPathsText, value); }
        public string EaPathsText { get => _eaPathsText; set => SetProperty(ref _eaPathsText, value); }

        public void Load(GameConfig config)
        {
            SteamGridDbApiKey = config.UISettings.SteamGridDbApiKey ?? "";
            IgnoredProcessesText = config.IgnoredProcesses == null
                ? ""
                : string.Join(System.Environment.NewLine, config.IgnoredProcesses);
            SteamPathsText = PathListFormatter.FormatLines(config.SteamLibraryPaths);
            EpicPathsText = PathListFormatter.FormatLines(config.EpicLibraryPaths);
            XboxPathsText = PathListFormatter.FormatLines(config.XboxLibraryPaths);
        }
    }
}
