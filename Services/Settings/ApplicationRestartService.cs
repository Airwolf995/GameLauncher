using System;
using System.Diagnostics;
using System.Globalization;
using GameLauncher.Models;

namespace GameLauncher.Services.Settings
{
    /// <summary>
    /// Startet die Anwendung neu.
    ///
    /// Gebraucht wird das nach dem Einspielen einer Sicherung: die eingespielte
    /// Konfiguration wird erst mit einem neuen Start wirksam, und bis dahin
    /// speichert die laufende Anwendung nichts mehr. Wer den Hinweis wegklickt
    /// und weiterspielt, verlöre die in der Zwischenzeit gesammelte Spielzeit.
    /// </summary>
    internal interface IApplicationRestartService
    {
        /// <summary>
        /// Startet eine neue Sitzung und beendet die laufende. Meldet, ob der
        /// Neustart angestoßen werden konnte.
        /// </summary>
        bool Restart();
    }

    internal sealed class ApplicationRestartService : IApplicationRestartService
    {
        /// <summary>
        /// Schalter, mit dem die neue Sitzung auf das Ende der alten wartet.
        /// Nötig wegen der Einzelinstanz-Sperre: ohne das Warten träfe die neue
        /// Sitzung die noch laufende alte an und beendete sich sofort wieder mit
        /// dem Hinweis, die Anwendung laufe bereits.
        /// </summary>
        internal const string WaitForProcessArgument = "--warte-auf-prozess";

        private readonly Action _exitApplication;

        public ApplicationRestartService(Action exitApplication)
        {
            _exitApplication = exitApplication ?? throw new ArgumentNullException(nameof(exitApplication));
        }

        public bool Restart()
        {
            string? executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Logger.Error(
                    "Neustart nicht möglich: der eigene Programmpfad ist nicht zu ermitteln",
                    new InvalidOperationException(nameof(Environment.ProcessPath)));
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = BuildWaitArguments(Environment.ProcessId),
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Neue Sitzung konnte nicht gestartet werden", ex);
                return false;
            }

            Logger.Log("Neustart angestoßen; die laufende Sitzung wird beendet.");
            _exitApplication();
            return true;
        }

        internal static string BuildWaitArguments(int processId) =>
            $"{WaitForProcessArgument} {processId.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Liest die Kennung des Prozesses, auf dessen Ende gewartet werden soll.
        /// Liefert null, wenn der Schalter fehlt oder keine Kennung folgt.
        /// </summary>
        internal static int? TryReadProcessIdToWaitFor(string[] arguments)
        {
            if (arguments == null)
            {
                return null;
            }

            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], WaitForProcessArgument, StringComparison.Ordinal))
                {
                    continue;
                }

                if (int.TryParse(
                        arguments[index + 1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int processId) &&
                    processId > 0)
                {
                    return processId;
                }

                return null;
            }

            return null;
        }
    }
}
