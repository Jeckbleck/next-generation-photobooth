using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Photobooth.Data.Models;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class DisplayPanel : UserControl
{
    private readonly SettingsManager  _settings;
    private readonly AppearancePanel  _appearancePanel;
    private readonly IEventService    _events;
    private bool _loading = true;
    private int? _activeEventId;
    private DispatcherTimer? _greetingDebounce;

    // Fires after greeting text is auto-saved so GreetingPage can refresh the live screen.
    public event EventHandler? GreetingTextSaved;

    public DisplayPanel(SettingsManager settings, AppearancePanel appearancePanel, IEventService events)
    {
        _settings        = settings;
        _appearancePanel = appearancePanel;
        _events          = events;
        InitializeComponent();
        AppearancePanelHost.Content = _appearancePanel;
        _loading = false;
    }

    public void SetActiveEvent(Event? ev)
    {
        _loading = true;
        _activeEventId             = ev?.Id;
        GreetingEyebrowBox.Text    = ev?.GreetingEyebrow  ?? string.Empty;
        GreetingTitleBox.Text      = ev?.GreetingTitle    ?? string.Empty;
        GreetingSubtitleBox.Text   = ev?.GreetingSubtitle ?? string.Empty;
        _loading = false;
    }

    private void GreetingBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || !_activeEventId.HasValue) return;
        if (_greetingDebounce is null)
        {
            _greetingDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _greetingDebounce.Tick += GreetingDebounce_Tick;
        }
        _greetingDebounce.Stop();
        _greetingDebounce.Start();
    }

    private void GreetingDebounce_Tick(object? sender, EventArgs e)
    {
        _greetingDebounce!.Stop();
        if (!_activeEventId.HasValue) return;
        _events.SetGreetingText(_activeEventId.Value,
            GreetingEyebrowBox.Text,
            GreetingTitleBox.Text,
            GreetingSubtitleBox.Text);
        GreetingTextSaved?.Invoke(this, EventArgs.Empty);
    }

    // Forwarding event — GreetingPage subscribes here instead of _appearancePanel directly.
    public event EventHandler<BitmapImage?> BackgroundImageChanged
    {
        add    => _appearancePanel.BackgroundImageChanged += value;
        remove => _appearancePanel.BackgroundImageChanged -= value;
    }

    // Called by GreetingPage.SelectTab when Display tab becomes visible.
    public void Activate()
    {
        _loading = true;
        CountdownSlider.Value                 = _settings.CountdownSeconds;
        PreviewHoldSlider.Value               = _settings.PreviewHoldSeconds;
        RetakeHoldSlider.Value                = _settings.RetakeHoldSeconds;
        MaxRetakesSlider.Value                = _settings.MaxRetakesPerSlot;
        ExperimentalFeaturesToggle.IsChecked  = _settings.ExperimentalFeatures;
        UpdateMaxRetakesLabel();
        _loading = false;
    }

    private void CountdownSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.SetCountdownSeconds((int)CountdownSlider.Value);
    }

    private void PreviewHoldSlider_ValueChanged(object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.SetPreviewHoldSeconds((int)PreviewHoldSlider.Value);
    }

    private void RetakeHoldSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.SetRetakeHoldSeconds((int)RetakeHoldSlider.Value);
    }

    private void MaxRetakesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        UpdateMaxRetakesLabel();
        _settings.SetMaxRetakesPerSlot((int)MaxRetakesSlider.Value);
    }

    private void ExperimentalFeaturesToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.SetExperimentalFeatures(ExperimentalFeaturesToggle.IsChecked == true);
    }

    private void UpdateMaxRetakesLabel()
    {
        var v = (int)MaxRetakesSlider.Value;
        MaxRetakesLabel.Text = v == 0 ? "∞" : v.ToString();
    }
}
