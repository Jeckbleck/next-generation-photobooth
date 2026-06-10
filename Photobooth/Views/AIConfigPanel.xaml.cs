using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views;

public partial class AIConfigPanel : UserControl
{
    private readonly AIEnhancementClient _aiClient;
    private readonly SettingsManager     _settings;

    public event EventHandler<bool> AIEnhancementEnabledChanged = delegate { };

    public AIConfigPanel(AIEnhancementClient aiClient, SettingsManager settings)
    {
        _aiClient = aiClient;
        _settings = settings;
        InitializeComponent();
    }

    public void Activate()
    {
        AIEnableToggle.IsChecked = _settings.AIEnhancementEnabled;
        AIServerUrlBox.Text      = _settings.AIServerUrl;
        AIApiKeyBox.Text         = _settings.AIApiKey;
        _ = TestAIConnectionAsync();
    }

    private void AIEnableToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = AIEnableToggle.IsChecked == true;
        _settings.SetAIEnhancementEnabled(enabled);
        AIEnhancementEnabledChanged(this, enabled);
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
}
