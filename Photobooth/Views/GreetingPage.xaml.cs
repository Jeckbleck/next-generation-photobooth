using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Photobooth.Camera;
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

        private SettingsTabController _tabController = null!;
        private bool _tapsEnabled;

        private EventManagementPanel _eventPanel = null!;
        private AppearancePanel      _appearancePanel = null!;
        private DisplayPanel         _displayPanel    = null!;
        private CameraSettingsPanel  _cameraPanel     = null!;
        private AIConfigPanel        _aiPanel         = null!;
        private PrinterPanel         _printerPanel    = null!;
        private SecurityPanel        _securityPanel   = null!;
        private EventStatsPanel      _statsPanel      = null!;
        private AboutPanel           _aboutPanel      = null!;

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
            _eventPanel = new EventManagementPanel(_events, _settings, _fileStorage, _aiClient);
            EventPanelHost.Content = _eventPanel;
            _eventPanel.ActiveEventChanged += OnActiveEventChanged;
            _appearancePanel = new AppearancePanel(_events, _settings);
            _displayPanel = new DisplayPanel(_settings, _appearancePanel, _events);
            DisplayPanelHost.Content = _displayPanel;
            _displayPanel.BackgroundImageChanged += OnBackgroundImageChanged;
            _displayPanel.GreetingTextSaved      += OnGreetingTextSaved;
            _cameraPanel = new CameraSettingsPanel(_camera);
            CameraSettingsPanelHost.Content = _cameraPanel;
            _aiPanel = new AIConfigPanel(_aiClient, _settings);
            AIConfigPanelHost.Content = _aiPanel;
            _aiPanel.AIEnhancementEnabledChanged += OnAIEnhancementEnabledChanged;
            _printerPanel = new PrinterPanel(_settings);
            PrinterPanelHost.Content = _printerPanel;
            _securityPanel = new SecurityPanel(_settings);
            SecurityPanelHost.Content = _securityPanel;
            _statsPanel = new EventStatsPanel(_events);
            StatsPanelHost.Content = _statsPanel;
            _aboutPanel = new AboutPanel(_camera, _settings);
            AboutPanelHost.Content = _aboutPanel;
            _tabController = new SettingsTabController(
                new[] { NavEvents, NavStats, NavCamera, NavStrip, NavPrinter, NavDisplay, NavAI, NavSync, NavAbout },
                new FrameworkElement[] { PanelEvents, PanelStats, PanelCamera, PanelStrip, PanelPrinter, PanelDisplay, PanelAI, PanelSync, PanelAbout },
                this);
            _tabController.TabChanged += OnTabChanged;
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
            _appearancePanel.ApplyActiveEventAppearance();
            UpdateAIEnhancementButton();
            ApplyGreetingText();

            _tapsEnabled = false;
            var cooldown = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            cooldown.Tick += (_, _) => { _tapsEnabled = true; cooldown.Stop(); };
            cooldown.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _camera.CameraDisconnected -= OnCameraDisconnected;
            _eventPanel.ActiveEventChanged -= OnActiveEventChanged;
            _displayPanel.BackgroundImageChanged -= OnBackgroundImageChanged;
            _displayPanel.GreetingTextSaved      -= OnGreetingTextSaved;
            _aiPanel.AIEnhancementEnabledChanged -= OnAIEnhancementEnabledChanged;
            if (Window.GetWindow(this) is Window w)
                w.PreviewKeyDown -= OnWindowKeyDown;
            _cameraPanel.Deactivate();
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
                SubtitleText.Visibility = Visibility.Collapsed;
                PaywallText.Visibility  = Visibility.Visible;
            }
            else
            {
                SubtitleText.Visibility = Visibility.Visible;
                PaywallText.Visibility  = Visibility.Collapsed;
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

        private void OnActiveEventChanged(object? sender, Data.Models.Event? ev)
        {
            _appearancePanel.SelectedEventId = ev?.Id;
            if (ev is not null) _appearancePanel.LoadEventAppearance(ev);
            UpdateCameraStatus();
            if (ev is not null) _statsPanel.Refresh(ev.Id);
            else                _statsPanel.Clear();
            _displayPanel.SetActiveEvent(ev);
            ApplyGreetingText(ev);
        }

        private void ApplyGreetingText(Data.Models.Event? ev = null)
        {
            if (ev is null && _settings.ActiveEventId.HasValue)
                ev = _events.GetById(_settings.ActiveEventId.Value);

            EyebrowText.Text  = ev?.GreetingEyebrow  ?? "THE NEXT GENERATION";
            TitleText.Text    = ev?.GreetingTitle    ?? "PHOTOBOOTH";
            SubtitleText.Text = ev?.GreetingSubtitle ?? "Tap anywhere to start your session";
        }

        private void OnBackgroundImageChanged(object? sender, BitmapImage? bmp)
        {
            GreetingBgImage.Source       = bmp;
            GreetingBgImage.Visibility   = bmp is null ? Visibility.Collapsed : Visibility.Visible;
            GreetingBgOverlay.Visibility = bmp is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnAIEnhancementEnabledChanged(object? sender, bool enabled)
            => UpdateAIEnhancementButton();

        private void OnGreetingTextSaved(object? sender, EventArgs e)
            => ApplyGreetingText();

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

        private void Screen_Tapped(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_tapsEnabled) return;
            if (SettingsOverlay.Visibility      == Visibility.Visible ||
                SettingsContentPanel.Visibility == Visibility.Visible) return;

            bool connected = _camera.IsConnected;
            bool hasEvent  = _settings.ActiveEventId.HasValue;

            if (!connected || !hasEvent)
            {
                UpdateCameraStatus();
                return;
            }

            var ev = _events.GetById(_settings.ActiveEventId!.Value);
            if (ev?.PaywallEnabled == true) return;

            Log.Information("Session started by screen tap");
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

        private string _pinEntry = "";

        private void GearButton_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings gear button tapped — showing PIN overlay");
            _pinEntry = "";
            PinError.Visibility = Visibility.Collapsed;
            UpdatePinDots();
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void CancelSettings_Click(object sender, RoutedEventArgs e)
        {
            Log.Debug("Settings overlay dismissed");
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void NumpadKey_Click(object sender, RoutedEventArgs e)
        {
            var tag = ((Button)sender).Tag?.ToString();
            switch (tag)
            {
                case "back":
                    if (_pinEntry.Length > 0)
                        _pinEntry = _pinEntry[..^1];
                    PinError.Visibility = Visibility.Collapsed;
                    UpdatePinDots();
                    break;
                default:
                    if (_pinEntry.Length < 6 && tag is not null)
                    {
                        _pinEntry += tag;
                        PinError.Visibility = Visibility.Collapsed;
                        UpdatePinDots();
                        TryUnlock(showError: false);
                    }
                    break;
            }
        }

        private void UpdatePinDots()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _pinEntry.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append('●');
            }
            PinDotsDisplay.Text = sb.ToString();
        }

        private void TryUnlock(bool showError = true)
        {
            if (_settings.VerifyPin(_pinEntry))
            {
                SettingsOverlay.Visibility = Visibility.Collapsed;
                OpenSettings();
                SettingsContentPanel.Visibility = Visibility.Visible;
            }
            else if (showError)
            {
                Log.Warning("Incorrect PIN attempt");
                PinError.Visibility = Visibility.Visible;
                _pinEntry = "";
                UpdatePinDots();
            }
        }

        private void OpenSettings()
        {
            _eventPanel.Refresh();
            var activeEv = _settings.ActiveEventId.HasValue
                ? _events.GetById(_settings.ActiveEventId.Value)
                : null;
            _displayPanel.SetActiveEvent(activeEv);
            SelectTab(0);
        }

        // --- Tab navigation ------------------------------------------------------

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            var navButtons = new[] { NavEvents, NavStats, NavCamera, NavStrip,
                                     NavPrinter, NavDisplay, NavAI, NavSync, NavAbout };
            int index = Array.IndexOf(navButtons, (Button)sender);
            if (index >= 0) _tabController.SelectTab(index);
        }

        private void SelectTab(int index) => _tabController.SelectTab(index);

        private void OnTabChanged(int previous, int next)
        {
            if (previous == 2 && next != 2) _cameraPanel.Deactivate();

            switch (next)
            {
                case 1: RefreshStatsTab();        break;
                case 2: _cameraPanel.Activate();  break;
                case 4: _printerPanel.Activate(); break;
                case 5: _displayPanel.Activate(); break;
                case 6: _aiPanel.Activate();      break;
                case 8: _aboutPanel.Activate();   break;
            }

            if (next == 3)
            {
                StripDesignerPanel.Visibility = Visibility.Visible;
                LoadStripDesigner();
            }
            else
            {
                StripDesignerPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshStatsTab()
        {
            var id = _settings.ActiveEventId;
            if (id.HasValue) _statsPanel.Refresh(id.Value);
            else             _statsPanel.Clear();
        }

        // --- Strip designer tab --------------------------------------------------

        private void LoadStripDesigner()
        {
            if (_eventPanel.SelectedEventId.HasValue)
            {
                var ev = _events.GetById(_eventPanel.SelectedEventId.Value);
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
