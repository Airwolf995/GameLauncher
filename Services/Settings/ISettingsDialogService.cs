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
        /// Rückfrage vor dem Einspielen. Sie deckt den gesamten Vorgang ab: das
        /// Ersetzen des bisherigen Stands und den anschließenden Neustart. Beides
        /// getrennt zu fragen wäre keine echte Wahl - ohne Neustart bliebe die
        /// Anwendung in dem Zustand, in dem sie nichts mehr speichert.
        /// </summary>
        bool ConfirmImport();

        void ShowConfigTransferResult(string message, string title);
    }
}
