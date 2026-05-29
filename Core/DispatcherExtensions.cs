using System;
using System.Windows;

namespace GameLauncher.Core
{
    public static class DispatcherExtensions
    {
        /// <summary>
        /// Executes the specified action on the UI thread.
        /// If the current thread is the UI thread, the action is executed immediately.
        /// </summary>
        public static void RunOnUI(this Action action)
        {
            if (Application.Current?.Dispatcher == null)
            {
                action();
                return;
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }
    }
}
