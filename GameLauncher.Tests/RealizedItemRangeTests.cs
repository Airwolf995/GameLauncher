using System.Threading;
using System.Windows;
using System.Windows.Controls;
using GameLauncher.Core;

namespace GameLauncher.Tests
{
    /// <summary>
    /// Ersetzt die frueheren VirtualizingWrapPanelTests. Der eigene
    /// VirtualizingWrapPanel wurde seit der Umstellung auf das Zeilenmodell
    /// nirgends mehr eingehaengt; getestet wird jetzt die Auswertung gegen den
    /// tatsaechlich verwendeten VirtualizingStackPanel.
    /// </summary>
    public class RealizedItemRangeTests
    {
        /// <summary>
        /// Die zentrale Zusage: bei 500 Eintraegen darf nur ein kleiner Ausschnitt
        /// realisiert sein. Genau diese Zahl liess sich vorher nicht ermitteln.
        /// </summary>
        [Fact]
        public void For_ReportsOnlyTheRealizedSliceOfALongList()
        {
            RunInSta(() =>
            {
                var listBox = CreateVirtualizedListBox(itemCount: 500);
                Layout(listBox);

                RealizedItemRange range = RealizedItemRange.For(listBox);

                Assert.False(range.IsEmpty);
                Assert.Equal(0, range.FirstIndex);
                Assert.True(
                    range.Count < 20,
                    $"Es wurden {range.Count} von 500 Eintraegen realisiert - die Virtualisierung greift nicht.");
            });
        }

        // Nicht abgedeckt: dass der Bereich beim Scrollen mitwandert. Ein
        // VirtualizingStackPanel laeuft nur mit angebundenem ScrollOwner im
        // Scroll-Modus, und der entsteht in einem losgeloesten Harness ohne
        // laufende Application nicht - weder ueber ScrollViewer.ScrollToVerticalOffset
        // noch ueber SetVerticalOffset oder ScrollIntoView bewegt sich etwas.
        // Bewusst ausgelassen statt als gruener Test ohne Aussage.

        [Fact]
        public void For_ReturnsEmptyBeforeAnyLayoutHappened()
        {
            RunInSta(() =>
            {
                var listBox = CreateVirtualizedListBox(itemCount: 10);

                Assert.True(RealizedItemRange.For(listBox).IsEmpty);
            });
        }

        [Fact]
        public void For_ReturnsEmptyForAnEmptyList()
        {
            RunInSta(() =>
            {
                var listBox = CreateVirtualizedListBox(itemCount: 0);
                Layout(listBox);

                Assert.True(RealizedItemRange.For(listBox).IsEmpty);
            });
        }

        private static ListBox CreateVirtualizedListBox(int itemCount)
        {
            // Eigenes Template: ausserhalb einer laufenden Application wird das
            // Standard-Template der ListBox nicht angewandt, dann entsteht gar
            // kein Panel.
            var listBox = new ListBox
            {
                Width = 200,
                Height = 100,
                Template = new ControlTemplate(typeof(ListBox))
                {
                    VisualTree = BuildScrollingTemplate()
                }
            };

            ScrollViewer.SetCanContentScroll(listBox, true);
            VirtualizingPanel.SetIsVirtualizing(listBox, true);
            VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Recycling);

            for (int index = 0; index < itemCount; index++)
            {
                listBox.Items.Add($"Spiel {index}");
            }

            return listBox;
        }

        private static FrameworkElementFactory BuildScrollingTemplate()
        {
            var scrollViewerFactory = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewerFactory.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollViewerFactory.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            return scrollViewerFactory;
        }

        private static void Layout(FrameworkElement element)
        {
            element.ApplyTemplate();
            element.Measure(new Size(element.Width, element.Height));
            element.Arrange(new Rect(0, 0, element.Width, element.Height));
            element.UpdateLayout();
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
