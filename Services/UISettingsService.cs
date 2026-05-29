using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLauncher.Models;

namespace GameLauncher.Services
{
    /// <summary>
    /// Service for applying UI settings like themes, font scaling, and background images.
    /// Extracted from MainWindow to reduce code complexity.
    /// </summary>
    public class UISettingsService
    {
        /// <summary>
        /// Applies the accent color theme to the application resources.
        /// </summary>
        /// <param name="colorCode">Hex color code like "#007ACC"</param>
        public void ApplyTheme(string colorCode)
        {
            try 
            {
                var color = (Color)ColorConverter.ConvertFromString(colorCode);
                var brush = new SolidColorBrush(color);
                Application.Current.Resources["AccentColor"] = brush;
                Logger.Log($"Theme applied: {colorCode}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to apply theme {colorCode}", ex);
            }
        }

        /// <summary>
        /// Applies font scaling to the main content grid.
        /// </summary>
        /// <param name="scale">Scale factor (0.8 - 1.4)</param>
        /// <param name="contentGrid">The main content Grid of the window</param>
        public void ApplyFontScale(double scale, Grid? contentGrid, bool writeLog = true)
        {
            if (contentGrid != null)
            {
                contentGrid.LayoutTransform = new ScaleTransform(scale, scale);
                if (writeLog)
                {
                    Logger.Log($"Font scale applied: {scale}");
                }
            }
        }

        /// <summary>
        /// Loads and applies a background image to an Image control.
        /// </summary>
        /// <param name="imagePath">Path to the background image file</param>
        /// <param name="backgroundImage">The Image control to apply the background to</param>
        public void ApplyBackgroundImage(string imagePath, Image? backgroundImage)
        {
            if (backgroundImage == null) return;

            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    backgroundImage.Source = bitmap;
                    Logger.Log($"Background image loaded: {imagePath}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to load background image: {imagePath}", ex);
                    backgroundImage.Source = null;
                }
            }
            else
            {
                backgroundImage.Source = null;
            }
        }

        /// <summary>
        /// Gets the UI configuration for a given card size.
        /// </summary>
        /// <param name="size">Card size enum value</param>
        /// <returns>CardSizeConfig with all relevant dimensions</returns>
        public CardSizeConfig GetCardSizeSettings(Models.CardSize size)
        {
            return size switch
            {
                Models.CardSize.Small => new CardSizeConfig(
                    PanelWidth: Constants.UI.CardWidthSmall,
                    PanelHeight: Constants.UI.CardHeightSmall,
                    ImageWidth: Constants.UI.ImageWidthSmall,
                    ImageHeight: Constants.UI.ImageHeightSmall,
                    TitleFontSize: Constants.UI.TitleFontSizeSmall,
                    PlatformFontSize: Constants.UI.PlatformFontSizeSmall,
                    CardMargin: new Thickness(10),
                    CardPadding: new Thickness(8)
                ),
                Models.CardSize.Large => new CardSizeConfig(
                    PanelWidth: Constants.UI.CardWidthLarge,
                    PanelHeight: Constants.UI.CardHeightLarge,
                    ImageWidth: Constants.UI.ImageWidthLarge,
                    ImageHeight: Constants.UI.ImageHeightLarge,
                    TitleFontSize: Constants.UI.TitleFontSizeLarge,
                    PlatformFontSize: Constants.UI.PlatformFontSizeLarge,
                    CardMargin: new Thickness(15),
                    CardPadding: new Thickness(15)
                ),
                _ => new CardSizeConfig( // Medium (default)
                    PanelWidth: Constants.UI.CardWidthMedium,
                    PanelHeight: Constants.UI.CardHeightMedium,
                    ImageWidth: Constants.UI.ImageWidthMedium,
                    ImageHeight: Constants.UI.ImageHeightMedium,
                    TitleFontSize: Constants.UI.TitleFontSizeMedium,
                    PlatformFontSize: Constants.UI.PlatformFontSizeMedium,
                    CardMargin: new Thickness(12),
                    CardPadding: new Thickness(12)
                )
            };
        }
    }

    /// <summary>
    /// Configuration record for card size settings.
    /// </summary>
    public record CardSizeConfig(
        double PanelWidth,
        double PanelHeight,
        double ImageWidth,
        double ImageHeight,
        double TitleFontSize,
        double PlatformFontSize,
        Thickness CardMargin,
        Thickness CardPadding
    );
}
