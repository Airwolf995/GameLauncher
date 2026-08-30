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

            // Ohne SteamGridDB-Schlüssel bleibt die Suche nutzbar: sie greift dann
            // allein auf die öffentliche Steam-Suche zurück.
            if (string.IsNullOrEmpty(apiKey))
            {
                SearchCoverButton.ToolTip = _localization.Get("AddGame.SteamOnlySearchTooltip");
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

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = TryGetDroppedFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!TryGetDroppedFile(e, out string filePath))
            {
                return;
            }

            e.Handled = true;

            // Verknüpfungen auf ihr Ziel auflösen, damit Pfad und Argumente
            // dem tatsächlich gestarteten Programm entsprechen.
            if (filePath.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase))
            {
                var target = Services.Scanners.ShortcutResolver.TryResolve(filePath);
                if (target == null)
                {
                    MessageBox.Show(
                        _localization.Get("AddGame.DropResolveFailed"),
                        _localization.Get("Common.Warning"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                PathBox.Text = target.TargetPath;
                if (string.IsNullOrWhiteSpace(ArgsBox.Text))
                {
                    ArgsBox.Text = target.Arguments;
                }
            }
            else
            {
                PathBox.Text = filePath;
            }

            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(filePath);
            }
        }

        /// <summary>
        /// Liefert die erste gezogene Datei, sofern es sich um ein Programm oder
        /// eine Verknüpfung handelt.
        /// </summary>
        private static bool TryGetDroppedFile(DragEventArgs e, out string filePath)
        {
            filePath = "";
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            {
                return false;
            }

            foreach (string file in files)
            {
                if (file.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase))
                {
                    filePath = file;
                    return true;
                }
            }

            return false;
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

        private void SearchCover_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show(_localization.Get("AddGame.NameRequiredBody"), _localization.Get("Common.Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Die Auswahl mit Vorschau ersetzt die frühere Übernahme des ersten
            // Suchergebnisses ohne Ansicht.
            var picker = new CoverPickerWindow(_metadataService, NameBox.Text) { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedCover == null)
            {
                return;
            }

            _pendingCoverUrl = picker.SelectedCover.ImageUrl;
            SearchCoverButton.Content = _localization.Get("AddGame.CoverFoundButton");
            SearchCoverButton.Style = (Style)FindResource("PrimaryButton");
        }
    }
}
