using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Photobooth.Camera;
using Photobooth.Helpers;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views
{
    public partial class GreetingPage : Page
    {
        private readonly CameraService       _camera;
        private readonly IEventService       _events;
        private readonly SettingsManager     _settings;
        private readonly IFileStorageService _fileStorage;
        private readonly AIEnhancementClient _aiClient;
        private readonly FlowController      _flow;

        // Lazily initialised — named elements aren't available until after InitializeComponent.
        private Button[]?           _navButtons;
        private FrameworkElement[]? _contentPanels;

        private const string DefaultAccent     = "#E94560";
        private const string DefaultBackground = "#1A1A2E";
        private const string DefaultSurface    = "#16213E";

        private int?  _selectedEventId;
        private bool  _loadingEvents;
        private int   _previewVersion;

        private Button[] NavButtons => _navButtons ??= new[]
            { NavEvents, NavCamera, NavStrip, NavPrinter, NavDisplay, NavAI, NavSync, NavAbout };

        private FrameworkElement[] ContentPanels => _contentPanels ??= new FrameworkElement[]
            { PanelEvents, PanelCamera, PanelStrip, PanelPrinter, PanelDisplay, PanelAI, PanelSync, PanelAbout };

        public GreetingPage(
            CameraService       camera,
            IEventService       events,
            SettingsManager     settings,
            IFileStorageService fileStorage,
            AIEnhancementClient aiClient,
            FlowController      flow)
        {
            _camera      = camera;
            _events      = events;
            _settings    = settings;
            _fileStorage = fileStorage;
            _aiClient    = aiClient;
            _flow        = flow;
            InitializeComponent();
            _loadingDisplaySliders = false;

            Log.Information("Navigated to GreetingPage");
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // --- Lifecycle -----------------------------------------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _camera.CameraDisconnected += OnCameraDisconnected;
            if (Window.GetWindow(this) is Window w)
                w.PreviewKeyDown += OnWindowKeyDown;
            UpdateCameraStatus();
            ApplyActiveEventAppearance();
            UpdateAIEnhancementButton();
        }

        private void ApplyActiveEventAppearance()
        {
            var id = _settings.ActiveEventId;
            if (!id.HasValue) return;
            var ev = _events.GetById(id.Value);
            if (ev is null) return;

            if (!string.IsNullOrEmpty(ev.AccentColor))     ApplyBrushColor("AccentBrush",     ev.AccentColor);
            if (!string.IsNullOrEmpty(ev.BackgroundColor)) ApplyBrushColor("BackgroundBrush", ev.BackgroundColor);
            if (!string.IsNullOrEmpty(ev.SurfaceColor))    ApplyBrushColor("SurfaceBrush",    ev.SurfaceColor);

            if (!string.IsNullOrEmpty(ev.BackgroundImagePath) && File.Exists(ev.BackgroundImagePath))
            {
                try
                {
                    var bmp = new BitmapImage(new Uri(ev.BackgroundImagePath));
                    GreetingBgImage.Source       = bmp;
                    GreetingBgImage.Visibility   = Visibility.Visible;
                    GreetingBgOverlay.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to restore background image on load");
                }
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _camera.CameraDisconnected    -= OnCameraDisconnected;
            _camera.CameraPropertyChanged -= OnCameraPropertyChanged;
            if (Window.GetWindow(this) is Window w)
                w.PreviewKeyDown -= OnWindowKeyDown;
            StopInlinePreview();
        }

        private void UpdateCameraStatus()
        {
            bool connected = _camera.IsConnected;
            bool hasEvent  = _settings.ActiveEventId.HasValue;

            bool paywallActive = false;
            if (hasEvent)
            {
                var ev = _events.GetById(_settings.ActiveEventId!.Value);
                paywallActive = ev?.PaywallEnabled == true;
            }

            if (paywallActive)
            {
                StartButton.Visibility  = Visibility.Collapsed;
                SubtitleText.Visibility = Visibility.Collapsed;
                PaywallText.Visibility  = Visibility.Visible;
            }
            else
            {
                StartButton.Visibility  = Visibility.Visible;
                SubtitleText.Visibility = Visibility.Visible;
                PaywallText.Visibility  = Visibility.Collapsed;
                StartButton.IsEnabled   = connected && hasEvent;
            }

            CameraStatusText.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            RetryButton.Visibility      = connected ? Visibility.Collapsed : Visibility.Visible;
            NoEventText.Visibility      = hasEvent  ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateAIEnhancementButton()
        {
            AIEnhancementButton.Visibility = _settings.AIEnhancementEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnCameraDisconnected(object? sender, System.EventArgs e)
        {
            Dispatcher.Invoke(UpdateCameraStatus);
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F13) return;
            if (SettingsOverlay.Visibility      == Visibility.Visible ||
                SettingsContentPanel.Visibility == Visibility.Visible) return;
            if (!_settings.ActiveEventId.HasValue) return;

            var ev = _events.GetById(_settings.ActiveEventId.Value);
            if (ev?.PaywallEnabled != true) return;

            if (!_camera.IsConnected)
            {
                Log.Warning("Payment signal (F13) received but camera not connected — session blocked");
                return;
            }

            Log.Information("Payment signal (F13) received — starting session");
            _flow.Trigger(FlowTrigger.StartNormal);
        }

        // --- Greeting actions ----------------------------------------------------

        private void RetryCamera_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User retrying camera connection");
            RetryButton.IsEnabled = false;
            CameraStatusText.Text = "Searching for camera…";

            bool ok = _camera.Initialize();
            Log.Information("Camera reconnect attempt: {Result}", ok ? "success" : "no camera found");

            RetryButton.IsEnabled = true;
            CameraStatusText.Text = ok ? string.Empty : "No camera connected";
            UpdateCameraStatus();
        }

        private void StartSession_Click(object sender, RoutedEventArgs e)
        {
            if (!_camera.IsConnected)
            {
                Log.Warning("Start tapped but camera not connected — blocked navigation");
                UpdateCameraStatus();
                return;
            }

            if (!_settings.ActiveEventId.HasValue)
            {
                Log.Warning("Start tapped but no active event selected — blocked navigation");
                UpdateCameraStatus();
                return;
            }

            Log.Information("Session started by user");
            _flow.Trigger(FlowTrigger.StartNormal);
        }

        private void AIEnhancement_Click(object sender, RoutedEventArgs e)
        {
            if (!_camera.IsConnected)
            {
                UpdateCameraStatus();
                return;
            }

            if (!_settings.ActiveEventId.HasValue)
            {
                UpdateCameraStatus();
                return;
            }

            Log.Information("AI Enhancement flow started by user");
            _flow.Trigger(FlowTrigger.StartAI);
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
            if (_settings.VerifyPin(PinBox.Password))
            {
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
            PopulatePrinterDropdown();
            AutoPrintToggle.IsChecked = _settings.AutoPrint;
            AIEnableToggle.IsChecked  = _settings.AIEnhancementEnabled;
            AIServerUrlBox.Text       = _settings.AIServerUrl;
            AIApiKeyBox.Text          = _settings.AIApiKey;
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
                foreach (var ev in _events.GetActive())
                    EventsComboBox.Items.Add(new ComboBoxItem { Content = ev.Name, Tag = ev.Id });
                EventsComboBox.SelectedIndex = 0;
            }
            finally
            {
                _loadingEvents = false;
            }

            var savedId = _settings.ActiveEventId;
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
                var ev = _events.GetById(id);
                if (ev is not null)
                {
                    PopulateEventFields(ev.Name, ev.PaywallEnabled, ev.SaveImagesEnabled, ev.PrintLimitPerEvent, ev.PrintLimitPerSession);
                    RefreshSessionStats(id);
                    LoadEventAppearance(ev);
                }
            }
            else
            {
                SetSelectedEvent(null);
                ClearEventFields();
            }
        }

        private void SetSelectedEvent(int? id)
        {
            _selectedEventId = id;
            _settings.SetActiveEventId(id);
        }

        private void PopulateEventFields(string name, bool paywallEnabled, bool saveImagesEnabled, int? printLimitPerEvent, int? printLimitPerSession)
        {
            EventNameBox.Text              = name;
            PaywallToggle.IsChecked        = paywallEnabled;
            SaveImagesToggle.IsChecked     = saveImagesEnabled;
            PrintLimitBox.Text             = printLimitPerEvent?.ToString()   ?? string.Empty;
            SessionLimitBox.Text           = printLimitPerSession?.ToString() ?? string.Empty;
            ArchiveEventButton.IsEnabled   = true;
        }

        private void ClearEventFields()
        {
            EventNameBox.Text               = string.Empty;
            PaywallToggle.IsChecked         = false;
            SaveImagesToggle.IsChecked      = true;
            PrintLimitBox.Text              = string.Empty;
            SessionLimitBox.Text            = string.Empty;
            SessionCountText.Text           = "—";
            PhotoCountText.Text             = "—";
            PrintCountText.Text             = "—";
            AICountText.Text                = "—";
            SessionPreviewList.ItemsSource  = null;
            ArchiveEventButton.IsEnabled    = false;
        }

        private void RefreshSessionStats(int eventId)
        {
            var (sessions, photos, prints, ai) = _events.GetStats(eventId);
            SessionCountText.Text = sessions.ToString();
            PhotoCountText.Text   = photos.ToString();
            PrintCountText.Text   = prints.ToString();
            AICountText.Text      = ai.ToString();
            _ = RefreshSessionPreviewAsync(eventId);
        }

        private sealed class SessionPreviewItem
        {
            public int    SessionId  { get; init; }
            public string Date       { get; init; } = "";
            public List<System.Windows.Media.ImageSource> Thumbnails { get; init; } = new();
        }

        private async Task RefreshSessionPreviewAsync(int eventId)
        {
            var version = ++_previewVersion;
            SessionPreviewList.ItemsSource = null;

            var items = await Task.Run(() =>
            {
                var sessions = _events.GetSessionsWithPhotos(eventId);

                return sessions.Select(s =>
                {
                    var thumbs = s.Photos
                        .Where(p => p.FilePath != null && File.Exists(p.FilePath))
                        .OrderBy(p => p.Sequence)
                        .Select(p =>
                        {
                            try
                            {
                                return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(p.FilePath!, fallbackDecodeWidth: 80);
                            }
                            catch { return null; }
                        })
                        .Where(b => b != null)
                        .Cast<System.Windows.Media.ImageSource>()
                        .ToList();

                    return new SessionPreviewItem
                    {
                        SessionId  = s.Id,
                        Date       = s.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
                        Thumbnails = thumbs,
                    };
                }).ToList();
            });

            if (_previewVersion != version) return;
            SessionPreviewList.ItemsSource = items;
        }

        private void SessionPreviewCard_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;
            if (((Button)sender).DataContext is not SessionPreviewItem item) return;
            OpenSessionBrowserAt(item.SessionId);
        }

        private void OpenSessionBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;
            OpenSessionBrowserAt(null);
        }

        private void OpenSessionBrowserAt(int? sessionId)
        {
            var browser = new SessionBrowserWindow(
                _selectedEventId!.Value, _events, _aiClient, _fileStorage, sessionId)
            {
                Owner = Window.GetWindow(this)
            };
            browser.ShowDialog();
            RefreshSessionStats(_selectedEventId.Value);
        }

        private void SaveEvent_Click(object sender, RoutedEventArgs e)
        {
            var name = EventNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            int? printLimitPerEvent   = int.TryParse(PrintLimitBox.Text.Trim(),   out int pe) && pe > 0 ? pe : null;
            int? printLimitPerSession = int.TryParse(SessionLimitBox.Text.Trim(), out int ps) && ps > 0 ? ps : null;

            int savedId;
            if (_selectedEventId.HasValue)
            {
                _events.UpdateDetails(_selectedEventId.Value, name,
                    PaywallToggle.IsChecked == true,
                    SaveImagesToggle.IsChecked == true,
                    printLimitPerEvent,
                    printLimitPerSession);
                savedId = _selectedEventId.Value;
            }
            else
            {
                if (!_settings.IsStorageConfigured)
                {
                    StoragePathWarning.Visibility = Visibility.Visible;
                    MessageBox.Show(
                        "Please select a storage folder before creating an event.\n\nUse the Browse button in the Storage section above.",
                        "Storage path required",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var ev = _events.Create(name,
                    PaywallToggle.IsChecked == true,
                    SaveImagesToggle.IsChecked == true,
                    printLimitPerEvent,
                    printLimitPerSession);
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
            _events.ClearSessions(_selectedEventId.Value);
            RefreshSessionStats(_selectedEventId.Value);
        }

        private void ArchiveEvent_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;

            var ev = _events.GetById(_selectedEventId.Value);
            if (ev is null) return;

            var result = MessageBox.Show(
                $"Archive \"{ev.Name}\"?\n\nThe event will be hidden from the list. All sessions and photos on disk are kept and can be recovered by an administrator.",
                "Archive Event",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;

            _events.Archive(_selectedEventId.Value);
            SetSelectedEvent(null);
            LoadEvents();
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;

            if (sender == PaywallToggle)
                _events.SetPaywall(_selectedEventId.Value, PaywallToggle.IsChecked == true);
            else if (sender == SaveImagesToggle)
                _events.SetSaveImages(_selectedEventId.Value, SaveImagesToggle.IsChecked == true);
        }

        private void PrintLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void PrintLimitBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;
            int? limit = int.TryParse(PrintLimitBox.Text.Trim(), out int parsed) && parsed > 0 ? parsed : null;
            _events.SetEventPrintLimit(_selectedEventId.Value, limit);
            PrintLimitBox.Text = limit?.ToString() ?? string.Empty;
        }

        private void SessionLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void SessionLimitBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_selectedEventId.HasValue) return;
            int? limit = int.TryParse(SessionLimitBox.Text.Trim(), out int parsed) && parsed > 0 ? parsed : null;
            _events.SetSessionPrintLimit(_selectedEventId.Value, limit);
            SessionLimitBox.Text = limit?.ToString() ?? string.Empty;
        }

        private void SelectEventById(int id)
        {
            var ev = _events.GetById(id);
            if (ev is null) return;

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
            PopulateEventFields(ev.Name, ev.PaywallEnabled, ev.SaveImagesEnabled, ev.PrintLimitPerEvent, ev.PrintLimitPerSession);
            RefreshSessionStats(id);
            LoadEventAppearance(ev);
        }

        // --- Storage path --------------------------------------------------------

        private void RefreshStoragePath()
        {
            StoragePathBox.Text = _settings.StorageRoot;
            StoragePathWarning.Visibility = _settings.IsStorageConfigured
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BrowseStoragePath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title            = "Select storage root folder",
                InitialDirectory = _settings.StorageRoot,
            };

            if (dlg.ShowDialog() != true) return;

            _settings.SetStorageRoot(dlg.FolderName);
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
            if (ContentPanels[1].Visibility == Visibility.Visible && index != 1)
            {
                _camera.CameraPropertyChanged -= OnCameraPropertyChanged;
                StopInlinePreview();
                CameraPreviewPanel.Visibility = Visibility.Collapsed;
            }

            for (int i = 0; i < NavButtons.Length; i++)
            {
                NavButtons[i].Style = (Style)FindResource(
                    i == index ? "SettingsNavButtonActive" : "SettingsNavButton");
                ContentPanels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }

            if (index == 1)
            {
                LoadCameraSettings();
                CameraPreviewPanel.Visibility = Visibility.Visible;
                InlinePreviewImage.Source = null;
                InlinePreviewStatusText.Text      = "Starting camera preview…";
                InlinePreviewStatusText.Visibility = Visibility.Visible;
                StartInlinePreview();
            }

            if (index == 2)
            {
                StripDesignerPanel.Visibility = Visibility.Visible;
                LoadStripDesigner();
            }
            else
            {
                StripDesignerPanel.Visibility = Visibility.Collapsed;
            }

            if (index == 4) LoadDisplaySliders();
            if (index == 5) _ = TestAIConnectionAsync();
            if (index == 7) PopulateAboutTab();
        }

        // --- Camera settings tab -------------------------------------------------

        private bool _settingCameraControls;

        private void LoadCameraSettings()
        {
            if (!_camera.IsConnected)
            {
                CameraModelLabel.Text        = "No camera connected";
                CameraSettingStatusText.Text = "Connect a camera and retry.";
                IsoComboBox.IsEnabled = TvComboBox.IsEnabled = AvComboBox.IsEnabled =
                    WhiteBalanceComboBox.IsEnabled = ImageQualityComboBox.IsEnabled = false;
                return;
            }

            CameraModelLabel.Text        = _camera.ModelName ?? "Camera";
            CameraSettingStatusText.Text = "Loading valid values from camera…";
            IsoComboBox.IsEnabled = TvComboBox.IsEnabled = AvComboBox.IsEnabled =
                WhiteBalanceComboBox.IsEnabled = ImageQualityComboBox.IsEnabled = true;

            _camera.CameraPropertyChanged -= OnCameraPropertyChanged;
            _camera.CameraPropertyChanged += OnCameraPropertyChanged;

            RefreshCameraDropdown(IsoComboBox,          EDSDKLib.EDSDK.PropID_ISOSpeed,     CameraPropertyMaps.Iso,          CameraPropertyMaps.LookupIso);
            RefreshCameraDropdown(TvComboBox,           EDSDKLib.EDSDK.PropID_Tv,           CameraPropertyMaps.Tv,           CameraPropertyMaps.LookupTv);
            RefreshCameraDropdown(AvComboBox,           EDSDKLib.EDSDK.PropID_Av,           CameraPropertyMaps.Av,           CameraPropertyMaps.LookupAv);
            RefreshCameraDropdown(WhiteBalanceComboBox, EDSDKLib.EDSDK.PropID_WhiteBalance, CameraPropertyMaps.WhiteBalance, CameraPropertyMaps.LookupWb);
            RefreshCameraDropdown(ImageQualityComboBox, EDSDKLib.EDSDK.PropID_ImageQuality, CameraPropertyMaps.ImageQuality, CameraPropertyMaps.LookupIq);

            _camera.RequestPropertyDescs();
        }

        private void RefreshCameraDropdown(ComboBox cb, uint propId,
            Dictionary<uint, string> map, Func<uint, string> fallback)
        {
            _settingCameraControls = true;
            try
            {
                uint currentValue = _camera.GetPropertyValue(propId) ?? 0xFFFFFFFF;
                int[]? desc       = _camera.GetPropertyDesc(propId);

                cb.Items.Clear();

                IEnumerable<uint> values = (desc != null && desc.Length > 0)
                    ? desc.Select(v => (uint)v)
                    : map.Keys;

                foreach (uint v in values)
                {
                    string label = map.TryGetValue(v, out var s) ? s : fallback(v);
                    cb.Items.Add(new ComboBoxItem { Content = label, Tag = v });
                }

                foreach (ComboBoxItem item in cb.Items)
                {
                    if (item.Tag is uint v && v == currentValue)
                    {
                        cb.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _settingCameraControls = false;
            }
        }

        private void OnCameraPropertyChanged(object? sender, uint propId)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (PanelCamera.Visibility != Visibility.Visible) return;

                switch (propId)
                {
                    case EDSDKLib.EDSDK.PropID_ISOSpeed:
                        RefreshCameraDropdown(IsoComboBox, propId, CameraPropertyMaps.Iso, CameraPropertyMaps.LookupIso);
                        break;
                    case EDSDKLib.EDSDK.PropID_Tv:
                        RefreshCameraDropdown(TvComboBox, propId, CameraPropertyMaps.Tv, CameraPropertyMaps.LookupTv);
                        break;
                    case EDSDKLib.EDSDK.PropID_Av:
                        RefreshCameraDropdown(AvComboBox, propId, CameraPropertyMaps.Av, CameraPropertyMaps.LookupAv);
                        break;
                    case EDSDKLib.EDSDK.PropID_WhiteBalance:
                        RefreshCameraDropdown(WhiteBalanceComboBox, propId, CameraPropertyMaps.WhiteBalance, CameraPropertyMaps.LookupWb);
                        break;
                    case EDSDKLib.EDSDK.PropID_ImageQuality:
                        RefreshCameraDropdown(ImageQualityComboBox, propId, CameraPropertyMaps.ImageQuality, CameraPropertyMaps.LookupIq);
                        break;
                }

                CameraSettingStatusText.Text = string.Empty;
            });
        }

        private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingCameraControls) return;
            if (sender is not ComboBox cb) return;
            if (cb.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not uint value) return;

            uint propId = cb == IsoComboBox          ? EDSDKLib.EDSDK.PropID_ISOSpeed
                        : cb == TvComboBox            ? EDSDKLib.EDSDK.PropID_Tv
                        : cb == AvComboBox            ? EDSDKLib.EDSDK.PropID_Av
                        : cb == WhiteBalanceComboBox  ? EDSDKLib.EDSDK.PropID_WhiteBalance
                        : cb == ImageQualityComboBox  ? EDSDKLib.EDSDK.PropID_ImageQuality
                        : 0u;

            if (propId == 0) return;
            Log.Information("Camera property 0x{PropId:X8} → 0x{Value:X8}", propId, value);
            _camera.SetProperty(propId, value);
        }

        // --- Inline live preview -------------------------------------------------

        private bool _inlineEvfRunning;
        private bool _inlineEvfFramePending;
        private System.Threading.Timer? _inlineEvfWatchdog;

        private void StartInlinePreview()
        {
            if (!_camera.IsConnected) return;
            _inlineEvfRunning = true;
            _camera.EvfFrameReady += OnInlineEvfFrame;
            _camera.StartLiveView();
            RequestInlineEvfFrame();

            _inlineEvfWatchdog = new System.Threading.Timer(_ =>
            {
                if (!_inlineEvfRunning) return;
                _inlineEvfFramePending = false;
                RequestInlineEvfFrame();
            }, null, 200, 100);
        }

        private void StopInlinePreview()
        {
            _inlineEvfRunning = false;
            _inlineEvfWatchdog?.Dispose();
            _inlineEvfWatchdog = null;
            _camera.EvfFrameReady -= OnInlineEvfFrame;
            _camera.StopLiveView();
        }

        private void RequestInlineEvfFrame()
        {
            if (!_inlineEvfRunning || _inlineEvfFramePending) return;
            _inlineEvfFramePending = true;
            _camera.RequestEvfFrame();
        }

        private void OnInlineEvfFrame(object? sender, System.Windows.Media.Imaging.BitmapSource frame)
        {
            _inlineEvfFramePending = false;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
            {
                InlinePreviewImage.Source = frame;
                InlinePreviewStatusText.Visibility = Visibility.Collapsed;
                InlinePreviewInfoText.Text = _camera.ModelName ?? string.Empty;
            });
            RequestInlineEvfFrame();
        }

        private async void TakeTestShot_Click(object sender, RoutedEventArgs e)
        {
            if (!_camera.IsConnected) return;

            TakeTestShotButton.IsEnabled = false;
            ResumePreviewButton.Visibility = Visibility.Collapsed;

            _inlineEvfRunning = false;
            _inlineEvfWatchdog?.Dispose();
            _inlineEvfWatchdog = null;
            _camera.EvfFrameReady -= OnInlineEvfFrame;
            _camera.StopLiveView();

            InlinePreviewStatusText.Text = "Capturing…";
            InlinePreviewStatusText.Visibility = Visibility.Visible;
            InlinePreviewInfoText.Text = string.Empty;

            try
            {
                string path = await _camera.TakePictureAsync();

                var bitmap = BitmapHelper.LoadFromFile(path);

                InlinePreviewImage.Source          = bitmap;
                InlinePreviewStatusText.Visibility = Visibility.Collapsed;
                InlinePreviewInfoText.Text         = $"Saved: {System.IO.Path.GetFileName(path)}";
                ResumePreviewButton.Visibility     = Visibility.Visible;

                Log.Information("Test shot saved: {Path}", path);
            }
            catch (OperationCanceledException)
            {
                InlinePreviewStatusText.Text   = "Capture timed out.";
                ResumePreviewButton.Visibility = Visibility.Visible;
                Log.Warning("Test shot timed out");
            }
            catch (Exception ex)
            {
                InlinePreviewStatusText.Text   = $"Error: {ex.Message}";
                ResumePreviewButton.Visibility = Visibility.Visible;
                Log.Error(ex, "Test shot failed");
            }
            finally
            {
                TakeTestShotButton.IsEnabled = true;
            }
        }

        private void ResumePreview_Click(object sender, RoutedEventArgs e)
        {
            ResumePreviewButton.Visibility     = Visibility.Collapsed;
            InlinePreviewImage.Source          = null;
            InlinePreviewStatusText.Text       = "Starting camera preview…";
            InlinePreviewStatusText.Visibility = Visibility.Visible;
            InlinePreviewInfoText.Text         = string.Empty;
            StartInlinePreview();
        }

        // --- Strip designer tab --------------------------------------------------

        private void LoadStripDesigner()
        {
            if (_selectedEventId.HasValue)
            {
                var ev = _events.GetById(_selectedEventId.Value);
                if (ev is not null)
                {
                    StripDesigner.LoadForEvent(
                        ev.Id,
                        ev.Slug,
                        _fileStorage.GetStripTemplatePath(ev.Slug),
                        ev.PhotostripTemplatePath);
                    return;
                }
            }
            StripDesigner.LoadForEvent(null, null, null, null);
        }

        // --- About tab (read-only live data) -------------------------------------

        private void PopulateAboutTab()
        {
            if (_camera.IsConnected)
            {
                AboutCameraModel.Text  = _camera.ModelName ?? "Canon camera";
                AboutCameraStatus.Text = "Connected";
            }
            else
            {
                AboutCameraModel.Text  = "Not connected";
                AboutCameraStatus.Text = "Supports Canon EOS cameras via EDSDK";
            }

            var printerName = _settings.PrinterName;
            if (!string.IsNullOrEmpty(printerName))
            {
                AboutPrinterModel.Text  = printerName;
                AboutPrinterStatus.Text = "Selected";
            }
            else
            {
                AboutPrinterModel.Text  = "Not selected";
                AboutPrinterStatus.Text = "Supports DNP DS620A · any Windows printer";
            }

            AboutLogPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Photobooth", "Logs");

            AboutSessionsPath.Text = string.IsNullOrWhiteSpace(_settings.StorageRoot)
                ? "(not configured)"
                : _settings.StorageRoot;
        }

        // --- Printer tab ---------------------------------------------------------

        private void PopulatePrinterDropdown()
        {
            PrinterDropdown.IsEnabled        = false;
            PrinterStatusText.Text           = "Loading printers…";
            PrinterDropdown.SelectionChanged -= PrinterDropdown_SelectionChanged;
            PrinterDropdown.Items.Clear();
            PrinterDropdown.Items.Add("(none)");

            string? saved = _settings.PrinterName;

            _ = Task.Run(() =>
            {
                var printers = new List<string>();
                foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    printers.Add(name);
                return printers;
            }).ContinueWith(t =>
            {
                int selectIndex = 0;
                foreach (var name in t.Result)
                {
                    PrinterDropdown.Items.Add(name);
                    if (name == saved)
                        selectIndex = PrinterDropdown.Items.Count - 1;
                }

                PrinterDropdown.SelectedIndex = selectIndex;
                PrinterStatusText.Text = selectIndex == 0
                    ? "No printer selected."
                    : $"Active: {PrinterDropdown.SelectedItem}";

                PrinterDropdown.IsEnabled = true;
                PrinterDropdown.SelectionChanged += PrinterDropdown_SelectionChanged;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void AutoPrintToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.SetAutoPrint(AutoPrintToggle.IsChecked == true);
        }

        private void AIEnableToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.SetAIEnhancementEnabled(AIEnableToggle.IsChecked == true);
            UpdateAIEnhancementButton();
        }

        private void AIServerUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            var url = AIServerUrlBox.Text.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                _settings.SetAIServerUrl(url);
                _ = TestAIConnectionAsync();
            }
        }

        private void TestAIConnection_Click(object sender, RoutedEventArgs e) => _ = TestAIConnectionAsync();

        private async Task TestAIConnectionAsync()
        {
            AIStatusDot.Fill        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x77));
            AIStatusText.Text       = "Testing…";
            AIStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x77));

            try
            {
                var styles = await _aiClient.GetStylesAsync();
                AIStatusDot.Fill        = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                AIStatusText.Text       = $"Connected — {styles.Count} style{(styles.Count == 1 ? "" : "s")} available";
                AIStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                Log.Information("AI connection test OK — {Count} style(s)", styles.Count);
            }
            catch (Exception ex)
            {
                AIStatusDot.Fill        = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                AIStatusText.Text       = $"Could not connect: {ex.Message}";
                AIStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                Log.Warning(ex, "AI connection test failed");
            }
        }

        private void AIApiKey_LostFocus(object sender, RoutedEventArgs e)
        {
            _settings.SetAIApiKey(AIApiKeyBox.Text.Trim());
        }

        private void PrinterDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrinterDropdown.SelectedIndex <= 0)
            {
                _settings.SetPrinterName(null);
                PrinterStatusText.Text = "No printer selected.";
                Log.Information("Printer selection cleared");
            }
            else
            {
                string name = (string)PrinterDropdown.SelectedItem;
                _settings.SetPrinterName(name);
                PrinterStatusText.Text = $"Active: {name}";
                Log.Information("Printer selected: {Name}", name);
            }
        }

        // --- Display tab: sliders ------------------------------------------------

        private bool _loadingDisplaySliders = true; // true until after InitializeComponent so XAML default values don't overwrite saved settings

        private void LoadDisplaySliders()
        {
            _loadingDisplaySliders = true;
            CountdownSlider.Value   = _settings.CountdownSeconds;
            PreviewHoldSlider.Value = _settings.PreviewHoldSeconds;
            _loadingDisplaySliders = false;
        }

        private void CountdownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingDisplaySliders) return;
            _settings.SetCountdownSeconds((int)CountdownSlider.Value);
        }

        private void PreviewHoldSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loadingDisplaySliders) return;
            _settings.SetPreviewHoldSeconds((int)PreviewHoldSlider.Value);
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

                if (_selectedEventId.HasValue)
                    _events.SetBackgroundImagePath(_selectedEventId.Value, dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load background image");
            }
        }

        private void ClearGreetingBg_Click(object sender, RoutedEventArgs e)
        {
            ClearBackground();
            if (_selectedEventId.HasValue)
                _events.SetBackgroundImagePath(_selectedEventId.Value, null);
        }

        private void ClearBackground()
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

        private void LoadEventAppearance(Data.Models.Event ev)
        {
            var accent = ev.AccentColor     ?? DefaultAccent;
            var bg     = ev.BackgroundColor ?? DefaultBackground;
            var surf   = ev.SurfaceColor    ?? DefaultSurface;

            AccentHexBox.Text  = accent;
            BgColorHexBox.Text = bg;
            SurfaceHexBox.Text = surf;

            ApplyBrushColor("AccentBrush",     accent);
            ApplyBrushColor("BackgroundBrush", bg);
            ApplyBrushColor("SurfaceBrush",    surf);

            if (!string.IsNullOrEmpty(ev.BackgroundImagePath) && File.Exists(ev.BackgroundImagePath))
            {
                try
                {
                    var bmp = new BitmapImage(new Uri(ev.BackgroundImagePath));
                    GreetingBgImage.Source       = bmp;
                    GreetingBgImage.Visibility   = Visibility.Visible;
                    GreetingBgOverlay.Visibility = Visibility.Visible;
                    BgPathBox.Text               = ev.BackgroundImagePath;
                    BgPreviewImage.Source        = bmp;
                    BgPreviewBorder.Visibility   = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to restore background image for event {Id}", ev.Id);
                    ClearBackground();
                }
            }
            else
            {
                ClearBackground();
            }
        }

        private void ApplyAccentColor_Click(object sender, RoutedEventArgs e)
        {
            var hex = AccentHexBox.Text.Trim();
            ApplyBrushColor("AccentBrush", hex);
            if (_selectedEventId.HasValue)
                _events.SetAccentColor(_selectedEventId.Value, hex);
        }

        private void ApplyBgColor_Click(object sender, RoutedEventArgs e)
        {
            var hex = BgColorHexBox.Text.Trim();
            ApplyBrushColor("BackgroundBrush", hex);
            if (_selectedEventId.HasValue)
                _events.SetBackgroundColor(_selectedEventId.Value, hex);
        }

        private void ApplySurfaceColor_Click(object sender, RoutedEventArgs e)
        {
            var hex = SurfaceHexBox.Text.Trim();
            ApplyBrushColor("SurfaceBrush", hex);
            if (_selectedEventId.HasValue)
                _events.SetSurfaceColor(_selectedEventId.Value, hex);
        }

        private void RevertAppearance_Click(object sender, RoutedEventArgs e)
        {
            AccentHexBox.Text  = DefaultAccent;
            BgColorHexBox.Text = DefaultBackground;
            SurfaceHexBox.Text = DefaultSurface;

            ApplyBrushColor("AccentBrush",     DefaultAccent);
            ApplyBrushColor("BackgroundBrush", DefaultBackground);
            ApplyBrushColor("SurfaceBrush",    DefaultSurface);

            ClearBackground();

            if (_selectedEventId.HasValue)
            {
                _events.SetAccentColor(_selectedEventId.Value, null);
                _events.SetBackgroundColor(_selectedEventId.Value, null);
                _events.SetSurfaceColor(_selectedEventId.Value, null);
                _events.SetBackgroundImagePath(_selectedEventId.Value, null);
            }

            Log.Information("Appearance reverted to defaults");
        }

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

            _settings.SetPin(newPin);
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
            UpdateCameraStatus();
        }

        private void CloseApplication_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Close the booth application?",
                "Close Application",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;

            Log.Information("Application closed by operator via About tab");
            Application.Current.Shutdown();
        }
    }
}
