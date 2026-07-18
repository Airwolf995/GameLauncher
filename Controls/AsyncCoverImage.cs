using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Controls
{
    public sealed class AsyncCoverImage : Image
    {
        public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
            nameof(ImagePath),
            typeof(string),
            typeof(AsyncCoverImage),
            new PropertyMetadata(string.Empty, OnImagePathChanged));

        private CancellationTokenSource? _loadCts;

        public AsyncCoverImage()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public string ImagePath
        {
            get => (string)GetValue(ImagePathProperty);
            set => SetValue(ImagePathProperty, value);
        }

        private static void OnImagePathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var image = (AsyncCoverImage)dependencyObject;
            if (image.IsLoaded)
            {
                image.LoadCurrentImage();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => LoadCurrentImage();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CancelCurrentLoad();
            SetCurrentValue(SourceProperty, null);
        }

        private async void LoadCurrentImage()
        {
            CancelCurrentLoad();
            SetCurrentValue(SourceProperty, null);

            string imagePath = ImagePath;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return;
            }

            var loadCts = new CancellationTokenSource();
            _loadCts = loadCts;

            try
            {
                GameImageLoadResult result = await LoadWithSingleRetryAsync(
                    imagePath,
                    GameImageBitmapCache.LoadAsync,
                    Task.Delay,
                    loadCts.Token);

                if (result.Bitmap != null &&
                    !loadCts.IsCancellationRequested &&
                    ReferenceEquals(_loadCts, loadCts) &&
                    string.Equals(ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    SetCurrentValue(SourceProperty, result.Bitmap);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error($"Cover konnte nicht angezeigt werden: {imagePath}", ex);
            }
            finally
            {
                if (ReferenceEquals(_loadCts, loadCts))
                {
                    _loadCts = null;
                }

                loadCts.Dispose();
            }
        }

        internal static async Task<GameImageLoadResult> LoadWithSingleRetryAsync(
            string imagePath,
            Func<string, CancellationToken, Task<GameImageLoadResult>> loadAsync,
            Func<TimeSpan, CancellationToken, Task> delayAsync,
            CancellationToken cancellationToken)
        {
            GameImageLoadResult result = await loadAsync(imagePath, cancellationToken);
            if (!result.ShouldRetryAutomatically)
            {
                return result;
            }

            await delayAsync(result.RetryDelay, cancellationToken);
            return await loadAsync(imagePath, cancellationToken);
        }

        private void CancelCurrentLoad()
        {
            _loadCts?.Cancel();
            _loadCts = null;
        }
    }
}
