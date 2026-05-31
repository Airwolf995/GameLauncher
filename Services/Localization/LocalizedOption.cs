using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameLauncher.Services.Localization
{
    public sealed class LocalizedOption : INotifyPropertyChanged
    {
        private string _displayName = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public required string Key { get; init; }
        public required string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName == value)
                {
                    return;
                }

                _displayName = value;
                OnPropertyChanged();
            }
        }

        public bool IsSeparator { get; init; }

        public override string ToString() => DisplayName;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
