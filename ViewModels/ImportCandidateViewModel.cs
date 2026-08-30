using GameLauncher.Core;
using GameLauncher.Services.Scanners;

namespace GameLauncher.ViewModels
{
    /// <summary>
    /// Ein im Import-Dialog zur Auswahl angebotenes Spiel.
    /// </summary>
    public sealed class ImportCandidateViewModel : ObservableObject
    {
        private bool _isSelected = true;

        internal ImportCandidateViewModel(ShortcutGameCandidate candidate)
        {
            Candidate = candidate;
        }

        internal ShortcutGameCandidate Candidate { get; }

        public string Name => Candidate.Name;

        public string TargetPath => Candidate.TargetPath;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
