using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;

namespace Photobooth.Views
{
    public partial class GreetingPage : Page
    {
        private const string StaffPin = "1234";

        public GreetingPage()
        {
            InitializeComponent();
            Log.Information("Navigated to GreetingPage");
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            App.Camera.CameraDisconnected += OnCameraDisconnected;
            UpdateCameraStatus();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            App.Camera.CameraDisconnected -= OnCameraDisconnected;
        }

        private void UpdateCameraStatus()
        {
            bool connected = App.Camera.IsConnected;
            StartButton.IsEnabled          = connected;
            CameraStatusText.Visibility    = connected ? Visibility.Collapsed : Visibility.Visible;
            RetryButton.Visibility         = connected ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnCameraDisconnected(object? sender, System.EventArgs e)
        {
            Dispatcher.Invoke(UpdateCameraStatus);
        }

        private void RetryCamera_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User retrying camera connection");
            RetryButton.IsEnabled   = false;
            CameraStatusText.Text   = "Searching for camera…";

            bool ok = App.Camera.Initialize();
            Log.Information("Camera reconnect attempt: {Result}", ok ? "success" : "no camera found");

            RetryButton.IsEnabled = true;
            CameraStatusText.Text = ok ? string.Empty : "No camera connected";
            UpdateCameraStatus();
        }

        private void StartSession_Click(object sender, RoutedEventArgs e)
        {
            if (!App.Camera.IsConnected)
            {
                Log.Warning("Start tapped but camera not connected — blocked navigation");
                UpdateCameraStatus();
                return;
            }

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
                PopulateSettingsPanel();
                SettingsContentPanel.Visibility = Visibility.Visible;
            }
            else
            {
                Log.Warning("Incorrect PIN attempt");
                PinError.Visibility = Visibility.Visible;
                PinBox.Password = string.Empty;
            }
        }

        private void PopulateSettingsPanel()
        {
            PrinterNameBox.Text  = App.Settings.Current.PrinterName ?? string.Empty;
            BrandingTextBox.Text = App.Settings.Current.BrandingText;
            RefreshPrinterStatus();
        }

        private void RefreshPrinterStatus()
        {
            string manual = PrinterNameBox.Text.Trim();
            if (!string.IsNullOrEmpty(manual))
            {
                PrinterStatusText.Text = $"Using: {manual}";
                return;
            }
            string? auto = App.Printer.FindDnpPrinter();
            PrinterStatusText.Text = auto != null
                ? $"Auto-detected: {auto}"
                : "No DNP printer detected. Connect the printer or enter its name manually.";
        }

        private void AutoDetectPrinter_Click(object sender, RoutedEventArgs e)
        {
            string? found = App.Printer.FindDnpPrinter();
            if (found != null)
            {
                PrinterNameBox.Text = found;
                Log.Information("Auto-detected printer: {Name}", found);
            }
            else
            {
                Log.Warning("Auto-detect found no DNP printer");
            }
            RefreshPrinterStatus();
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string name = PrinterNameBox.Text.Trim();
            App.Settings.Current.PrinterName = string.IsNullOrEmpty(name) ? null : name;

            string branding = BrandingTextBox.Text.Trim();
            App.Settings.Current.BrandingText = string.IsNullOrEmpty(branding)
                ? "THE NEXT GENERATION PHOTOBOOTH"
                : branding;

            App.Settings.Save();
            Log.Information("Settings saved — printer={Printer}", App.Settings.Current.PrinterName ?? "(auto)");
            SettingsContentPanel.Visibility = Visibility.Collapsed;
        }

        private void CloseSettingsContent_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings panel closed without saving");
            SettingsContentPanel.Visibility = Visibility.Collapsed;
        }
    }
}
