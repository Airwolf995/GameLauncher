using GameLauncher;
using GameLauncher.Models;
using GameLauncher.Services.MainWindow;

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

        [Fact]
        public void ShutdownCoordinator_RequestExit_DisablesTrayMinimization()
        {
            var coordinator = new MainWindowShutdownCoordinator();
            var settings = new UISettings { MinimizeToTray = true };

            coordinator.RequestExit();

            Assert.False(coordinator.ShouldMinimizeToTray(settings));
        }

        [Fact]
        public async Task ShutdownCoordinator_Preparation_CanOnlyStartOnceAndCompletes()
        {
            var coordinator = new MainWindowShutdownCoordinator();

            Assert.True(coordinator.TryBeginPreparation());
            Assert.False(coordinator.TryBeginPreparation());

            await coordinator.PrepareAsync(null);

            Assert.True(coordinator.IsPrepared);
            Assert.False(coordinator.IsPreparing);
            Assert.False(coordinator.TryBeginPreparation());
        }
    }
}
