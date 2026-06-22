namespace GameLauncher.Services.Settings
{
    internal interface IAutostartService
    {
        bool IsEnabled(bool fallbackValue);
        void SetEnabled(bool enabled);
    }
}
