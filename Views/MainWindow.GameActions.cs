using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameLauncher.Models;
using GameLauncher.Services.GameManagement;

namespace GameLauncher
{
    /// <summary>
    /// Aktionen an einzelnen Spielen: hinzufügen, importieren, starten,
    /// bearbeiten, ausblenden und löschen sowie die Detailansicht.
    /// </summary>
    public partial class MainWindow
    {
        private async void AddGame_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = _gameManager.Config.UISettings.SteamGridDbApiKey;
            var dialog = new AddGameWindow(apiKey) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                // Add game in manager but don't trigger the global event (that would cause a full reload/re-animation)
                var newGame = _gameManager.AddManualGame(dialog.GameName, dialog.GamePath, dialog.GameArgs, dialog.GameCoverPath, notifyUI: false);
                
                // Add to our main collection instantly via ViewModel
                _viewModel.Games.Add(newGame);
                
                await _viewModel.RebuildLibraryViewAsync();
                ShowStatus(_localization.Get("Main.StatusGameAdded"));
                Logger.Log("User added a manual game. Added instantly to list.");
                
                // Refresh instantly so the new item (Opacity 0) becomes visible immediately
                RefreshList(instant: true);
            }
        }

        private async void ImportGames_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ImportGamesWindow(_viewModel.Games) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.SelectedCandidates.Count == 0)
            {
                return;
            }

            // Wie beim manuellen Hinzufügen ohne globales Event arbeiten, damit die
            // Bibliothek nicht komplett neu geladen und animiert wird. Das Übernehmen
            // liest je Spiel das Symbol aus der Programmdatei und läuft deshalb
            // außerhalb des UI-Threads.
            var candidates = dialog.SelectedCandidates;
            var addedGames = await Task.Run(() => candidates
                .Select(candidate => _gameManager.AddManualGame(
                    candidate.Name,
                    candidate.TargetPath,
                    candidate.Arguments,
                    notifyUI: false))
                .ToList());

            foreach (var game in addedGames)
            {
                _viewModel.Games.Add(game);
            }

            await _viewModel.RebuildLibraryViewAsync();
            ShowStatus(_localization.Format("Main.StatusGamesImported", dialog.SelectedCandidates.Count));
            Logger.Log($"{dialog.SelectedCandidates.Count} Spiel(e) über Verknüpfungen importiert.");

            RefreshList(instant: true);
        }

        private void GameCard_Click(object sender, RoutedEventArgs e)
        {
            if (TryGetGameFromSender(sender, out Game? game))
            {
                OpenGameDetails(game!);
            }
        }

        private void GameTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (!TryGetGameFromSender(sender, out Game? game))
            {
                return;
            }

            OpenGameDetails(game!);
            e.Handled = true;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
               LaunchGame(game);
            }
        }

        private async void ChangeImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = _localization.Get("Main.ChangeImageDialogFilter"),
                    Title = _localization.Get("Main.ChangeImageDialogTitle")
                };

                if (dialog.ShowDialog() == true)
                {
                    _gameManager.SetManualGameImage(game, dialog.FileName, notifyUI: false);
                    await _viewModel.RebuildLibraryViewAsync();
                    RefreshList(instant: true);
                    ShowStatus(_localization.Get("Main.StatusImageUpdated"));
                }
            }
        }

        private async void Favorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                _gameManager.ToggleFavorite(game, notifyUI: false);
                 await _viewModel.RebuildLibraryViewAsync();
                 RefreshList(instant: true);
                 ShowStatus(game.IsFavorite ? _localization.Get("Main.StatusFavoriteAdded") : _localization.Get("Main.StatusFavoriteRemoved"));
            }
        }

        private async void Hide_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                 if (game.IsHidden)
                 {
                     _gameManager.UnhideGame(game, notifyUI: false);
                     ShowStatus(_localization.Get("Main.StatusGameShown"));
                 }
                 else if (ModernMessageWindow.Show(
                     _localization.Format("Main.HideConfirmBody", game.Name),
                     _localization.Get("Main.HideConfirmTitle"),
                     ModernMessageWindow.ModernMessageButton.YesNo,
                     this) == MessageBoxResult.Yes)
                 {
                     _gameManager.HideGame(game, notifyUI: false);
                     ShowStatus(_localization.Get("Main.StatusGameHidden"));
                 }
                 else
                 {
                     return;
                 }

                 await _viewModel.RebuildLibraryViewAsync();
                 RefreshList(instant: true);
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                string apiKey = _gameManager.Config.UISettings.SteamGridDbApiKey;
                var dialog = new AddGameWindow(apiKey, game) { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                // Das Bild zuerst setzen: UpdateManualGame meldet die Änderung
                // und löst damit die Aktualisierung der Bibliothek aus.
                if (!string.IsNullOrEmpty(dialog.GameCoverPath))
                {
                    _gameManager.SetDownloadedGameImage(game, dialog.GameCoverPath);
                }

                _gameManager.UpdateManualGame(game, dialog.GameName, dialog.GamePath, dialog.GameArgs);
                ShowStatus(_localization.Get("Main.StatusGameUpdated"));
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is Game game)
            {
                if (ModernMessageWindow.Show(
                    _localization.Format("Main.DeleteConfirmBody", game.Name),
                    _localization.Get("Main.DeleteConfirmTitle"),
                    ModernMessageWindow.ModernMessageButton.YesNo,
                    this) == MessageBoxResult.Yes)
                {
                    // Remove but don't trigger global update
                    _gameManager.RemoveManualGame(game, notifyUI: false);
                    
                    // Remove from our visible collection instantly via ViewModel
                    _viewModel.Games.Remove(game);
                    
                    await _viewModel.RebuildLibraryViewAsync();
                    ShowStatus(_localization.Get("Main.StatusGameDeleted"));
                    RefreshList(instant: true);
                }
            }
        }

        private void LaunchGame(Game game)
        {
            try
            {
                _gameManager.LaunchGame(game, notifyUI: false);
                
                // Update LastPlayed immediately on launch
                game.LastPlayed = DateTime.Now;
                _gameManager.UpdateLastPlayed(game.Id, game.LastPlayed.Value);
                _gameManager.NotifyGamesUpdated();

                ShowStatus(_localization.Format("Main.StatusLaunching", game.Name));

                // Handle launcher behavior on game start
                var settings = _gameManager?.Config?.UISettings;
                if (settings != null)
                {
                    if (settings.CloseOnGameStart)
                    {
                        // Mark as explicit exit so OnClosing bypasses MinimizeToTray.
                        BeginExit();
                        Close();
                    }
                    else if (settings.MinimizeOnGameStart)
                    {
                        this.WindowState = WindowState.Minimized;
                    }
                }
            }
            catch (Exception ex)
            {
                // Logger.Error handled in GameManager
                if (GameManager.IsMissingFileError(ex))
                {
                     ModernMessageWindow.Show(_localization.Format("Main.FileMissingBody", game.Path), _localization.Get("Main.FileMissingTitle"), ModernMessageWindow.ModernMessageButton.OK, this);
                }
                else
                {
                     ModernMessageWindow.Show(_localization.Get("Main.LaunchErrorBody"), _localization.Get("Common.Error"), ModernMessageWindow.ModernMessageButton.OK, this);
                }
                ShowStatus(_localization.Get("Main.StatusError"));
            }
        }

        private void OpenGameDetails(Game game)
        {
            var details = new GameDetailsWindow(game, _gameManager);
            details.Owner = this;
            details.LaunchGameRequested += LaunchGame;
            Logger.Log($"Opening details for: {game.Name}");
            details.ShowDialog();
        }

        private static bool TryGetGameFromSender(object sender, out Game? game)
        {
            game = sender switch
            {
                FrameworkElement element when element.DataContext is Game senderGame => senderGame,
                _ => null
            };

            return game != null;
        }
    }
}
