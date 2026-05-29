using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GameLauncher.Services.MainWindow
{
    /// <summary>
    /// Handles staggered fly-in animations for game cards.
    /// Extracted from MainWindow code-behind to reduce complexity.
    /// </summary>
    public class AnimationService
    {
        /// <summary>
        /// Runs the staggered card animation on the given ListBox.
        /// </summary>
        /// <param name="listBox">The ListBox containing game cards.</param>
        /// <param name="setStartupActive">Action to set the IsStartupActive dependency property.</param>
        /// <param name="instant">If true, skip animation and show items immediately.</param>
        public async Task AnimateItemsStaggeredAsync(ListBox listBox, Action<bool> setStartupActive, bool instant = false)
        {
            int count = listBox.Items.Count;
            if (count == 0) return;

            if (instant)
            {
                var instantWatch = Stopwatch.StartNew();
                AnimateInstant(listBox, count);
                instantWatch.Stop();
                Models.Logger.Log($"Card animation skipped: instant update for {count} item(s) in {instantWatch.ElapsedMilliseconds} ms.");
                setStartupActive(false);
                return;
            }

            // STARTUP ANIMATION MODE
            var totalWatch = Stopwatch.StartNew();
            var frameStats = new AnimationFrameStats();
            Models.Logger.Log($"Card animation start: {count} item(s), visible containers before layout={CountGeneratedContainers(listBox, count)}.");
            setStartupActive(true);

            // Let WPF finish generating visible containers, then schedule all animations at once.
            var layoutWatch = Stopwatch.StartNew();
            await listBox.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            layoutWatch.Stop();

            frameStats.Start();
            var scheduleWatch = Stopwatch.StartNew();
            int animatedCount = ScheduleStartupAnimations(listBox, count);
            scheduleWatch.Stop();

            // Transition to global "Visible" state (handles virtualization)
            int cleanupDelay = animatedCount == 0 ? 80 : Math.Min(700, 420 + (animatedCount - 1) * 18);
            Models.Logger.Log($"Card animation scheduled: animated={animatedCount}/{count}, generated containers={CountGeneratedContainers(listBox, count)}, layout wait={layoutWatch.ElapsedMilliseconds} ms, scheduling={scheduleWatch.ElapsedMilliseconds} ms, cleanup delay={cleanupDelay} ms.");
            await Task.Delay(cleanupDelay);
            setStartupActive(false);

            // Final safety cleanup: remove animations to return control to XAML triggers
            var cleanupWatch = Stopwatch.StartNew();
            CleanupAnimations(listBox, count);
            cleanupWatch.Stop();
            frameStats.Stop();
            totalWatch.Stop();
            Models.Logger.Log($"Card animation completed: total={totalWatch.ElapsedMilliseconds} ms, cleanup={cleanupWatch.ElapsedMilliseconds} ms, {frameStats.BuildSummary()}.");
        }

        private static int ScheduleStartupAnimations(ListBox listBox, int count)
        {
            int animatedCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (i >= listBox.Items.Count) break;

                var container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (container == null) continue;

                var border = FindNamedBorder(container);
                if (border == null) continue;

                var translate = EnsureMutableTranslateTransform(border);
                int delayMs = Math.Min(320, animatedCount * 18);

                border.BeginAnimation(UIElement.OpacityProperty, null);
                border.Opacity = 0;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 20;

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var slideUp = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(360))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                border.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
                animatedCount++;
            }

            return animatedCount;
        }

        private static int CountGeneratedContainers(ListBox listBox, int count)
        {
            int generated = 0;
            for (int i = 0; i < count; i++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) != null)
                {
                    generated++;
                }
            }

            return generated;
        }

        /// <summary>
        /// Immediately shows all items without animation (for filters/instant updates).
        /// </summary>
        private static void AnimateInstant(ListBox listBox, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (container == null) continue;

                var border = FindNamedBorder(container);
                if (border == null) continue;

                var translate = EnsureMutableTranslateTransform(border);

                border.BeginAnimation(UIElement.OpacityProperty, null);
                border.Opacity = 1.0;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
            }
        }

        /// <summary>
        /// Removes all running animations to return control to XAML triggers.
        /// </summary>
        private static void CleanupAnimations(ListBox listBox, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var container = listBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (container == null) continue;

                var border = FindNamedBorder(container);
                if (border == null) continue;

                var translate = EnsureMutableTranslateTransform(border);

                border.BeginAnimation(UIElement.OpacityProperty, null);
                border.Opacity = 1.0;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = 0;
            }
        }

        /// <summary>
        /// Ensures the border's translate transform is mutable and directly addressable by XAML triggers.
        /// </summary>
        private static TranslateTransform EnsureMutableTranslateTransform(Border border)
        {
            if (border.RenderTransform is TranslateTransform translate)
            {
                if (translate.IsFrozen)
                {
                    translate = translate.Clone();
                    border.RenderTransform = translate;
                }

                return translate;
            }

            if (border.RenderTransform is TransformGroup group)
            {
                TranslateTransform? groupTranslate = null;

                if (group.IsFrozen)
                {
                    group = group.Clone();
                }

                foreach (var child in group.Children)
                {
                    if (child is TranslateTransform existingTranslate)
                    {
                        groupTranslate = existingTranslate.IsFrozen ? existingTranslate.Clone() : existingTranslate;
                        break;
                    }
                }

                var replacement = groupTranslate ?? new TranslateTransform(0, 20);
                border.RenderTransform = replacement;
                return replacement;
            }

            var newTranslate = new TranslateTransform(0, 20);
            border.RenderTransform = newTranslate;
            return newTranslate;
        }

        /// <summary>
        /// Finds the Border named "Container" within a ListBoxItem.
        /// </summary>
        private static Border? FindNamedBorder(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Border border && border.Name == "Container")
                    return border;

                var nested = FindNamedBorder(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private sealed class AnimationFrameStats
        {
            private TimeSpan? _lastRenderTime;
            private int _frameCount;
            private int _slowFrameCount;
            private double _maxFrameMs;

            public void Start()
            {
                _lastRenderTime = null;
                _frameCount = 0;
                _slowFrameCount = 0;
                _maxFrameMs = 0;
                CompositionTarget.Rendering += OnRendering;
            }

            public void Stop()
            {
                CompositionTarget.Rendering -= OnRendering;
            }

            public string BuildSummary() =>
                $"frames={_frameCount}, slow frames (>24ms)={_slowFrameCount}, max frame={_maxFrameMs:F1} ms";

            private void OnRendering(object? sender, EventArgs e)
            {
                if (e is not RenderingEventArgs renderingArgs)
                {
                    return;
                }

                if (_lastRenderTime.HasValue)
                {
                    double frameMs = (renderingArgs.RenderingTime - _lastRenderTime.Value).TotalMilliseconds;
                    _maxFrameMs = Math.Max(_maxFrameMs, frameMs);
                    if (frameMs > 24)
                    {
                        _slowFrameCount++;
                    }
                }

                _lastRenderTime = renderingArgs.RenderingTime;
                _frameCount++;
            }
        }

    }
}
