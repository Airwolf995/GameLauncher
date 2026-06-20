using System;
using System.Threading;
using System.Windows;
using GameLauncher.Services.Localization;

namespace GameLauncher
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "GameLauncher_CSharp_SingleInstance";
            bool createdNew;
            var localization = LocalizationService.Instance;
            localization.ApplyLanguageCode(Services.ConfigService.GetStoredLanguageCode());

            _mutex = new Mutex(true, appName, out createdNew);

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

        protected override void OnExit(ExitEventArgs e)
        {
            Models.Logger.Log("Application shutting down.");
            Models.Logger.Shutdown();
            _mutex?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}
