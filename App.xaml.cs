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
