using System.Windows;
using System.Windows.Media;

namespace GameLauncher.Core
{
    internal static class VisualTreeExtensions
    {
        public static T? FindDescendant<T>(this DependencyObject parent) where T : DependencyObject
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                {
                    return match;
                }

                T? nestedMatch = child.FindDescendant<T>();
                if (nestedMatch != null)
                {
                    return nestedMatch;
                }
            }

            return null;
        }
    }
}
