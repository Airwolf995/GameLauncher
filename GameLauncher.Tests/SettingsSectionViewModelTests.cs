using GameLauncher.Models;
using GameLauncher.Services.Localization;
using GameLauncher.ViewModels.Settings;

namespace GameLauncher.Tests
{
    public class SettingsSectionViewModelTests
    {
        [Fact]
        public void BehaviorSettings_MakesGameStartActionsMutuallyExclusive()
        {
            var viewModel = new BehaviorSettingsViewModel(LocalizationService.Instance)
            {
                MinimizeOnGameStart = true
            };

            viewModel.CloseOnGameStart = true;

            Assert.True(viewModel.CloseOnGameStart);
            Assert.False(viewModel.MinimizeOnGameStart);
        }

        [Fact]
        public void BehaviorSettings_AlwaysKeepsAHotkeyModifier()
        {
            var viewModel = new BehaviorSettingsViewModel(LocalizationService.Instance);
            viewModel.Load(
                new UISettings
                {
                    OverlayHotkeyCtrl = true,
                    OverlayHotkeyAlt = false,
                    OverlayHotkeyShift = false,
                    OverlayHotkeyWin = false
                },
                autostartEnabled: false);

            viewModel.OverlayHotkeyCtrl = false;

            Assert.True(viewModel.OverlayHotkeyAlt);
        }

        [Fact]
        public void AppearanceSettings_LoadDoesNotTriggerPreview()
        {
            int previewCount = 0;
            var viewModel = new AppearanceSettingsViewModel(
                () => previewCount++,
                _ => { });

            viewModel.Load(new GameConfig());

            Assert.Equal(0, previewCount);

            viewModel.FontScale = 1.2;

            Assert.Equal(1, previewCount);
        }
    }
}
