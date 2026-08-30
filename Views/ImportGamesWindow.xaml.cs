using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GameLauncher.Models;
using GameLauncher.Services.Localization;
using GameLauncher.Services.Scanners;
using GameLauncher.ViewModels;

namespace GameLauncher
{
    /// <summary>
    /// Bietet die im Startmenü und auf dem Desktop gefundenen Programme zur
    /// Übernahme in die Bibliothek an. Standardmäßig sind das die als Spiel
    /// eingestuften Einträge; die übrigen bleiben über den Umschalter erreichbar,
    /// damit eine Fehleinschätzung kein Spiel unauffindbar macht.
    /// </summary>
    public partial class ImportGamesWindow : Window
    {
        private readonly LocalizationService _localization = LocalizationService.Instance;
        private readonly ObservableCollection<ImportCandidateViewModel> _candidates = [];
        private readonly List<Game> _existingGames;
        private readonly CancellationTokenSource _searchCts = new();

        private ICollectionView? _candidateView;
        private int _otherProgramCount;

        internal ImportGamesWindow(IEnumerable<Game> existingGames)
        {
            InitializeComponent();
            _existingGames = existingGames.ToList();

            _candidateView = CollectionViewSource.GetDefaultView(_candidates);
            _candidateView.Filter = IsVisibleCandidate;
            CandidateList.ItemsSource = _candidateView;

            ConfirmButton.IsEnabled = false;
            StatusText.Text = _localization.Get("Import.Searching");

            Loaded += async (_, _) => await SearchCandidatesAsync();
        }

        /// <summary>
        /// Die vom Benutzer ausgewählten Programme. Nur nach DialogResult == true gültig.
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

            foreach (var candidate in _candidates)
            {
                candidate.PropertyChanged -= OnCandidatePropertyChanged;
            }

            base.OnClosed(e);
        }

        private async Task SearchCandidatesAsync()
        {
            try
            {
                var token = _searchCts.Token;
                var found = await Task.Run(() => ShortcutImportScanner.FindCandidates(token), token);

                var newCandidates = found
                    .Where(candidate => !ShortcutImportScanner.IsAlreadyKnown(candidate, _existingGames))
                    .ToList();

                foreach (var candidate in newCandidates)
                {
                    var viewModel = new ImportCandidateViewModel(candidate);
                    viewModel.PropertyChanged += OnCandidatePropertyChanged;
                    _candidates.Add(viewModel);
                }

                int likelyGameCount = newCandidates.Count(candidate => candidate.IsLikelyGame);
                _otherProgramCount = newCandidates.Count - likelyGameCount;
                int alreadyKnownCount = found.Count - newCandidates.Count;

                StatusText.Text = likelyGameCount == 0 && _otherProgramCount == 0
                    ? _localization.Get("Import.NoneFound")
                    : _localization.Format(
                        "Import.FoundSummary",
                        likelyGameCount,
                        alreadyKnownCount,
                        _otherProgramCount);

                UpdateSelectionState();
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

        /// <summary>
        /// Entscheidet, ob ein Eintrag in der Liste erscheint: abhängig von der
        /// Umschaltung und dem Suchbegriff, der Name und Pfad berücksichtigt.
        /// </summary>
        private bool IsVisibleCandidate(object item)
        {
            if (item is not ImportCandidateViewModel candidate)
            {
                return false;
            }

            if (ShowAllProgramsBox.IsChecked != true && !candidate.IsLikelyGame)
            {
                return false;
            }

            string search = SearchBox.Text.Trim();
            if (search.Length == 0)
            {
                return true;
            }

            return candidate.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                   candidate.TargetPath.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
            _candidateView?.Refresh();

        private void ShowAllPrograms_Changed(object sender, RoutedEventArgs e) =>
            _candidateView?.Refresh();

        private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImportCandidateViewModel.IsSelected))
            {
                UpdateSelectionState();
            }
        }

        /// <summary>
        /// Hält den Bestätigungsknopf auf dem Stand der Auswahl. Die Anzahl ist
        /// wichtig, weil ausgewählte Einträge durch Suche oder Umschaltung
        /// vorübergehend ausgeblendet sein können.
        /// </summary>
        private void UpdateSelectionState()
        {
            int selectedCount = _candidates.Count(candidate => candidate.IsSelected);
            ConfirmButton.IsEnabled = selectedCount > 0;
            ConfirmButton.Content = selectedCount == 0
                ? _localization.Get("Import.Confirm")
                : _localization.Format("Import.ConfirmWithCount", selectedCount);
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelectionForVisible(true);

        private void SelectNone_Click(object sender, RoutedEventArgs e) => SetSelectionForVisible(false);

        /// <summary>
        /// Wirkt nur auf die sichtbaren Einträge, damit die Schaltflächen nicht
        /// unbemerkt ausgeblendete Programme mit auswählen.
        /// </summary>
        private void SetSelectionForVisible(bool isSelected)
        {
            if (_candidateView == null)
            {
                return;
            }

            foreach (var candidate in _candidateView.Cast<ImportCandidateViewModel>().ToList())
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
