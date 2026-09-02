using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;
using GameLauncher.Services.Localization;
using GameLauncher.ViewModels;

namespace GameLauncher.Services.MainWindow
{
    internal sealed class MainWindowStartupCoordinator
    {
        private static readonly TimeSpan MetadataDelay = TimeSpan.FromMilliseconds(1200);

        private readonly GameManager _gameManager;
        private readonly MainViewModel _viewModel;
        private readonly UpdateCoordinator _updateCoordinator;
        private readonly LocalizationService _localization;

        public MainWindowStartupCoordinator(
            GameManager gameManager,
            MainViewModel viewModel,
            UpdateCoordinator updateCoordinator,
            LocalizationService localization)
        {
            _gameManager = gameManager;
            _viewModel = viewModel;
            _updateCoordinator = updateCoordinator;
            _localization = localization;
        }

        public async Task RunAsync(global::GameLauncher.MainWindow window)
        {
            try
            {
                window.InitializeTrayIcon();
                ShowSetupWizardIfRequired(window);

                var settings = _gameManager.Config.UISettings;
                window.ApplyUiSettings(settings, registerHotkey: false, writeLog: true);

                window.IsInitialLoading = true;
                await _viewModel.LoadGamesAsync(
                    loadSteamMetadataInBackground: false,
                    includeDeferredStartupGames: true);

                window.InitializeRuntimeServices();
                window.ApplyUiSettings(settings, registerHotkey: true, writeLog: false);
                await PrepareInitialLibraryAsync(window);
                window.IsInitialLoading = false;

                if (settings.AutoCheckUpdates)
                {
                    _ = _updateCoordinator.CheckForUpdatesAsync(window);
                }

                Logger.Log("MainWindow loaded and ready.");
                _ = RefreshMetadataDeferredAsync();
            }
            catch (Exception ex)
            {
                window.IsInitialLoading = false;
                Logger.Error("Error loading games in MainWindow", ex);
                ModernMessageWindow.Show(
                    _localization.Format("App.LoadError", ex.Message),
                    _localization.Get("Common.Error"),
                    ModernMessageWindow.ModernMessageButton.OK,
                    window);
            }
        }

        private void ShowSetupWizardIfRequired(Window owner)
        {
            if (!_gameManager.Config.UISettings.FirstStart)
            {
                return;
            }

            var wizard = new SetupWizardWindow(_gameManager) { Owner = owner };
            wizard.ShowDialog();
        }

        private async Task PrepareInitialLibraryAsync(global::GameLauncher.MainWindow window)
        {
            await window.Dispatcher.InvokeAsync(
                () => window.GameListControl.UpdateLayout(),
                DispatcherPriority.Loaded);
            window.RefreshLibrary(instant: true);
            await window.Dispatcher.InvokeAsync(
                () => window.GameListControl.UpdateLayout(),
                DispatcherPriority.Loaded);

            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        }

        private async Task RefreshMetadataDeferredAsync()
        {
            try
            {
                await Task.Delay(MetadataDelay);
                await _viewModel.RefreshSteamMetadataAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("Deferred Steam metadata refresh failed", ex);
            }
        }
    }
}
