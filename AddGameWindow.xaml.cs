using Microsoft.Win32;
using System.IO;
using System.Windows;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class AddGameWindow : Window
    {
        protected override void OnSourceInitialized(System.EventArgs e)
        {
            base.OnSourceInitialized(e);
            Services.DarkModeHelper.EnableDarkTitleBar(this);
        }

        public string GameName => NameBox.Text;
        public string GamePath => PathBox.Text;
        public string GameArgs => ArgsBox.Text;
        public string GameCoverPath { get; private set; } = "";

        private Services.MetadataService _metadataService;
        private string _pendingCoverUrl = "";
        private readonly LocalizationService _localization = LocalizationService.Instance;

        public AddGameWindow(string apiKey = "")
        {
            InitializeComponent();
            _metadataService = new Services.MetadataService(apiKey);
            
            // Disable search button if no API key
            if (string.IsNullOrEmpty(apiKey))
            {
                SearchCoverButton.IsEnabled = false;
                SearchCoverButton.ToolTip = _localization.Get("AddGame.MissingApiKeyTooltip");
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = _localization.Get("AddGame.BrowseFilter")
            };

            if (dialog.ShowDialog() == true)
            {
                PathBox.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                {
                    NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GameName) || string.IsNullOrWhiteSpace(GamePath))
            {
                MessageBox.Show(_localization.Get("AddGame.ValidationBody"), _localization.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Download cover if one was found during search
            if (!string.IsNullOrEmpty(_pendingCoverUrl))
            {
                try
                {
                    string? localPath = await _metadataService.DownloadImageAsync(_pendingCoverUrl, GameName);
                    if (localPath != null)
                    {
                        GameCoverPath = localPath;
                    }
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(_localization.Format("AddGame.CoverDownloadError", ex.Message), _localization.Get("Common.Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async void SearchCover_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show(_localization.Get("AddGame.NameRequiredBody"), _localization.Get("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SearchCoverButton.IsEnabled = false;
            SearchCoverButton.Content = _localization.Get("AddGame.Searching");

            try
            {
                string? imageUrl = await _metadataService.GetCoverUrlAsync(NameBox.Text);
                
                if (imageUrl != null)
                {
                    _pendingCoverUrl = imageUrl;
                    MessageBox.Show(_localization.Get("AddGame.CoverFoundBody"), _localization.Get("Common.Done"), MessageBoxButton.OK, MessageBoxImage.Information);
                    SearchCoverButton.Content = _localization.Get("AddGame.CoverFoundButton");
                    SearchCoverButton.Style = (Style)FindResource("PrimaryButton");
                    return;
                }
                
                MessageBox.Show(_localization.Get("AddGame.CoverNotFoundBody"), _localization.Get("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                SearchCoverButton.Content = _localization.Get("AddGame.SearchCover");
            }
            catch (System.Exception ex)
            {
                 MessageBox.Show(_localization.Format("AddGame.SearchError", ex.Message), _localization.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                 SearchCoverButton.Content = _localization.Get("AddGame.SearchCover");
            }
            finally
            {
                SearchCoverButton.IsEnabled = true;
            }
        }
    }
}
