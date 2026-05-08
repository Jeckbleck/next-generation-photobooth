using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Serilog;

namespace Photobooth.Views
{
    public partial class GreetingPage : Page
    {

        // Lazily initialised — named elements aren't available until after InitializeComponent.
        private Button[]?           _navButtons;
        private FrameworkElement[]? _contentPanels;

        private int?  _selectedEventId;
        private bool  _loadingEvents;

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
            if (App.Settings.VerifyPin(PinBox.Password))
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
            LoadEvents();
            RefreshStoragePath();
            RefreshPrinterStatus();
            SelectTab(0);
        }

        // --- Event Management tab ------------------------------------------------

        private void LoadEvents()
        {
            _loadingEvents = true;
            try
            {
                EventsComboBox.Items.Clear();
                EventsComboBox.Items.Add(new ComboBoxItem { Content = "— New event —", Tag = null });
                foreach (var ev in App.Events.GetActive())
                    EventsComboBox.Items.Add(new ComboBoxItem { Content = ev.Name, Tag = ev.Id });
                EventsComboBox.SelectedIndex = 0;
            }
            finally
            {
                _loadingEvents = false;
            }

            // Restore the previously active event if it still exists
            var savedId = App.Settings.ActiveEventId;
            if (savedId.HasValue)
                SelectEventById(savedId.Value);
            else
            {
                _selectedEventId = null;
                ClearEventFields();
            }
        }

        private void EventsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingEvents) return;
            if (EventsComboBox.SelectedItem is not ComboBoxItem item) return;

            if (item.Tag is int id)
            {
                SetSelectedEvent(id);
                var ev = App.Events.GetById(id);
                if (ev is not null)
                {
                    PopulateEventFields(ev.Name, ev.PaywallEnabled, ev.SaveImagesEnabled, ev.PrintLimitPerSession);
                    RefreshSessionStats(id);
                }
            }
            else
            {
                SetSelectedEvent(null);
                ClearEventFields();
            }
        }

        /// <summary>
        /// Single point of truth for changing the selected event.
        /// Keeps _selectedEventId and the persisted setting in sync.
        /// </summary>
        private void SetSelectedEvent(int? id)
        {
            _selectedEventId = id;
            App.Settings.SetActiveEventId(id);
        }

        private void PopulateEventFields(string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimit)
        {
            EventNameBox.Text              = name;
            PaywallToggle.IsChecked        = paywallEnabled;
            SaveImagesToggle.IsChecked     = saveImagesEnabled;
            PrintLimitBox.Text             = printLimit?.ToString() ?? string.Empty;
            ArchiveEventButton.IsEnabled   = true;
        }

        private void ClearEventFields()
        {
            EventNameBox.Text              = string.Empty;
            PaywallToggle.IsChecked        = false;
            SaveImagesToggle.IsChecked     = true;
            PrintLimitBox.Text             = string.Empty;
            SessionCountText.Text          = "—";
            PhotoCountText.Text            = "—";
            ArchiveEventButton.IsEnabled   = false;
        }

        private void RefreshSessionStats(int eventId)
        {
            var (sessions, photos) = App.Events.GetStats(eventId);
            SessionCountText.Text = sessions.ToString();
            PhotoCountText.Text   = photos.ToString();
        }

        private void SaveEvent_Click(object sender, RoutedEventArgs e)
        {
            var name = EventNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            int? printLimit = int.TryParse(PrintLimitBox.Text.Trim(), out int parsed) && parsed > 0 ? parsed : null;

            int savedId;
            if (_selectedEventId.HasValue)
            {
                App.Events.UpdateDetails(_selectedEventId.Value, name,
                    PaywallToggle.IsChecked == true,
                    SaveImagesToggle.IsChecked == true,
                    printLimit);
                savedId = _selectedEventId.Value;
            }
            else
            {
                if (!App.Settings.IsStorageConfigured)
                {
                    StoragePathWarning.Visibility = Visibility.Visible;
                    MessageBox.Show(
                        "Please select a storage folder before creating an event.\n\nUse the Browse button in the Storage section above.",
                        "Storage path required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var ev = App.Events.Create(name,
                    PaywallToggle.IsChecked == true,
                    SaveImagesToggle.IsChecked == true,
                    printLimit);
                savedId = ev.Id;
            }

            LoadEvents();
            SelectEventById(savedId);
        }

        private void ClearEvent_Click(object sender, RoutedEventArgs e)
        {
            _loadingEvents = true;
            EventsComboBox.SelectedIndex = 0;
            _loadingEvents = false;
            SetSelectedEvent(null);
            ClearEventFields();
        }

        private void ClearSessionData_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;
            App.Events.ClearSessions(_selectedEventId.Value);
            RefreshSessionStats(_selectedEventId.Value);
        }

        private void ArchiveEvent_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;

            var ev = App.Events.GetById(_selectedEventId.Value);
            if (ev is null) return;

            var result = MessageBox.Show(
                $"Archive \"{ev.Name}\"?\n\nThe event will be hidden from the list. All sessions and photos on disk are kept and can be recovered by an administrator.",
                "Archive Event",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;

            App.Events.Archive(_selectedEventId.Value);
            SetSelectedEvent(null);
            LoadEvents();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;

            if (sender == PaywallToggle)
                App.Events.SetPaywall(_selectedEventId.Value, PaywallToggle.IsChecked == true);
            else if (sender == SaveImagesToggle)
                App.Events.SetSaveImages(_selectedEventId.Value, SaveImagesToggle.IsChecked == true);
        }

        private void PrintLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void PrintLimitBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;

            int? limit = int.TryParse(PrintLimitBox.Text.Trim(), out int parsed) && parsed > 0 ? parsed : null;
            App.Events.SetPrintLimit(_selectedEventId.Value, limit);
            PrintLimitBox.Text = limit?.ToString() ?? string.Empty;
        }

        private void SelectEventById(int id)
        {
            var ev = App.Events.GetById(id);
            if (ev is null) return; // event no longer exists — stay in new-event state

            _loadingEvents = true;
            try
            {
                foreach (ComboBoxItem item in EventsComboBox.Items)
                {
                    if (item.Tag is int itemId && itemId == id)
                    {
                        EventsComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _loadingEvents = false;
            }

            SetSelectedEvent(id);
            PopulateEventFields(ev.Name, ev.PaywallEnabled, ev.SaveImagesEnabled, ev.PrintLimitPerSession);
            RefreshSessionStats(id);
        }

        // --- Storage path --------------------------------------------------------

        private void RefreshStoragePath()
        {
            StoragePathBox.Text = App.Settings.StorageRoot;
            StoragePathWarning.Visibility = App.Settings.IsStorageConfigured
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BrowseStoragePath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title            = "Select storage root folder",
                InitialDirectory = App.Settings.StorageRoot,
            };

            if (dlg.ShowDialog() != true) return;

            App.Settings.SetStorageRoot(dlg.FolderName);
            StoragePathBox.Text = dlg.FolderName;
            StoragePathWarning.Visibility = Visibility.Collapsed;
            Log.Information("Storage root changed to {Path}", dlg.FolderName);
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
            AboutPrinterStatus.Text = "—";

            AboutLogPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "logs");

            AboutSessionsPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Photobooth");
        }

        // --- Printer tab ---------------------------------------------------------

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

        // --- Display tab: background image ---------------------------------------

        private void BrowseGreetingBg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select greeting background image",
                Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.tiff|All files|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var bmp = new BitmapImage(new Uri(dlg.FileName));
                GreetingBgImage.Source       = bmp;
                GreetingBgImage.Visibility   = Visibility.Visible;
                GreetingBgOverlay.Visibility = Visibility.Visible;
                BgPathBox.Text               = dlg.FileName;
                BgPreviewImage.Source        = bmp;
                BgPreviewBorder.Visibility   = Visibility.Visible;
                Log.Information("Greeting background set: {Path}", dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load background image");
            }
        }

        private void ClearGreetingBg_Click(object sender, RoutedEventArgs e)
        {
            GreetingBgImage.Source       = null;
            GreetingBgImage.Visibility   = Visibility.Collapsed;
            GreetingBgOverlay.Visibility = Visibility.Collapsed;
            BgPathBox.Text               = string.Empty;
            BgPreviewImage.Source        = null;
            BgPreviewBorder.Visibility   = Visibility.Collapsed;
            Log.Information("Greeting background cleared");
        }

        // --- Display tab: color pickers ------------------------------------------

        private void ApplyAccentColor_Click(object sender, RoutedEventArgs e)
            => ApplyBrushColor("AccentBrush", AccentHexBox.Text.Trim());

        private void ApplyBgColor_Click(object sender, RoutedEventArgs e)
            => ApplyBrushColor("BackgroundBrush", BgColorHexBox.Text.Trim());

        private void ApplySurfaceColor_Click(object sender, RoutedEventArgs e)
            => ApplyBrushColor("SurfaceBrush", SurfaceHexBox.Text.Trim());

        private static void ApplyBrushColor(string resourceKey, string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                Application.Current.Resources[resourceKey] = new SolidColorBrush(color);

                if (resourceKey == "AccentBrush")
                {
                    Application.Current.Resources["AccentHoverBrush"]   = new SolidColorBrush(Lighten(color, 0.12));
                    Application.Current.Resources["AccentPressedBrush"] = new SolidColorBrush(Darken(color, 0.22));
                }

                Log.Information("Applied {Key} = {Hex}", resourceKey, hex);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Invalid color value '{Hex}' for {Key}", hex, resourceKey);
            }
        }

        private static Color Lighten(Color c, double amount) => Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + (int)((255 - c.R) * amount)),
            (byte)Math.Min(255, c.G + (int)((255 - c.G) * amount)),
            (byte)Math.Min(255, c.B + (int)((255 - c.B) * amount)));

        private static Color Darken(Color c, double amount) => Color.FromArgb(c.A,
            (byte)(c.R * (1.0 - amount)),
            (byte)(c.G * (1.0 - amount)),
            (byte)(c.B * (1.0 - amount)));

        // --- Security: change PIN ------------------------------------------------

        private void ChangePIN_Click(object sender, RoutedEventArgs e)
        {
            var newPin     = NewPinBox.Password;
            var confirmPin = ConfirmPinBox.Password;

            if (newPin.Length < 4)
            {
                ShowPinStatus("PIN must be at least 4 digits.", isError: true);
                return;
            }
            if (newPin != confirmPin)
            {
                ShowPinStatus("PINs do not match.", isError: true);
                return;
            }

            App.Settings.SetPin(newPin);
            NewPinBox.Password     = string.Empty;
            ConfirmPinBox.Password = string.Empty;
            ShowPinStatus("PIN changed successfully.", isError: false);
        }

        private void ShowPinStatus(string message, bool isError)
        {
            PinChangeStatus.Text = message;
            PinChangeStatus.Foreground = isError
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
                : new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            PinChangeStatus.Visibility = Visibility.Visible;
        }

        // --- Close ---------------------------------------------------------------

        private void CloseSettingsContent_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings panel closed");
            SettingsContentPanel.Visibility = Visibility.Collapsed;
        }
    }
}
