using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using GameLauncher.Models;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class GameDetailsWindow : Window
    {
        private Game _game = null!;
        private GameManager _manager = null!;
        private readonly LocalizationService _localization = LocalizationService.Instance;

        public bool GameWasModified { get; private set; } = false;
        public event Action<Game>? LaunchGameRequested;

        public GameDetailsWindow(Game game, GameManager manager)
        {
            InitializeComponent();
            _game = game;
            _manager = manager;

            // Enable Dragging
            MouseLeftButtonDown += (s, e) => DragMove();
            
            // Set DataContext for Bindings (Metadata, Path, Args, etc.)
            DataContext = _game;

            // Populate UI (Legacy non-bound elements)
            GameNameText.Text = game.Name;
            PlatformText.Text = game.Platform;
            UpdateLocalizedTexts();
            
            SetPlatformColor(game.Platform);

            UpdateFavoriteUI();
            PopulateTagComboBox();

            // Optional: Close on Esc
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
            _localization.LanguageChanged += OnLanguageChanged;
        }

        private void PopulateTagComboBox()
        {
            AddTagComboBox.Items.Clear();
            AddTagComboBox.Items.Add(_localization.Get("Details.AddTag"));
            
            // Add default tags that are not already assigned
            foreach (var tag in Constants.Tags.DefaultTags)
            {
                if (!_game.Tags.Contains(tag))
                {
                    AddTagComboBox.Items.Add(tag);
                }
            }
            
            // Add used tags from other games that are not already assigned
            foreach (var tag in _manager.GetAllUsedTags())
            {
                if (!_game.Tags.Contains(tag) && !AddTagComboBox.Items.Contains(tag))
                {
                    AddTagComboBox.Items.Add(tag);
                }
            }
            
            AddTagComboBox.SelectedIndex = 0;
        }

        private void AddTag_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AddTagComboBox.SelectedIndex <= 0) return;
            
            var selectedTag = AddTagComboBox.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(selectedTag) && selectedTag != _localization.Get("Details.AddTag"))
            {
                _manager.AddTag(_game, selectedTag);
                GameWasModified = true;
                
                // Refresh UI
                TagsItemsControl.ItemsSource = null;
                TagsItemsControl.ItemsSource = _game.Tags;
                PopulateTagComboBox();
            }
        }

        private void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagName)
            {
                _manager.RemoveTag(_game, tagName);
                GameWasModified = true;
                
                // Refresh UI
                TagsItemsControl.ItemsSource = null;
                TagsItemsControl.ItemsSource = _game.Tags;
                PopulateTagComboBox();
            }
        }



        private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            _manager.ToggleFavorite(_game);
            UpdateFavoriteUI();
            GameWasModified = true;
        }

        private void UpdateFavoriteUI()
        {
            if (_game.IsFavorite)
            {
                FavoriteIcon.Text = "❤";
                FavoriteIcon.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                FavoriteIcon.Text = "♡";
                FavoriteIcon.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Enable Dark Mode Title Bar
            Services.DarkModeHelper.EnableDarkTitleBar(this);

            // Initial Focus
            PlayButton.Focus();

        }



        private void Play_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Delegate launch to main window (manager launch is handled there)
                LaunchGameRequested?.Invoke(_game);
                Close();
            }
            catch (Exception)
            {
                 MessageBox.Show(_localization.Get("Details.PlayErrorBody"), _localization.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetPlatformColor(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return;

            if (Constants.UI.PlatformBrushes.TryGetValue(platform, out var brush))
            {
                PlatformBadge.Background = brush;
            }
            else
            {
                PlatformBadge.Background = System.Windows.Media.Brushes.Gray;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
            base.OnClosed(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            UpdateLocalizedTexts();
            PopulateTagComboBox();
        }

        private void UpdateLocalizedTexts()
        {
            LastPlayedText.Text = _game.LastPlayed.HasValue
                ? _localization.Format("Details.LastPlayed", _game.LastPlayed.Value)
                : _localization.Get("Details.NeverPlayed");
        }
    }
}
