using System.Reflection;
using GameLauncher.Models;
using GameLauncher.Services;

namespace GameLauncher.Tests
{
    public class HotkeyServiceTests
    {
        [Fact]
        public void TryBuildHotkey_ReturnsExpectedValues_ForConfiguredOverlayHotkey()
        {
            var settings = new UISettings
            {
                OverlayHotkeyCtrl = true,
                OverlayHotkeyAlt = true,
                OverlayHotkeyShift = false,
                OverlayHotkeyWin = false,
                OverlayHotkeyKey = "F2"
            };

            var method = typeof(HotkeyService).GetMethod("TryBuildHotkey", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var args = new object?[] { settings, null, null, null };
            var success = Assert.IsType<bool>(method!.Invoke(null, args));

            Assert.True(success);
            Assert.Equal((uint)0x0003, Assert.IsType<uint>(args[1]));
            Assert.Equal((uint)0x71, Assert.IsType<uint>(args[2]));
            Assert.Equal("Strg+Alt+F2", Assert.IsType<string>(args[3]));
        }

        [Fact]
        public void TryBuildHotkey_ReturnsFalse_WithoutModifier()
        {
            var settings = new UISettings
            {
                OverlayHotkeyCtrl = false,
                OverlayHotkeyAlt = false,
                OverlayHotkeyShift = false,
                OverlayHotkeyWin = false,
                OverlayHotkeyKey = "G"
            };

            var method = typeof(HotkeyService).GetMethod("TryBuildHotkey", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var args = new object?[] { settings, null, null, null };
            var success = Assert.IsType<bool>(method!.Invoke(null, args));

            Assert.False(success);
        }
    }
}
