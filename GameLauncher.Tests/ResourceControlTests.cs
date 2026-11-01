using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GameLauncher.Tests;

public sealed class ResourceControlTests
{
    [Fact]
    public void ScrollViewerScrolltInBeidenRichtungenMitPassenderGeometrie()
    {
        InWindow(window =>
        {
            var viewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Width = 1200, Height = 900 }
            };
            window.Content = viewer;
            window.Show();
            window.UpdateLayout();

            var horizontal = Descendants<ScrollBar>(viewer).Single(bar => bar.Orientation == Orientation.Horizontal);
            var vertical = Descendants<ScrollBar>(viewer).Single(bar => bar.Orientation == Orientation.Vertical);
            Assert.True(horizontal.ActualWidth > horizontal.ActualHeight * 5);
            Assert.True(vertical.ActualHeight > vertical.ActualWidth * 5);

            Page(horizontal, increase: true);
            Page(vertical, increase: true);
            Pump();
            Assert.True(viewer.HorizontalOffset > 0);
            Assert.True(viewer.VerticalOffset > 0);

            Page(horizontal, increase: false);
            Page(vertical, increase: false);
            Pump();
            Assert.Equal(0, viewer.HorizontalOffset);
            Assert.Equal(0, viewer.VerticalOffset);

            viewer.ScrollToRightEnd();
            viewer.ScrollToBottom();
            Pump();
            foreach (var bar in new[] { horizontal, vertical })
            {
                var track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
                Point position = track.Thumb.TranslatePoint(new Point(), track);
                Assert.True(bar.Orientation == Orientation.Horizontal ? position.X > 0 : position.Y > 0);
                Assert.True(track.Thumb.ActualWidth > 0 && track.Thumb.ActualHeight > 0);
            }
        });
    }

    private static void Page(ScrollBar bar, bool increase)
    {
        var track = Assert.IsType<Track>(bar.Template.FindName("PART_Track", bar));
        RepeatButton button = increase ? track.IncreaseRepeatButton : track.DecreaseRepeatButton;
        var command = Assert.IsType<RoutedCommand>(button.Command);
        command.Execute(null, button);
    }

    [Theory]
    [InlineData(true, 18)]
    [InlineData(false, 0)]
    public void ToggleStartetAmZielUndKannLaufendeUebergaengeUmkehren(bool initial, double initialX)
    {
        InWindow(window =>
        {
            var toggle = new CheckBox { IsChecked = initial, Content = "Test" };
            window.Content = toggle;
            window.Show();
            window.UpdateLayout();
            var thumb = Assert.IsType<Ellipse>(toggle.Template.FindName("Ellipse", toggle));
            var transform = Assert.IsType<TranslateTransform>(thumb.RenderTransform);
            Assert.Equal(initialX, transform.X, 3);

            toggle.IsChecked = !initial;
            Pump(70);
            double before = Assert.IsType<TranslateTransform>(thumb.RenderTransform).X;
            Assert.InRange(before, 0, 18);
            toggle.IsChecked = initial;
            Assert.InRange(Math.Abs(Assert.IsType<TranslateTransform>(thumb.RenderTransform).X - before), 0, 0.1);
            Pump(250);
            Assert.Equal(initialX, Assert.IsType<TranslateTransform>(thumb.RenderTransform).X, 3);

            toggle.IsChecked = null;
            Pump(250);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(thumb.RenderTransform).X, 3);
        });
    }

    [Fact]
    public void ComboBoxOeffnetDropdownUndUebernimmtAuswahl()
    {
        InWindow(window =>
        {
            var combo = new ComboBox { ItemsSource = new[] { "Eins", "Zwei", "Drei" }, SelectedIndex = 0 };
            window.Content = combo;
            window.Show();
            window.UpdateLayout();
            combo.IsDropDownOpen = true;
            Pump();
            var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
            Assert.True(popup.IsOpen);
            Assert.NotNull(combo.ItemContainerGenerator.ContainerFromIndex(2));
            combo.SelectedIndex = 2;
            combo.IsDropDownOpen = false;
            Assert.Equal("Drei", combo.SelectedItem);
            Assert.False(popup.IsOpen);
            combo.IsEnabled = false;
            var root = Assert.IsType<Grid>(combo.Template.FindName("ComboRoot", combo));
            Assert.True(root.Opacity < 1);
        });
    }

    private static void InWindow(Action<Window> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                window = new Window { Width = 320, Height = 220, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                foreach (string name in new[] { "Colors", "Buttons", "Controls" })
                {
                    window.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/GameLauncher;component/Resources/Styles/{name}.xaml", UriKind.Relative)
                    });
                }
                test(window);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF-Prüfung antwortete nicht rechtzeitig.");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void Pump(int milliseconds = 30)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
