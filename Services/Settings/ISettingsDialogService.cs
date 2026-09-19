namespace GameLauncher.Services.Settings
{
    internal interface ISettingsDialogService
    {
        string? SelectBackgroundImage();
        bool ConfirmReset();

        /// <summary>Zieldatei für die Sicherung, oder null bei Abbruch.</summary>
        string? SelectConfigExportTarget(string suggestedFileName);

        /// <summary>Einzuspielende Sicherung, oder null bei Abbruch.</summary>
        string? SelectConfigImportSource();

        /// <summary>
        /// Rückfrage vor dem Import. Er ersetzt den gesamten bisherigen Stand,
        /// deshalb wird er ausdrücklich bestätigt.
        /// </summary>
        bool ConfirmImport();

        void ShowConfigTransferResult(string message, string title);

        /// <summary>
        /// Rückfrage nach dem Einspielen: Die Sicherung wird erst mit einem
        /// neuen Start wirksam, und bis dahin speichert die laufende Anwendung
        /// nichts mehr.
        /// </summary>
        bool ConfirmRestartAfterImport();
    }
}
