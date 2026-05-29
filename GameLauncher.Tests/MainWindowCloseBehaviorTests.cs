using GameLauncher;

namespace GameLauncher.Tests
{
    public class MainWindowCloseBehaviorTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        public void ShouldMinimizeToTrayOnClose_RespectsExplicitExit(bool isExiting, bool minimizeToTray, bool expected)
        {
            var result = MainWindow.ShouldMinimizeToTrayOnClose(isExiting, minimizeToTray);

            Assert.Equal(expected, result);
        }
    }
}
