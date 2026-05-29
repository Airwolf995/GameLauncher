namespace GameLauncher.Services.MainWindow
{
    public enum ViewModeAnimationAction
    {
        None,
        Animate,
        AnimateInstant
    }

    public sealed class ViewModeAnimationPolicy
    {
        public ViewModeAnimationAction GetAction(bool isCardMode, bool refresh, int visibleItemsCount)
        {
            if (!isCardMode)
            {
                return ViewModeAnimationAction.None;
            }

            if (refresh)
            {
                return ViewModeAnimationAction.Animate;
            }

            return visibleItemsCount > 0
                ? ViewModeAnimationAction.AnimateInstant
                : ViewModeAnimationAction.None;
        }
    }
}
