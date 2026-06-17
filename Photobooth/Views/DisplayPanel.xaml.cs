using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class DisplayPanel : UserControl
{
    private readonly SettingsManager  _settings;
    private readonly AppearancePanel  _appearancePanel;
    private bool _loading = true;

    public DisplayPanel(SettingsManager settings, AppearancePanel appearancePanel)
    {
        _settings        = settings;
        _appearancePanel = appearancePanel;
        InitializeComponent();
        AppearancePanelHost.Content = _appearancePanel;
        _loading = false;
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
        CountdownSlider.Value   = _settings.CountdownSeconds;
        PreviewHoldSlider.Value = _settings.PreviewHoldSeconds;
        RetakeHoldSlider.Value  = _settings.RetakeHoldSeconds;
        MaxRetakesSlider.Value  = _settings.MaxRetakesPerSlot;
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

    private void UpdateMaxRetakesLabel()
    {
        var v = (int)MaxRetakesSlider.Value;
        MaxRetakesLabel.Text = v == 0 ? "∞" : v.ToString();
    }
}
