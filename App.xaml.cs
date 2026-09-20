using System;
using System.Threading;
using System.Windows;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private static bool _ownsMutex;

        /// <summary>
        /// Verhindert eine Lawine von Meldungsfenstern: Tritt derselbe Fehler bei
        /// jedem Bildaufbau erneut auf, waere sonst kein Klick mehr moeglich.
        /// </summary>
        private static bool _isReportingError;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "GameLauncher_CSharp_SingleInstance";
            bool createdNew;
            var localization = LocalizationService.Instance;

            // Beim Neustart nach dem Einspielen einer Sicherung wartet die neue
            // Sitzung auf das Ende der alten. Ohne dieses Warten liefe sie in die
            // Einzelinstanz-Sperre unten und beendete sich sofort wieder.
            WaitForPreviousInstance(e.Args);

            localization.ApplyLanguageCode(Services.ConfigService.GetStoredLanguageCode());

            _mutex = new Mutex(true, appName, out createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                // App is already running!
                MessageBox.Show(localization.Get("App.AlreadyRunning"), localization.Get("AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Initialize Logger
            Models.Logger.Initialize();

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Models.Logger.Error("Unhandled Application Crash", ex);
                MessageBox.Show(localization.Format("App.UnhandledError", args.ExceptionObject ?? "Unknown"), localization.Get("App.CrashTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Fehler im Oberflaechenstrang beendeten den Launcher bisher
            // kommentarlos: Der Haken oben meldet den Absturz nur, abwenden kann er
            // ihn nicht. Betroffen war vor allem alles, was aus einem
            // async-void-Ereignis heraus laeuft - dort landet eine Ausnahme
            // unmittelbar beim Dispatcher.
            DispatcherUnhandledException += (s, args) =>
            {
                Models.Logger.Error("Unbehandelter Fehler in der Oberflaeche", args.Exception);

                if (!IsRecoverable(args.Exception))
                {
                    return;
                }

                args.Handled = true;
                ReportRecoverableError(localization, args.Exception);
            };

            try
            {
                Models.Logger.Log("Starting application...");
                
                // Manually startup MainWindow to catch construction errors
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Startup failed", ex);
                MessageBox.Show(localization.Format("App.StartupError", ex.Message, ex.StackTrace ?? string.Empty), localization.Get("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// Entscheidet, ob der Launcher nach diesem Fehler weiterlaufen darf. Ein
        /// misslungener Klick soll ihn nicht beenden; bei erschoepftem Speicher
        /// oder verletztem Speicherschutz waere Weiterlaufen dagegen ein
        /// Blindflug - dort bleibt es beim Absturz samt Protokolleintrag.
        /// </summary>
        internal static bool IsRecoverable(Exception? exception)
        {
            return exception switch
            {
                null => false,
                OutOfMemoryException => false,
                AccessViolationException => false,
                _ => true
            };
        }

        /// <summary>
        /// Meldet den abgefangenen Fehler. Laeuft bewusst ueber die
        /// Systemmeldung statt ueber das eigene Meldungsfenster: Ist die
        /// Oberflaeche gerade in einem schlechten Zustand, soll die Meldung
        /// daran nicht auch noch scheitern.
        /// </summary>
        private static void ReportRecoverableError(LocalizationService localization, Exception exception)
        {
            if (_isReportingError)
            {
                return;
            }

            try
            {
                _isReportingError = true;
                MessageBox.Show(
                    localization.Format("App.RecoverableErrorBody", exception.Message),
                    localization.Get("App.RecoverableErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Fehlermeldung konnte nicht angezeigt werden", ex);
            }
            finally
            {
                _isReportingError = false;
            }
        }

        /// <summary>
        /// Wartet, bis die vorherige Sitzung beendet ist. Das Zeitlimit
        /// verhindert, dass ein haengengebliebener Vorgaenger den Start
        /// dauerhaft blockiert - laeuft es ab, greift danach die regulaere
        /// Einzelinstanz-Sperre mit ihrem Hinweis.
        /// </summary>
        private static void WaitForPreviousInstance(string[] arguments)
        {
            int? processId = Services.Settings.ApplicationRestartService.TryReadProcessIdToWaitFor(arguments);
            if (processId == null)
            {
                return;
            }

            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(processId.Value);
                previous.WaitForExit(PreviousInstanceWaitMs);
            }
            catch (ArgumentException)
            {
                // Der Vorgaenger ist bereits beendet.
            }
            catch (Exception ex)
            {
                Models.Logger.Error("Warten auf die vorherige Sitzung fehlgeschlagen", ex);
            }
        }

        private const int PreviousInstanceWaitMs = 15000;

        protected override void OnExit(ExitEventArgs e)
        {
            Models.Logger.Log("Application shutting down.");
            Models.Logger.Shutdown();

            if (_ownsMutex)
            {
                _mutex?.ReleaseMutex();
                _ownsMutex = false;
            }

            _mutex?.Dispose();
            _mutex = null;
            base.OnExit(e);
        }
    }
}
