using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    /// <summary>
    /// Zeigt die gefundenen Titelbilder zur Auswahl an, statt ungesehen das erste
    /// Suchergebnis zu übernehmen.
    /// </summary>
    public partial class CoverPickerWindow : Window
    {
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private readonly MetadataService _metadataService;
        private readonly string _gameName;
        private readonly CancellationTokenSource _searchCts = new();

        public CoverPickerWindow(MetadataService metadataService, string gameName)
        {
            InitializeComponent();
            _metadataService = metadataService;
            _gameName = gameName;
            StatusText.Text = _localization.Get("CoverPicker.Searching");

            Loaded += async (_, _) => await SearchCoversAsync();
        }

        /// <summary>
        /// Das ausgewählte Titelbild. Nur nach DialogResult == true gültig.
        /// </summary>
        public CoverCandidate? SelectedCover { get; private set; }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Services.DarkModeHelper.EnableDarkTitleBar(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            _searchCts.Cancel();
            _searchCts.Dispose();
            base.OnClosed(e);
        }

        private async Task SearchCoversAsync()
        {
            try
            {
                List<CoverCandidate> candidates =
                    await _metadataService.GetCoverCandidatesAsync(_gameName, _searchCts.Token);

                CoverList.ItemsSource = candidates;
                StatusText.Text = candidates.Count == 0
                    ? _localization.Get("CoverPicker.NoneFound")
                    : _localization.Format("CoverPicker.FoundSummary", candidates.Count);
            }
            catch (OperationCanceledException)
            {
                // Der Dialog wurde während der Suche geschlossen.
            }
            catch (Exception ex)
            {
                Logger.Error($"Titelbildsuche für {_gameName} fehlgeschlagen", ex);
                StatusText.Text = _localization.Get("CoverPicker.SearchFailed");
            }
        }

        private void CoverList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfirmButton.IsEnabled = CoverList.SelectedItem is CoverCandidate;
        }

        private void CoverList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (CoverList.SelectedItem is CoverCandidate)
            {
                Confirm_Click(sender, e);
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (CoverList.SelectedItem is not CoverCandidate selected)
            {
                return;
            }

            SelectedCover = selected;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
