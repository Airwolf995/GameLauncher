using System;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.Services;
using GameLauncher.Services.Localization;
using GameLauncher.ViewModels;

namespace GameLauncher
{
    public partial class SettingsWindow : Window
    {
        private readonly LocalizationService _localization = LocalizationService.Instance;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            DarkModeHelper.EnableDarkTitleBar(this);
            Opacity = 1;
        }

        public SettingsWindow(GameManager gameManager, Action<string> onThemeChanged, Action<UISettings> onSettingsChanged)
        {
            DataContext = new SettingsViewModel(gameManager, onThemeChanged, onSettingsChanged);
            InitializeComponent();
            _localization.LanguageChanged += OnLanguageChanged;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult != true && DataContext is SettingsViewModel viewModel)
            {
                viewModel.RevertPreview();
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnClosed(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            CardSizeBox.Items.Refresh();
            ViewModeBox.Items.Refresh();
        }
    }
}
