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
        public void ShutdownCoordinator_ShouldMinimizeToTray_RespectsExplicitExit(bool isExiting, bool minimizeToTray, bool expected)
        {
            var coordinator = new MainWindowShutdownCoordinator();
            var settings = new UISettings { MinimizeToTray = minimizeToTray };
            if (isExiting)
            {
                coordinator.RequestExit();
            }

            Assert.Equal(expected, coordinator.ShouldMinimizeToTray(settings));
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
