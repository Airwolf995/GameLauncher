using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameLauncher.Controls;
using GameLauncher.Models;

namespace GameLauncher.Services.MainWindow
{
    public sealed class GameCardLayoutService : IGameCardLayoutService
    {
        private readonly UISettingsService _uiSettingsService;
        private readonly ViewModeAnimationPolicy _animationPolicy;

        public GameCardLayoutService(UISettingsService uiSettingsService)
        {
            _uiSettingsService = uiSettingsService;
            _animationPolicy = new ViewModeAnimationPolicy();
        }

        public void ApplyCardSize(ListBox gameListControl, ResourceDictionary resources, CardSize size, bool refresh = true)
        {
            var panel = FindVisualChild<VirtualizingWrapPanel>(gameListControl);
            if (panel == null)
            {
                return;
            }

            var config = _uiSettingsService.GetCardSizeSettings(size);

            panel.ItemWidth = config.PanelWidth;
            panel.ItemHeight = config.PanelHeight;

            resources["GameImageWidth"] = config.ImageWidth;
            resources["GameImageHeight"] = config.ImageHeight;
            resources["GameTitleFontSize"] = config.TitleFontSize;
            resources["GamePlatformFontSize"] = config.PlatformFontSize;
            resources["CardMargin"] = config.CardMargin;
            resources["CardPadding"] = config.CardPadding;

            if (refresh)
            {
                gameListControl.Items.Refresh();
            }
        }

        public ViewModeAnimationAction ApplyViewMode(
            ListBox gameListControl,
            ResourceDictionary resources,
            ViewMode mode,
            DataTemplate? originalCardTemplate,
            CardSize currentCardSize,
            bool refresh = true)
        {
            if (mode == ViewMode.List)
            {
                ApplyListMode(gameListControl, resources);
                return _animationPolicy.GetAction(isCardMode: false, refresh, gameListControl.Items.Count);
            }

            var config = _uiSettingsService.GetCardSizeSettings(currentCardSize);
            ApplyCardMode(gameListControl, originalCardTemplate, config);
            ApplyCardSize(gameListControl, resources, currentCardSize, refresh);

            return _animationPolicy.GetAction(isCardMode: true, refresh, gameListControl.Items.Count);
        }

        private static void ApplyListMode(ListBox gameListControl, ResourceDictionary resources)
        {
            var stackPanelTemplate = new ItemsPanelTemplate();
            var stackPanelFactory = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
            stackPanelTemplate.VisualTree = stackPanelFactory;
            gameListControl.ItemsPanel = stackPanelTemplate;

            var listTemplate = new DataTemplate();
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2D30")));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(10));
            borderFactory.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 2));

            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            AddListColumns(gridFactory);
            AddImageColumn(gridFactory, resources);
            AddTitleColumn(gridFactory);
            AddPlatformColumn(gridFactory);
            AddFavoriteColumn(gridFactory, resources);

            borderFactory.AppendChild(gridFactory);
            listTemplate.VisualTree = borderFactory;
            gameListControl.ItemTemplate = listTemplate;
            gameListControl.SelectedIndex = -1;
        }

        private static void ApplyCardMode(ListBox gameListControl, DataTemplate? originalCardTemplate, CardSizeConfig config)
        {
            var wrapPanelTemplate = new ItemsPanelTemplate();
            var wrapPanelFactory = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
            wrapPanelFactory.SetValue(VirtualizingWrapPanel.ItemWidthProperty, config.PanelWidth);
            wrapPanelFactory.SetValue(VirtualizingWrapPanel.ItemHeightProperty, config.PanelHeight);
            wrapPanelFactory.SetValue(FrameworkElement.ClipToBoundsProperty, false);
            wrapPanelFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(40, 0, 10, 0));
            wrapPanelTemplate.VisualTree = wrapPanelFactory;
            gameListControl.ItemsPanel = wrapPanelTemplate;
            gameListControl.ItemTemplate = originalCardTemplate;
        }

        private static void AddListColumns(FrameworkElementFactory gridFactory)
        {
            var widths = new[]
            {
                new GridLength(100, GridUnitType.Pixel),
                new GridLength(1, GridUnitType.Star),
                new GridLength(150, GridUnitType.Pixel),
                new GridLength(30, GridUnitType.Pixel)
            };

            foreach (var width in widths)
            {
                var column = new FrameworkElementFactory(typeof(ColumnDefinition));
                column.SetValue(ColumnDefinition.WidthProperty, width);
                gridFactory.AppendChild(column);
            }
        }

        private static void AddImageColumn(FrameworkElementFactory gridFactory, ResourceDictionary resources)
        {
            var imageFactory = new FrameworkElementFactory(typeof(Image));
            imageFactory.SetBinding(Image.SourceProperty, new Binding("ImageUrl")
            {
                Converter = resources["BitmapCacheConverter"] as IValueConverter,
                IsAsync = true,
                NotifyOnTargetUpdated = true
            });
            // List mode image size (85x40 fits the Steam ratio 2.14 almost perfectly)
            imageFactory.SetValue(Image.WidthProperty, 85.0);
            imageFactory.SetValue(Image.HeightProperty, 40.0);
            imageFactory.SetValue(Image.StretchProperty, Stretch.UniformToFill);
            imageFactory.SetValue(UIElement.OpacityProperty, 1.0);
            imageFactory.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.Fant);
            imageFactory.SetValue(Grid.ColumnProperty, 0);
            imageFactory.AddHandler(Binding.TargetUpdatedEvent, new EventHandler<DataTransferEventArgs>(OnGameImageTargetUpdated));
            gridFactory.AppendChild(imageFactory);
        }

        private static void OnGameImageTargetUpdated(object? sender, DataTransferEventArgs e)
        {
            if (sender is not Image image)
            {
                return;
            }

            if (image.Source == null)
            {
                if (Window.GetWindow(image) is GameLauncher.MainWindow startupWindow && startupWindow.AreImageLoadTransitionsEnabled)
                {
                    image.Opacity = 0;
                }
                else
                {
                    image.Opacity = 1;
                }
                return;
            }

            if (Window.GetWindow(image) is GameLauncher.MainWindow mainWindow && !mainWindow.AreImageLoadTransitionsEnabled)
            {
                image.BeginAnimation(UIElement.OpacityProperty, null);
                image.Opacity = 1;
                return;
            }

            image.BeginAnimation(UIElement.OpacityProperty, null);
            image.Opacity = 0;
            image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }

        private static void AddTitleColumn(FrameworkElementFactory gridFactory)
        {
            var titleFactory = new FrameworkElementFactory(typeof(TextBlock));
            titleFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            titleFactory.SetValue(TextBlock.FontSizeProperty, 14.0);
            titleFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            titleFactory.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            titleFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            titleFactory.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0));
            titleFactory.SetValue(Grid.ColumnProperty, 1);
            gridFactory.AppendChild(titleFactory);
        }

        private static void AddPlatformColumn(FrameworkElementFactory gridFactory)
        {
            var platformFactory = new FrameworkElementFactory(typeof(TextBlock));
            platformFactory.SetBinding(TextBlock.TextProperty, new Binding("Platform"));
            platformFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            platformFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")));
            platformFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            platformFactory.SetValue(Grid.ColumnProperty, 2);
            gridFactory.AppendChild(platformFactory);
        }

        private static void AddFavoriteColumn(FrameworkElementFactory gridFactory, ResourceDictionary resources)
        {
            var converter = resources["BooleanToVisibilityConverter"] as IValueConverter;

            var favoriteFactory = new FrameworkElementFactory(typeof(TextBlock));
            favoriteFactory.SetValue(TextBlock.TextProperty, "⭐");
            favoriteFactory.SetValue(TextBlock.FontSizeProperty, 16.0);
            favoriteFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")));
            favoriteFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            favoriteFactory.SetBinding(TextBlock.VisibilityProperty, new Binding("IsFavorite")
            {
                Converter = converter
            });
            favoriteFactory.SetValue(Grid.ColumnProperty, 3);
            gridFactory.AppendChild(favoriteFactory);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
