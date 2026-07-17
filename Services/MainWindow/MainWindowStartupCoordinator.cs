using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GameLauncher.Converters;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;
using GameLauncher.Services.Localization;
using GameLauncher.ViewModels;

namespace GameLauncher.Services.MainWindow
{
    internal sealed class MainWindowStartupCoordinator
    {
        private const double PreloadVerticalBuffer = 1200;
        private const int WarmupImageCount = 24;
        private static readonly TimeSpan MetadataDelay = TimeSpan.FromMilliseconds(1200);

        private readonly GameManager _gameManager;
        private readonly MainViewModel _viewModel;
        private readonly GameImageCacheController _imageCacheController;
        private readonly UpdateCoordinator _updateCoordinator;
        private readonly LocalizationService _localization;

        public MainWindowStartupCoordinator(
            GameManager gameManager,
            MainViewModel viewModel,
            GameImageCacheController imageCacheController,
            UpdateCoordinator updateCoordinator,
            LocalizationService localization)
        {
            _gameManager = gameManager;
            _viewModel = viewModel;
            _imageCacheController = imageCacheController;
            _updateCoordinator = updateCoordinator;
            _localization = localization;
        }

        public async Task RunAsync(global::GameLauncher.MainWindow window)
        {
            try
            {
                window.InitializeTrayIcon();
                window.InitializeFpsCounter();
                ShowSetupWizardIfRequired(window);

                var settings = _gameManager.Config.UISettings;
                window.ApplyUiSettings(settings, registerHotkey: false, writeLog: true);

                window.IsInitialLoading = true;
                BitmapCacheConverter.BeginStartupCacheTracking();
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
                BitmapCacheConverter.ReleaseStartupStrongCache();
                Logger.Error("Error loading games in MainWindow", ex);
                MessageBox.Show(
                    _localization.Format("App.LoadError", ex.Message),
                    _localization.Get("Common.Error"));
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

            try
            {
                var preloadPaths = window.CollectStartupPreloadPaths(PreloadVerticalBuffer, WarmupImageCount);
                if (preloadPaths.Count > 0)
                {
                    Logger.Log($"Startup-Bildvorwärmung gestartet: {preloadPaths.Count} Cover.");
                    await BitmapCacheConverter.PreloadAsync(preloadPaths);
                }

                var visiblePaths = _imageCacheController.GetBufferedImagePaths(0);
                if (visiblePaths.Count > 0)
                {
                    Logger.Log($"Startup-Sichtbereich wird gezielt vorgewärmt: {visiblePaths.Count} Cover.");
                    await BitmapCacheConverter.PreloadAsync(visiblePaths);
                }

                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await _imageCacheController.WaitForVisibleImagesReadyAsync(TimeSpan.FromSeconds(4));
                await _imageCacheController.RefreshVisibleImagesAsync();
                await window.Dispatcher.InvokeAsync(
                    () => window.GameListControl.UpdateLayout(),
                    DispatcherPriority.Loaded);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Logger.Error("Warten auf sichtbare Startbilder fehlgeschlagen.", ex);
            }
            finally
            {
                BitmapCacheConverter.ReleaseStartupStrongCache();
            }
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
