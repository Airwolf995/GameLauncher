using System;
using System.Windows;

namespace GameLauncher
{
    public partial class ModernMessageWindow : Window
    {
        public enum ModernMessageButton
        {
            OK,
            YesNo
        }

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public ModernMessageWindow(string message, string title, ModernMessageButton buttons = ModernMessageButton.OK)
        {
            InitializeComponent();
            
            TitleText.Text = title;
            MessageText.Text = message;

            SetupButtons(buttons);

            SourceInitialized += (_, _) => Services.DarkModeHelper.EnableDarkTitleBar(this);
        }

        private void SetupButtons(ModernMessageButton buttons)
        {
            BtnOk.Visibility = Visibility.Collapsed;
            BtnYes.Visibility = Visibility.Collapsed;
            BtnNo.Visibility = Visibility.Collapsed;

            switch (buttons)
            {
                case ModernMessageButton.OK:
                    BtnOk.Visibility = Visibility.Visible;
                    break;
                case ModernMessageButton.YesNo:
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            Close();
        }

        public static MessageBoxResult Show(string message, string title, ModernMessageButton buttons = ModernMessageButton.OK, Window? owner = null)
        {
            var window = new ModernMessageWindow(message, title, buttons);
            if (owner != null)
            {
                window.Owner = owner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            window.ShowDialog();
            return window.Result;
        }
    }
}
