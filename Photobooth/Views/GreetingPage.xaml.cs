using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;

namespace Photobooth.Views
{
    public partial class GreetingPage : Page
    {
        private const string StaffPin = "1234";

        // Lazily initialised — named elements aren't available until after InitializeComponent.
        private Button[]?          _navButtons;
        private FrameworkElement[]? _contentPanels;

        private Button[] NavButtons => _navButtons ??= new[]
            { NavEvents, NavCamera, NavStrip, NavPrinter, NavDisplay, NavAI, NavSync, NavAbout };

        private FrameworkElement[] ContentPanels => _contentPanels ??= new FrameworkElement[]
            { PanelEvents, PanelCamera, PanelStrip, PanelPrinter, PanelDisplay, PanelAI, PanelSync, PanelAbout };

        public GreetingPage()
        {
            InitializeComponent();
            Log.Information("Navigated to GreetingPage");
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // --- Lifecycle -----------------------------------------------------------

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
            StartButton.IsEnabled       = connected;
            CameraStatusText.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            RetryButton.Visibility      = connected ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnCameraDisconnected(object? sender, System.EventArgs e)
        {
            Dispatcher.Invoke(UpdateCameraStatus);
        }

        // --- Greeting actions ----------------------------------------------------

        private void RetryCamera_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User retrying camera connection");
            RetryButton.IsEnabled = false;
            CameraStatusText.Text = "Searching for camera…";

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

        // --- Settings overlay (PIN gate) -----------------------------------------

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

        private void UnlockSettings_Click(object sender, RoutedEventArgs e) => TryUnlock();

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
                OpenSettings();
                SettingsContentPanel.Visibility = Visibility.Visible;
            }
            else
            {
                Log.Warning("Incorrect PIN attempt");
                PinError.Visibility = Visibility.Visible;
                PinBox.Password = string.Empty;
            }
        }

        private void OpenSettings()
        {
            RefreshPrinterStatus();
            SelectTab(0);
        }

        // --- Tab navigation ------------------------------------------------------

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            int index = Array.IndexOf(NavButtons, (Button)sender);
            if (index >= 0) SelectTab(index);
        }

        private void SelectTab(int index)
        {
            for (int i = 0; i < NavButtons.Length; i++)
            {
                NavButtons[i].Style = (Style)FindResource(
                    i == index ? "SettingsNavButtonActive" : "SettingsNavButton");
                ContentPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }

            if (index == 7) PopulateAboutTab();
        }

        // --- About tab (read-only live data) -------------------------------------

        private void PopulateAboutTab()
        {
            AboutCameraStatus.Text = App.Camera.IsConnected ? "Connected" : "Not connected";
            AboutPrinterStatus.Text = "—";   // populated when printer branch is merged

            AboutLogPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "logs");

            AboutSessionsPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth");
        }

        // --- Printer tab (fully wired on merge with feature/printer-setup) -------

        private void RefreshPrinterStatus()
        {
            string manual = PrinterNameBox.Text.Trim();
            PrinterStatusText.Text = string.IsNullOrEmpty(manual)
                ? "Auto-detect will run on the printer branch."
                : $"Using: {manual}";
        }

        private void AutoDetectPrinter_Click(object sender, RoutedEventArgs e)
        {
            PrinterStatusText.Text = "Auto-detect available after merging feature/printer-setup.";
            Log.Debug("Auto-detect clicked on settings-menu branch — no-op");
        }

        // --- Close ---------------------------------------------------------------

        private void CloseSettingsContent_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings panel closed");
            SettingsContentPanel.Visibility = Visibility.Collapsed;
        }
    }
}
