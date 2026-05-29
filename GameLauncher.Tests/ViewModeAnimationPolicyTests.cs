using GameLauncher.Services.MainWindow;
using Xunit;

namespace GameLauncher.Tests
{
    public class ViewModeAnimationPolicyTests
    {
        [Fact]
        public void GetAction_ForListMode_ReturnsNone()
        {
            var policy = new ViewModeAnimationPolicy();
            var result = policy.GetAction(isCardMode: false, refresh: true, visibleItemsCount: 10);

            Assert.Equal(ViewModeAnimationAction.None, result);
        }

        [Fact]
        public void GetAction_ForCardModeWithRefresh_ReturnsAnimate()
        {
            var policy = new ViewModeAnimationPolicy();
            var result = policy.GetAction(isCardMode: true, refresh: true, visibleItemsCount: 10);

            Assert.Equal(ViewModeAnimationAction.Animate, result);
        }

        [Fact]
        public void GetAction_ForCardModeWithoutRefreshAndVisibleItems_ReturnsAnimateInstant()
        {
            var policy = new ViewModeAnimationPolicy();
            var result = policy.GetAction(isCardMode: true, refresh: false, visibleItemsCount: 3);

            Assert.Equal(ViewModeAnimationAction.AnimateInstant, result);
        }
    }
}
