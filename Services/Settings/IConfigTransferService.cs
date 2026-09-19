namespace GameLauncher.Services.Settings
{
    /// <summary>
    /// Sichern und Einspielen der Konfiguration. Als Schnittstelle geführt,
    /// damit die Ablaufsteuerung der Einstellungen ohne Dateizugriff prüfbar
    /// bleibt - so wie die übrigen Dienste dieses Ordners.
    /// </summary>
    internal interface IConfigTransferService
    {
        ConfigTransferResult Export(string targetPath);
        ConfigTransferResult Import(string sourcePath);
    }
}
