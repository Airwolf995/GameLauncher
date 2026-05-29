using System;
using System.Threading;
using System.Windows;

namespace GameLauncher
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "GameLauncher_CSharp_SingleInstance";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running!
                MessageBox.Show("Der Launcher läuft bereits!", "Game Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Initialize Logger
            Models.Logger.Initialize();

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Models.Logger.Error("Unhandled Application Crash", ex);
                MessageBox.Show($"Unhandled Error: {args.ExceptionObject}", "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Startup Error: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
