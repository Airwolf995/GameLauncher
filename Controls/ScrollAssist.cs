using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameLauncher.Controls
{
    /// <summary>
    /// Reicht das Mausrad an den umgebenden Bildlaufbereich weiter, sobald das
    /// Element selbst nichts mehr zu verschieben hat. Ohne das behandelt etwa eine
    /// TextBox mit eigenem Bildlauf das Ereignis immer selbst - auch wenn ihr Inhalt
    /// vollständig sichtbar ist -, und das Rad wirkt über ihr scheinbar gar nicht.
    /// </summary>
    public static class ScrollAssist
    {
        public static readonly DependencyProperty BubbleScrollProperty =
            DependencyProperty.RegisterAttached(
                "BubbleScroll",
                typeof(bool),
                typeof(ScrollAssist),
                new PropertyMetadata(false, OnBubbleScrollChanged));

        public static void SetBubbleScroll(DependencyObject element, bool value) =>
            element.SetValue(BubbleScrollProperty, value);

        public static bool GetBubbleScroll(DependencyObject element) =>
            (bool)element.GetValue(BubbleScrollProperty);

        private static void OnBubbleScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
            {
                return;
            }

            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            if (e.NewValue is true)
            {
                element.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not UIElement element)
            {
                return;
            }

            var inner = FindScrollViewer(element);
            if (inner != null && !IsAtEdge(inner, e.Delta))
            {
                return;
            }

            if (VisualTreeHelper.GetParent(element) is not UIElement parent)
            {
                return;
            }

            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = element
            });
        }

        /// <summary>
        /// Am Anschlag, wenn es entweder nichts zu verschieben gibt oder in der
        /// gedrehten Richtung bereits das Ende erreicht ist. Positives Delta scrollt
        /// nach oben.
        /// </summary>
        private static bool IsAtEdge(ScrollViewer scrollViewer, int delta)
        {
            if (scrollViewer.ScrollableHeight <= 0)
            {
                return true;
            }

            return delta > 0
                ? scrollViewer.VerticalOffset <= 0
                : scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }

                var nested = FindScrollViewer(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
