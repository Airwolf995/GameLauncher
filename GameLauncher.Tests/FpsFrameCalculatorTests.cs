using System;
using GameLauncher.Services.MainWindow;
using Xunit;

namespace GameLauncher.Tests
{
    public class FpsFrameCalculatorTests
    {
        [Fact]
        public void TryProcessFrame_IgnoresDuplicateRenderingTime()
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var calculator = new FpsFrameCalculator(start);

            Assert.False(calculator.TryProcessFrame(TimeSpan.FromMilliseconds(16), start.AddMilliseconds(16), out _));
            Assert.False(calculator.TryProcessFrame(TimeSpan.FromMilliseconds(16), start.AddMilliseconds(32), out _));
            Assert.False(calculator.TryProcessFrame(TimeSpan.FromMilliseconds(48), start.AddMilliseconds(500), out _));
            Assert.True(calculator.TryProcessFrame(TimeSpan.FromMilliseconds(64), start.AddSeconds(1.1), out var fps));

            Assert.True(fps > 0);
        }

        [Fact]
        public void TryProcessFrame_ResetsAfterFpsEmission()
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var calculator = new FpsFrameCalculator(start);

            calculator.TryProcessFrame(TimeSpan.FromMilliseconds(16), start.AddMilliseconds(16), out _);
            calculator.TryProcessFrame(TimeSpan.FromMilliseconds(32), start.AddMilliseconds(32), out _);
            Assert.True(calculator.TryProcessFrame(TimeSpan.FromMilliseconds(48), start.AddSeconds(1.2), out var fps));
            Assert.True(fps > 0);

            Assert.False(calculator.TryProcessFrame(TimeSpan.FromMilliseconds(64), start.AddSeconds(1.3), out _));
        }
    }
}
