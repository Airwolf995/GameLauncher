using GameLauncher.Core;
using GameLauncher.Services.Scanners;

namespace GameLauncher.ViewModels
{
    /// <summary>
    /// Ein im Import-Dialog zur Auswahl angebotenes Spiel.
    /// </summary>
    public sealed class ImportCandidateViewModel : ObservableObject
    {
        // Bewusst nicht vorausgewählt: die Liste enthält je nach Ansicht auch
        // Programme, die keine Spiele sind. Die Auswahl trifft der Benutzer.
        private bool _isSelected;

        internal ImportCandidateViewModel(ShortcutGameCandidate candidate)
        {
            Candidate = candidate;
        }

        internal ShortcutGameCandidate Candidate { get; }

        public string Name => Candidate.Name;

        public string TargetPath => Candidate.TargetPath;

        /// <summary>
        /// Steuert, ob der Eintrag in der Standardansicht erscheint oder erst beim
        /// Anzeigen aller Programme.
        /// </summary>
        public bool IsLikelyGame => Candidate.IsLikelyGame;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
