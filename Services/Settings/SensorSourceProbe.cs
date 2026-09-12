namespace GameLauncher.Services.Settings
{
    /// <summary>
    /// Fragt die LibreHardwareMonitor-Anwendung ueber ihren Webserver ab.
    /// </summary>
    internal sealed class SensorSourceProbe : ISensorSourceProbe
    {
        public bool IsAvailable() => LibreHardwareMonitorWebSource.IsApplicationAvailable();
    }
}
