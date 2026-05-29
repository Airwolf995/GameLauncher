using System;
using System.Windows;
using GameLauncher.Models;
using GameLauncher.ViewModels;

namespace GameLauncher
{
    public partial class SettingsWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        public SettingsWindow(GameManager gameManager, Action<string> onThemeChanged, Action<UISettings> onSettingsChanged)
        {
            DataContext = new SettingsViewModel(gameManager, onThemeChanged, onSettingsChanged);
            InitializeComponent();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult != true && DataContext is SettingsViewModel viewModel)
            {
                viewModel.RevertPreview();
            }

            base.OnClosing(e);
        }
    }
}
