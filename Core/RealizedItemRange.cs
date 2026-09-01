using System;
using System.Windows;
using System.Windows.Controls;

namespace GameLauncher.Core
{
    /// <summary>
    /// Der Indexbereich, den eine virtualisierte Liste gerade tatsächlich
    /// realisiert hat.
    ///
    /// Hintergrund: Zuvor wurde dieser Bereich über den eigenen
    /// <c>VirtualizingWrapPanel</c> ermittelt. Der wird seit der Umstellung auf
    /// das Zeilenmodell nirgends mehr eingehängt, weshalb die Abfrage immer
    /// fehlschlug - die Startup-Animation hielt daraufhin alle Einträge für
    /// realisiert und die Diagnose konnte gar nichts melden. Diese Auswertung
    /// arbeitet deshalb gegen den tatsächlich verwendeten
    /// <see cref="VirtualizingStackPanel"/>.
    /// </summary>
    internal readonly record struct RealizedItemRange(int FirstIndex, int LastIndexExclusive)
    {
        public int Count => Math.Max(0, LastIndexExclusive - FirstIndex);

        public bool IsEmpty => Count == 0;

        public static RealizedItemRange Empty => new(0, 0);

        /// <summary>
        /// Ermittelt den realisierten Bereich einer virtualisierten Liste. Liefert
        /// <see cref="Empty"/>, wenn noch kein Layout stattgefunden hat oder die
        /// Liste nicht virtualisiert ist.
        /// </summary>
        public static RealizedItemRange For(ItemsControl itemsControl)
        {
            ArgumentNullException.ThrowIfNull(itemsControl);

            if (itemsControl.FindDescendant<VirtualizingStackPanel>() is not VirtualizingStackPanel panel)
            {
                return Empty;
            }

            int firstIndex = int.MaxValue;
            int lastIndexExclusive = 0;

            foreach (UIElement child in panel.Children)
            {
                // Recycelte, aber gerade nicht zugeordnete Container liefern -1.
                int index = itemsControl.ItemContainerGenerator.IndexFromContainer(child);
                if (index < 0)
                {
                    continue;
                }

                firstIndex = Math.Min(firstIndex, index);
                lastIndexExclusive = Math.Max(lastIndexExclusive, index + 1);
            }

            return firstIndex == int.MaxValue
                ? Empty
                : new RealizedItemRange(firstIndex, lastIndexExclusive);
        }
    }
}
