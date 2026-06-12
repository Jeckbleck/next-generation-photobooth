using System.IO;
using System.Windows.Controls;
using Photobooth.Camera;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class AboutPanel : UserControl
{
    private readonly CameraService   _camera;
    private readonly SettingsManager _settings;

    public AboutPanel(CameraService camera, SettingsManager settings)
    {
        _camera   = camera;
        _settings = settings;
        InitializeComponent();
    }

    // Called by GreetingPage.SelectTab when the About tab becomes visible.
    public void Activate()
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
}
