using Microsoft.Win32;
using System.IO;
using System.Windows;

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

        public AddGameWindow(string apiKey = "")
        {
            InitializeComponent();
            _metadataService = new Services.MetadataService(apiKey);
            
            // Disable search button if no API key
            if (string.IsNullOrEmpty(apiKey))
            {
                SearchCoverButton.IsEnabled = false;
                SearchCoverButton.ToolTip = "Bitte gib in den Einstellungen zuerst einen SteamGridDB API Key ein.";
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
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
                MessageBox.Show("Bitte Name und Pfad angeben.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show($"Fehler beim Herunterladen des Covers: {ex.Message}", "Warnung", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show("Bitte gib zuerst einen Spielnamen ein.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SearchCoverButton.IsEnabled = false;
            SearchCoverButton.Content = "Suche...";

            try
            {
                string? imageUrl = await _metadataService.GetCoverUrlAsync(NameBox.Text);
                
                if (imageUrl != null)
                {
                    _pendingCoverUrl = imageUrl;
                    MessageBox.Show("Cover gefunden!", "Erfolg", MessageBoxButton.OK, MessageBoxImage.Information);
                    SearchCoverButton.Content = "Cover ✔";
                    SearchCoverButton.Style = (Style)FindResource("PrimaryButton");
                    return;
                }
                
                MessageBox.Show("Kein Cover gefunden.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                SearchCoverButton.Content = "Cover suchen";
            }
            catch (System.Exception ex)
            {
                 MessageBox.Show($"Fehler bei der Suche: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                 SearchCoverButton.Content = "Cover suchen";
            }
            finally
            {
                SearchCoverButton.IsEnabled = true;
            }
        }
    }
}
