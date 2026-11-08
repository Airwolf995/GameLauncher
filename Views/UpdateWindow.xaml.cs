using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using GameLauncher.Services;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateService _updateService;
        private readonly UpdateInfo _updateInfo;
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private readonly CancellationTokenSource _downloadCts = new();
        private bool _isUpdating;

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

            foreach (Inline inline in ChangelogFormatter.CreateInlines(changelog, textBlock.FontSize))
            {
                if (inline is Hyperlink hyperlink)
                {
                    hyperlink.SetResourceReference(TextElement.ForegroundProperty, "AccentColor");
                    hyperlink.RequestNavigate += OpenChangelogLink;
                }

                textBlock.Inlines.Add(inline);
            }
        }

        private static void OpenChangelogLink(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Link aus den Versionshinweisen konnte nicht geöffnet werden", ex);
            }
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating || _downloadCts.IsCancellationRequested) return;
            _isUpdating = true;
            var token = _downloadCts.Token;
            try
            {
                // Hide buttons, show progress
                UpdateButton.Visibility = Visibility.Collapsed;
                CancelButton.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Visible;

                var progress = new Progress<int>(percent =>
                {
                    if (token.IsCancellationRequested) return;
                    ProgressBar.Value = percent;
                    ProgressText.Text = _localization.Format("Update.DownloadingProgress", percent);
                });

                UpdateDownloadResult downloadResult = await _updateService.DownloadUpdateAsync(_updateInfo.DownloadUrl, _updateInfo.Sha256, progress, token);
                token.ThrowIfCancellationRequested();

                if (downloadResult == UpdateDownloadResult.Succeeded)
                {
                    ProgressText.Text = _localization.Get("Update.Installing");
                    await Task.Delay(500, token);
                    token.ThrowIfCancellationRequested();

                    // Ohne weiteres await ist Schließen auf dem UI-Thread ab hier
                    // nicht mehr zwischen Prüfung, Installerstart und Prozessende möglich.
                    if (_updateService.InstallUpdate())
                    {
                        ExitAfterInstallation();
                    }
                    else
                    {
                        ModernMessageWindow.Show(_localization.Get("Update.GenericError"), _localization.Get("Common.Error"));
                        Close();
                    }
                }
                else
                {
                    string messageKey = downloadResult == UpdateDownloadResult.ChecksumMismatch
                        ? "Update.ChecksumError"
                        : "Update.DownloadError";
                    ModernMessageWindow.Show(_localization.Get(messageKey), _localization.Get("Common.Error"));
                    Close();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Update download/install failed", ex);
                if (token.IsCancellationRequested) return;
                ModernMessageWindow.Show(_localization.Get("Update.GenericError"), _localization.Get("Common.Error"));
                Close();
            }
            finally
            {
                _isUpdating = false;
                if (token.IsCancellationRequested) _downloadCts.Dispose();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _downloadCts.Cancel();
            // Während des Downloads besitzt der laufende Ablauf die Quelle.
            if (!_isUpdating) _downloadCts.Dispose();
            base.OnClosed(e);
        }

        private static void ExitAfterInstallation()
        {
            // Environment.Exit überspringt OnClosing und App.OnExit.
            (Application.Current.MainWindow as MainWindow)?.SaveConfigNow();
            Models.Logger.Log("Installer gestartet, Anwendung wird beendet.");
            Models.Logger.Shutdown();
            Environment.Exit(0);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
