using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;

namespace Photobooth.Views
{
    public partial class GreetingPage : Page
    {
        // Change this PIN or load it from a config file later
        private const string StaffPin = "1234";

        public GreetingPage()
        {
            InitializeComponent();
            Log.Information("Navigated to GreetingPage");
        }

        private void StartSession_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("Session started by user");
            var window = Window.GetWindow(this) as MainWindow;
            window?.NavigateTo(new ShootPage());
        }

        private void GearButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings gear button tapped — showing PIN overlay");
            PinBox.Password = string.Empty;
            PinError.Visibility = Visibility.Collapsed;
            SettingsOverlay.Visibility = Visibility.Visible;
            PinBox.Focus();
        }

        private void CancelSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings overlay dismissed");
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void UnlockSettings_Click(object sender, RoutedEventArgs e)
            => TryUnlock();

        private void PinBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) TryUnlock();
        }

        private void TryUnlock()
        {
            if (PinBox.Password == StaffPin)
            {
                Log.Information("Settings unlocked successfully");
                SettingsOverlay.Visibility = Visibility.Collapsed;
                SettingsContentPanel.Visibility = Visibility.Visible;
            }
            else
            {
                Log.Warning("Incorrect PIN attempt");
                PinError.Visibility = Visibility.Visible;
                PinBox.Password = string.Empty;
            }
        }

        private void CloseSettingsContent_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings panel closed");
            SettingsContentPanel.Visibility = Visibility.Collapsed;
        }
    }
}
