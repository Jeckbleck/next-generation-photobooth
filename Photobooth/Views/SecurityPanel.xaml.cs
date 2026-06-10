using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class SecurityPanel : UserControl
{
    private readonly SettingsManager _settings;

    public SecurityPanel(SettingsManager settings)
    {
        _settings = settings;
        InitializeComponent();
    }

    private void ChangePIN_Click(object sender, RoutedEventArgs e)
    {
        PinChangeStatus.Visibility = Visibility.Collapsed;

        var newPin     = NewPinBox.Password;
        var confirmPin = ConfirmPinBox.Password;

        if (!newPin.All(char.IsDigit))
        {
            ShowPinStatus("PIN must contain digits only.", isError: true);
            return;
        }
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
}
