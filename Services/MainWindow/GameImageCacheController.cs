using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameLauncher.Controls;
using GameLauncher.Core;
using GameLauncher.Models;

namespace GameLauncher.Services.MainWindow
{
    internal sealed class GameImageCacheController : IDisposable
    {
        private const double ViewportRetentionVerticalBuffer = 300;
        private const double ViewChangePreloadVerticalBuffer = 1600;
        private readonly ListBox _gameList;
        private readonly DispatcherTimer _retentionTimer;
        private readonly DispatcherTimer _viewChangeTimer;
        private ScrollViewer? _scrollViewer;
        private CancellationTokenSource? _viewChangePreloadCts;

        public GameImageCacheController(ListBox gameList)
        {
            _gameList = gameList;
            _retentionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _retentionTimer.Tick += OnRetentionTimerTick;

            _viewChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            _viewChangeTimer.Tick += OnViewChangeTimerTick;

            _gameList.SizeChanged += OnGameListSizeChanged;
            _gameList.Loaded += OnGameListLoaded;
        }

        public void ScheduleViewportRetentionUpdate()
        {
            _retentionTimer.Stop();
            _retentionTimer.Start();
        }

        public void ScheduleViewChangeStabilization()
        {
            _viewChangeTimer.Stop();
            _viewChangeTimer.Start();
        }

        public async Task WaitForVisibleImagesReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                bool allReady = await _gameList.Dispatcher.InvokeAsync(AreVisibleImagesReady);
                if (allReady)
                {
                    return;
                }

                try
                {
                    await Task.Delay(60, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public IReadOnlyList<string> GetBufferedImagePaths(double verticalBuffer)
        {
            ScrollViewer? scrollViewer = _gameList.FindDescendant<ScrollViewer>();
            if (scrollViewer == null)
            {
                return [];
            }

            if (_gameList.FindDescendant<VirtualizingWrapPanel>() is VirtualizingWrapPanel wrapPanel)
            {
                var range = wrapPanel.GetBufferedIndexRange(verticalBuffer);
                return CollectImagePaths(range.firstIndex, range.lastIndexExclusive);
            }

            var visiblePaths = new List<string>();
            var viewport = new Rect(0, 0, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
            viewport.Inflate(0, verticalBuffer);

            for (int index = 0; index < _gameList.Items.Count; index++)
            {
                if (_gameList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container)
                {
                    continue;
                }

                Rect bounds = container.TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (!bounds.IntersectsWith(viewport))
                {
                    continue;
                }

                AddImagePathsFromItem(container.DataContext, visiblePaths);
            }

            return visiblePaths;
        }

        public void Dispose()
        {
            _retentionTimer.Stop();
            _retentionTimer.Tick -= OnRetentionTimerTick;
            _viewChangeTimer.Stop();
            _viewChangeTimer.Tick -= OnViewChangeTimerTick;
            _gameList.SizeChanged -= OnGameListSizeChanged;
            _gameList.Loaded -= OnGameListLoaded;
            DetachScrollViewer();
            _viewChangePreloadCts?.Cancel();
            _viewChangePreloadCts?.Dispose();
        }

        private bool AreVisibleImagesReady()
        {
            ScrollViewer? scrollViewer = _gameList.FindDescendant<ScrollViewer>();
            if (scrollViewer == null)
            {
                return true;
            }

            var visiblePaths = new HashSet<string>(
                GetBufferedImagePaths(0),
                StringComparer.OrdinalIgnoreCase);

            if (visiblePaths.Count == 0)
            {
                return true;
            }

            foreach (string path in visiblePaths)
            {
                if (!BitmapCacheConverter.IsCachedForUi(path))
                {
                    return false;
                }
            }

            return true;
        }

        private List<string> CollectImagePaths(int firstIndex, int lastIndexExclusive)
        {
            var paths = new List<string>(Math.Max(0, lastIndexExclusive - firstIndex));
            for (int index = firstIndex; index < lastIndexExclusive; index++)
            {
                AddImagePathsFromItem(_gameList.Items[index], paths);
            }

            return paths;
        }

        private static void AddImagePathsFromItem(object? item, ICollection<string> target)
        {
            switch (item)
            {
                case Game game when !string.IsNullOrWhiteSpace(game.ImageUrl):
                    target.Add(game.ImageUrl);
                    break;
                case GameRow row:
                    foreach (Game rowGame in row.Games)
                    {
                        if (!string.IsNullOrWhiteSpace(rowGame.ImageUrl))
                        {
                            target.Add(rowGame.ImageUrl);
                        }
                    }
                    break;
            }
        }

        private static void CollectDescendantImages(DependencyObject parent, ICollection<Image> images)
        {
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int index = 0; index < childCount; index++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                if (child is Image image)
                {
                    images.Add(image);
                }

                CollectDescendantImages(child, images);
            }
        }

        private void OnGameListLoaded(object sender, RoutedEventArgs e)
        {
            EnsureScrollViewerHooked();
            ScheduleViewportRetentionUpdate();
        }

        private void OnGameListSizeChanged(object sender, SizeChangedEventArgs e)
        {
            EnsureScrollViewerHooked();
            ScheduleViewportRetentionUpdate();
        }

        private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (Math.Abs(e.VerticalChange) < 0.1 && Math.Abs(e.ViewportHeightChange) < 0.1)
            {
                return;
            }

            ScheduleViewportRetentionUpdate();
        }

        private void OnRetentionTimerTick(object? sender, EventArgs e)
        {
            _retentionTimer.Stop();
            BitmapCacheConverter.UpdateViewportRetention(GetBufferedImagePaths(ViewportRetentionVerticalBuffer));
        }

        private void OnViewChangeTimerTick(object? sender, EventArgs e)
        {
            _viewChangeTimer.Stop();
            _ = StabilizeAfterViewChangeAsync();
        }

        private void EnsureScrollViewerHooked()
        {
            if (_scrollViewer != null)
            {
                return;
            }

            _gameList.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_scrollViewer != null)
                    {
                        return;
                    }

                    _scrollViewer = _gameList.FindDescendant<ScrollViewer>();
                    if (_scrollViewer != null)
                    {
                        _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
                    }
                }),
                DispatcherPriority.Loaded);
        }

        private void DetachScrollViewer()
        {
            if (_scrollViewer == null)
            {
                return;
            }

            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
            _scrollViewer = null;
        }

        private async Task StabilizeAfterViewChangeAsync()
        {
            _viewChangePreloadCts?.Cancel();
            _viewChangePreloadCts?.Dispose();
            _viewChangePreloadCts = new CancellationTokenSource();
            var cancellationToken = _viewChangePreloadCts.Token;

            try
            {
                await _gameList.Dispatcher.InvokeAsync(_gameList.UpdateLayout, DispatcherPriority.Loaded, cancellationToken);

                var imagePaths = GetBufferedImagePaths(ViewChangePreloadVerticalBuffer);
                BitmapCacheConverter.UpdateViewportRetention(imagePaths);

                if (imagePaths.Count == 0)
                {
                    return;
                }

                await BitmapCacheConverter.PreloadAsync(imagePaths, cancellationToken);
                Logger.Log($"Library view stabilization completed: preloaded {imagePaths.Count} buffered cover image(s) after filter/search/sort change.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error("Library view stabilization failed", ex);
            }
        }
    }
}
