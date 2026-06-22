namespace GameLauncher.Services.Settings
{
    internal interface ISettingsDialogService
    {
        string? SelectBackgroundImage();
        bool ConfirmReset();
    }
}
