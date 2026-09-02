using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace GameLauncher
{
    public partial class LicenseInfoWindow : Window
    {
        public LicenseInfoWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Services.DarkModeHelper.EnableDarkTitleBar(this);
        }

        /// <summary>
        /// Das Fenster hat keinen Abbrechen-Knopf, an dem IsCancel hängen könnte.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                return;
            }

            base.OnKeyDown(e);
        }

        private void OpenLicense_Click(object sender, RoutedEventArgs e)
        {
            var licensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE");
            if (!File.Exists(licensePath))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = licensePath,
                UseShellExecute = true
            });
        }
    }
}
