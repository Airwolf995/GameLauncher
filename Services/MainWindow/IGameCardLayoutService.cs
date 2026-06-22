using System.Windows;
using System.Windows.Controls;
using GameLauncher.Models;

namespace GameLauncher.Services.MainWindow
{
    public readonly record struct CardRowLayoutResult(int Columns, double CardWidth, bool Changed);

    public interface IGameCardLayoutService
    {
        void ApplyCardSize(ListBox gameListControl, ResourceDictionary resources, CardSize size, bool refresh = true);

        ViewModeAnimationAction ApplyViewMode(
            ListBox gameListControl,
            ResourceDictionary resources,
            ViewMode mode,
            DataTemplate? originalCardTemplate,
            CardSize currentCardSize,
            bool refresh = true);

        CardRowLayoutResult ApplyCardRowLayout(
            ResourceDictionary resources,
            double actualWidth,
            CardSize currentCardSize,
            int currentColumns);
    }
}
