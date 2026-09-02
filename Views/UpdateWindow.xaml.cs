using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GameLauncher.Services;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateService _updateService;
        private readonly UpdateInfo _updateInfo;
        private readonly LocalizationService _localization = LocalizationService.Instance;

        public UpdateWindow(UpdateService updateService, UpdateInfo updateInfo)
        {
            InitializeComponent();
            _updateService = updateService;
            _updateInfo = updateInfo;

            // Set version info
            CurrentVersionText.Text = updateService.GetCurrentVersion();
            NewVersionText.Text = updateInfo.Version;

            // Set changelog
            SetFormattedChangelog(ChangelogText, string.IsNullOrWhiteSpace(updateInfo.Changelog)
                ? _localization.Get("Update.NoChangelog") 
                : updateInfo.Changelog);

            SourceInitialized += (_, _) => DarkModeHelper.EnableDarkTitleBar(this);
        }

        private static void SetFormattedChangelog(TextBlock textBlock, string changelog)
        {
            textBlock.Inlines.Clear();

            string normalizedChangelog = changelog.Replace("\r\n", "\n", StringComparison.Ordinal);
            string[] lines = normalizedChangelog.Split('\n');

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                AddFormattedLine(textBlock, lines[lineIndex]);

                if (lineIndex < lines.Length - 1)
                {
                    textBlock.Inlines.Add(new LineBreak());
                }
            }
        }

        private static void AddFormattedLine(TextBlock textBlock, string line)
        {
            MatchCollection boldSegments = Regex.Matches(line, @"\*\*(.+?)\*\*");
            int currentIndex = 0;

            foreach (Match segment in boldSegments)
            {
                if (segment.Index > currentIndex)
                {
                    textBlock.Inlines.Add(new Run(line[currentIndex..segment.Index]));
                }

                textBlock.Inlines.Add(new Bold(new Run(segment.Groups[1].Value)));
                currentIndex = segment.Index + segment.Length;
            }

            if (currentIndex < line.Length)
            {
                textBlock.Inlines.Add(new Run(line[currentIndex..]));
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Hide buttons, show progress
                UpdateButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Visible;

                var progress = new Progress<int>(percent =>
                {
                    ProgressBar.Value = percent;
                    ProgressText.Text = _localization.Format("Update.DownloadingProgress", percent);
                });

                bool downloadSuccess = await _updateService.DownloadUpdateAsync(_updateInfo.DownloadUrl, progress);

                if (downloadSuccess)
                {
                    ProgressText.Text = _localization.Get("Update.Installing");
                    await Task.Delay(500);
                    _updateService.InstallUpdate();
                    // App will close automatically
                }
                else
                {
                    ModernMessageWindow.Show(_localization.Get("Update.DownloadError"), _localization.Get("Common.Error"));
                    Close();
                }
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Update download/install failed", ex);
                ModernMessageWindow.Show(_localization.Get("Update.GenericError"), _localization.Get("Common.Error"));
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
