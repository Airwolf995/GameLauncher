using System;
using System.Threading.Tasks;
using System.Windows;
using GameLauncher.Services;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class UpdateWindow : Window
    {
        private UpdateService _updateService;
        private UpdateInfo _updateInfo;
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
            ChangelogText.Text = string.IsNullOrWhiteSpace(updateInfo.Changelog) 
                ? _localization.Get("Update.NoChangelog") 
                : updateInfo.Changelog;

            // Dark mode title bar
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Value = percent;
                        ProgressText.Text = _localization.Format("Update.DownloadingProgress", percent);
                    });
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
            }
            catch { }
        }
    }
}
