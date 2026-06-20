using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameLauncher.Controls;

namespace GameLauncher.Tests
{
    public class VirtualizingWrapPanelTests
    {
        [Fact]
        public void RecyclingMode_ReusesContainersWhenVisibleRangeChanges()
        {
            RunInSta(() =>
            {
                var listBox = CreateListBox();
                Layout(listBox);

                var panel = FindVisualChild<VirtualizingWrapPanel>(listBox);
                Assert.NotNull(panel);

                var initialContainers = GetRealizedContainers(panel!);
                Assert.NotEmpty(initialContainers);

                panel!.SetVerticalOffset(panel.ExtentHeight);
                LayoutPanel(panel);

                var containersAfterScrolling = GetRealizedContainers(panel);
                Assert.Contains(
                    containersAfterScrolling,
                    container => initialContainers.Contains(container));

                panel.SetVerticalOffset(0);
                LayoutPanel(panel);

                var realizedRange = panel.GetRealizedIndexRange();
                Assert.Equal(0, realizedRange.firstIndex);
                Assert.True(realizedRange.lastIndexExclusive > realizedRange.firstIndex);
            });
        }

        private static ListBox CreateListBox()
        {
            var panelFactory = new FrameworkElementFactory(typeof(VirtualizingWrapPanel));
            panelFactory.SetValue(VirtualizingWrapPanel.ItemWidthProperty, 100.0);
            panelFactory.SetValue(VirtualizingWrapPanel.ItemHeightProperty, 20.0);

            var listBox = new ListBox
            {
                Width = 200,
                Height = 100,
                ItemsPanel = new ItemsPanelTemplate(panelFactory),
                Template = new ControlTemplate(typeof(ListBox))
                {
                    VisualTree = new FrameworkElementFactory(typeof(ItemsPresenter))
                }
            };

            ScrollViewer.SetCanContentScroll(listBox, true);
            VirtualizingPanel.SetIsVirtualizing(listBox, true);
            VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);

            for (int i = 0; i < 100; i++)
            {
                listBox.Items.Add($"Spiel {i}");
            }

            return listBox;
        }

        private static void Layout(FrameworkElement element)
        {
            element.ApplyTemplate();
            element.Measure(new Size(element.Width, element.Height));
            element.Arrange(new Rect(0, 0, element.Width, element.Height));
            element.UpdateLayout();
        }

        private static void LayoutPanel(VirtualizingWrapPanel panel)
        {
            var size = new Size(200, 100);
            panel.Measure(size);
            panel.Arrange(new Rect(size));
            panel.UpdateLayout();
        }

        private static HashSet<ListBoxItem> GetRealizedContainers(VirtualizingWrapPanel panel)
        {
            var containers = new HashSet<ListBoxItem>();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
            {
                if (VisualTreeHelper.GetChild(panel, i) is ListBoxItem container)
                {
                    containers.Add(container);
                }
            }

            return containers;
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

        private static void RunInSta(Action action)
        {
            Exception? capturedException = null;
            using var finished = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    capturedException = ex;
                }
                finally
                {
                    finished.Set();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            finished.Wait();
            thread.Join();

            if (capturedException != null)
            {
                throw capturedException;
            }
        }
    }
}
