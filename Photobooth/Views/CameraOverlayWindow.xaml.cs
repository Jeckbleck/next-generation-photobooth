using System;
using System.Windows;
using System.Windows.Input;
using Photobooth.Camera;

namespace Photobooth.Views;

public partial class CameraOverlayWindow : Window
{
    private readonly CameraService _camera;

    public CameraOverlayWindow(CameraService camera)
    {
        _camera = camera;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshAll();
            _camera.CameraPropertyChanged += OnPropertyChanged;
        };
        Closed += (_, _) => _camera.CameraPropertyChanged -= OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, uint propId) =>
        Dispatcher.BeginInvoke(RefreshAll);

    private void RefreshAll()
    {
        IsoText.Text     = Read(EDSDKLib.EDSDK.PropID_ISOSpeed,    CameraPropertyMaps.LookupIso);
        TvText.Text      = Read(EDSDKLib.EDSDK.PropID_Tv,          CameraPropertyMaps.LookupTv);
        AvText.Text      = Read(EDSDKLib.EDSDK.PropID_Av,          CameraPropertyMaps.LookupAv);
        WbText.Text      = Read(EDSDKLib.EDSDK.PropID_WhiteBalance, CameraPropertyMaps.LookupWb);
        IqText.Text      = Read(EDSDKLib.EDSDK.PropID_ImageQuality, CameraPropertyMaps.LookupIq);
    }

    private string Read(uint propId, Func<uint, string> lookup)
    {
        var v = _camera.GetPropertyValue(propId);
        return v is uint val ? lookup(val) : "—";
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
