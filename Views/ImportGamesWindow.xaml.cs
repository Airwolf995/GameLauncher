using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services.Localization;
using GameLauncher.Services.Scanners;
using GameLauncher.ViewModels;

namespace GameLauncher
{
    /// <summary>
    /// Bietet die im Startmenü und auf dem Desktop gefundenen Spiele zur Übernahme
    /// in die Bibliothek an.
    /// </summary>
    public partial class ImportGamesWindow : Window
    {
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private readonly ObservableCollection<ImportCandidateViewModel> _candidates = [];
        private readonly List<Game> _existingGames;
        private readonly CancellationTokenSource _searchCts = new();

        internal ImportGamesWindow(IEnumerable<Game> existingGames)
        {
            InitializeComponent();
            _existingGames = existingGames.ToList();
            CandidateList.ItemsSource = _candidates;
            ConfirmButton.IsEnabled = false;
            StatusText.Text = _localization.Get("Import.Searching");

            Loaded += async (_, _) => await SearchCandidatesAsync();
        }

        /// <summary>
        /// Die vom Benutzer ausgewählten Spiele. Nur nach DialogResult == true gültig.
        /// </summary>
        internal IReadOnlyList<ShortcutGameCandidate> SelectedCandidates { get; private set; } = [];

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

        private async Task SearchCandidatesAsync()
        {
            try
            {
                var token = _searchCts.Token;
                var found = await Task.Run(() => ShortcutImportScanner.FindCandidates(token), token);

                var newCandidates = found
                    .Where(candidate => candidate.IsLikelyGame)
                    .Where(candidate => !ShortcutImportScanner.IsAlreadyKnown(candidate, _existingGames))
                    .ToList();

                foreach (var candidate in newCandidates)
                {
                    _candidates.Add(new ImportCandidateViewModel(candidate));
                }

                int alreadyKnownCount = found.Count - newCandidates.Count;
                StatusText.Text = newCandidates.Count == 0
                    ? _localization.Get("Import.NoneFound")
                    : _localization.Format("Import.FoundSummary", newCandidates.Count, alreadyKnownCount);

                ConfirmButton.IsEnabled = newCandidates.Count > 0;
            }
            catch (OperationCanceledException)
            {
                // Der Dialog wurde während der Suche geschlossen.
            }
            catch (Exception ex)
            {
                Logger.Error("Verknüpfungssuche für den Spielimport fehlgeschlagen", ex);
                StatusText.Text = _localization.Get("Import.SearchFailed");
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelectionForAll(true);

        private void SelectNone_Click(object sender, RoutedEventArgs e) => SetSelectionForAll(false);

        private void SetSelectionForAll(bool isSelected)
        {
            foreach (var candidate in _candidates)
            {
                candidate.IsSelected = isSelected;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            SelectedCandidates = _candidates
                .Where(candidate => candidate.IsSelected)
                .Select(candidate => candidate.Candidate)
                .ToList();

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
