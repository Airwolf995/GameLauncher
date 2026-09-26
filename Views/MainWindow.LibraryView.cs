using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using GameLauncher.Core;
using GameLauncher.Models;
using GameLauncher.Services.MainWindow;

namespace GameLauncher
{
    using GameLauncher.ViewModels;

    /// <summary>
    /// Darstellung der Bibliothek: Ansichtsmodus, Kartenraster, Animationen,
    /// Oberflächeneinstellungen und Bildlaufposition.
    /// </summary>
    public partial class MainWindow
    {
        private UiSettingsSnapshot? _lastAppliedUiSettings;
        private ViewMode _currentViewMode = ViewMode.Cards;
        private CardSize _currentCardSize = CardSize.Medium;
        private int _currentCardColumns = 1;
        private bool _resetViewportAfterNextRefresh;

        private void ComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is not ComboBox comboBox)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(() => AlignComboBoxDropDown(comboBox)),
                DispatcherPriority.Loaded);
        }

        private static void AlignComboBoxDropDown(ComboBox comboBox)
        {
            if (comboBox.SelectedIndex < 0)
            {
                return;
            }

            if (comboBox.Template.FindName("Popup", comboBox) is not Popup popup || popup.Child is not DependencyObject popupChild)
            {
                return;
            }

            var scrollViewer = popupChild.FindDescendant<ScrollViewer>();
            if (scrollViewer == null)
            {
                return;
            }

            comboBox.UpdateLayout();

            int anchorIndex = Math.Max(0, comboBox.SelectedIndex - 1);
            if (comboBox.ItemContainerGenerator.ContainerFromIndex(anchorIndex) is not ComboBoxItem anchorItem)
            {
                return;
            }

            var top = anchorItem.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
            if (Math.Abs(top) > 0.5)
            {
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + top));
            }
        }

        private void RefreshList(bool instant = true)
        {
            // Re-animate items when list is refreshed/filtered
            Dispatcher.BeginInvoke(new Action(() => {
                AnimateItemsStaggered(instant);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        internal void RefreshLibrary(bool instant) => RefreshList(instant);

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SearchText = string.Empty;
        }

        private void LibraryViewStateChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || IsInitialLoading)
            {
                return;
            }

            string sourceName = sender switch
            {
                FrameworkElement element when !string.IsNullOrWhiteSpace(element.Name) => element.Name,
                _ => sender.GetType().Name
            };
            Logger.Log($"Bibliotheksansicht geändert: Auslöser={sourceName}, Einträge={GameListControl.Items.Count}.");

            if (ReferenceEquals(sender, FilterBox) || ReferenceEquals(sender, SortBox))
            {
                _resetViewportAfterNextRefresh = true;
            }
        }

        private void OnLibraryViewRefreshed(object? sender, EventArgs e)
        {
            if (!IsLoaded || IsInitialLoading)
            {
                return;
            }

            if (_resetViewportAfterNextRefresh)
            {
                ResetLibraryViewportToTop();
                _resetViewportAfterNextRefresh = false;
            }

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var realizedRange = GameLauncher.Core.RealizedItemRange.For(GameListControl);
                    if (realizedRange.IsEmpty)
                    {
                        Logger.Log($"Virtualisierung nach Ansichtswechsel: noch nichts realisiert, Gesamt={GameListControl.Items.Count}.");
                    }
                    else
                    {
                        Logger.Log($"Virtualisierung nach Ansichtswechsel: realisiert={realizedRange.Count}, Bereich={realizedRange.FirstIndex}-{realizedRange.LastIndexExclusive - 1}, Gesamt={GameListControl.Items.Count}.");
                    }
                }),
                DispatcherPriority.Loaded);
        }

        private void ResetLibraryViewportToTop()
        {
            if (GameListControl.FindDescendant<ScrollViewer>() is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(0);
            }
        }

        private void ApplySavedUiSettings()
        {
            ApplyUiSettings(_gameManager.Config.UISettings, registerHotkey: true, writeLog: true);
        }

        private void ApplyUiSettingsPreview(UISettings uiSettings)
        {
            ApplyUiSettings(uiSettings, registerHotkey: false, writeLog: false);
        }

        internal void ApplyUiSettings(UISettings uiSettings, bool registerHotkey, bool writeLog)
        {
            var snapshot = UiSettingsSnapshot.From(uiSettings);
            if (_lastAppliedUiSettings == snapshot)
            {
                if (registerHotkey && IsLoaded)
                {
                    _overlayController.RegisterHotkey(this, uiSettings);
                }
                return;
            }

            var previous = _lastAppliedUiSettings;

            if (snapshot.ViewDiffersFrom(previous))
            {
                ApplyViewMode(uiSettings.ViewMode, uiSettings.CardSize, false);
            }

            if (snapshot.AnimationsDifferFrom(previous))
            {
                ApplyAnimations(uiSettings.AnimationsEnabled, writeLog);
            }

            if (snapshot.FontScaleDiffersFrom(previous))
            {
                _uiSettingsService.ApplyFontScale(uiSettings.FontScale, this.Content as Grid, writeLog);
            }

            if (snapshot.BackgroundImageDiffersFrom(previous))
            {
                _uiSettingsService.ApplyBackgroundImage(uiSettings.BackgroundImage, BackgroundImage);
            }

            if (writeLog)
            {
                Logger.Log($"UI Settings applied: CardSize={uiSettings.CardSize}, ViewMode={uiSettings.ViewMode}, Animations={uiSettings.AnimationsEnabled}, FontScale={uiSettings.FontScale}");
            }

            if (registerHotkey && IsLoaded)
            {
                _overlayController.RegisterHotkey(this, uiSettings);
            }

            _lastAppliedUiSettings = snapshot;
        }

        private void ApplyViewMode(Models.ViewMode mode, Models.CardSize size, bool refresh = true)
        {
            _currentViewMode = mode;
            _currentCardSize = size;
            BindingOperations.SetBinding(
                GameListControl,
                ItemsControl.ItemsSourceProperty,
                new Binding(mode == ViewMode.Cards ? nameof(MainViewModel.CardRows) : nameof(MainViewModel.GamesView)));
            GameListControl.ItemContainerStyle = mode == ViewMode.Cards
                ? Resources["GameRowItemContainerStyle"] as Style
                : Resources["GameListItemContainerStyle"] as Style;

            var action = _gameCardLayoutService.ApplyViewMode(
                GameListControl,
                Resources,
                mode,
                _originalCardTemplate,
                size,
                refresh);

            UpdateCardRowsLayout();

            if (action == Services.MainWindow.ViewModeAnimationAction.Animate)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AnimateItemsStaggered();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            else if (action == Services.MainWindow.ViewModeAnimationAction.AnimateInstant)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    AnimateItemsStaggered(true);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void GameListControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded || _currentViewMode != ViewMode.Cards)
            {
                return;
            }

            UpdateCardRowsLayout();
        }

        private void UpdateCardRowsLayout()
        {
            if (_viewModel == null || _currentViewMode != ViewMode.Cards || GameListControl.ActualWidth <= 0)
            {
                return;
            }

            var layoutResult = _gameCardLayoutService.ApplyCardRowLayout(
                Resources,
                GameListControl.ActualWidth,
                _currentCardSize,
                _currentCardColumns);

            _currentCardColumns = layoutResult.Columns;
            if (_viewModel.UpdateCardColumns(layoutResult.Columns))
            {
                Logger.Log($"Kartenzeilen aktualisiert: Breite={GameListControl.ActualWidth:0.#}, Spalten={layoutResult.Columns}, Kartenbreite={layoutResult.CardWidth:0.#}, Kartengröße={_currentCardSize}.");
            }
        }

        private void ApplyAnimations(bool enabled, bool writeLog = true)
        {
            // Toggle the dependency property so XAML triggers can react
            this.AreAnimationsEnabled = enabled;
            if (writeLog)
            {
                Logger.Log($"Animations {(enabled ? "enabled" : "disabled")}");
            }
        }

        private async void AnimateItemsStaggered(bool instant = false)
        {
            instant = instant || !AreAnimationsEnabled;
            await _animationService.AnimateItemsStaggeredAsync(
                GameListControl,
                instant);
        }
    }
}
