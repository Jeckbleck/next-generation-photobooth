using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Camera;
using Photobooth.Helpers;
using Serilog;

namespace Photobooth.Views;

public partial class CameraSettingsPanel : UserControl
{
    private readonly CameraService _camera;
    private bool     _settingCameraControls;
    private EvfPump? _inlineEvfPump;

    public CameraSettingsPanel(CameraService camera)
    {
        _camera = camera;
        InitializeComponent();
    }

    // Call when the Camera tab becomes visible.
    public void Activate()
    {
        LoadCameraSettings();
        InlinePreviewImage.Source          = null;
        InlinePreviewStatusText.Text       = "Starting camera preview…";
        InlinePreviewStatusText.Visibility = Visibility.Visible;
        InlinePreviewInfoText.Text         = string.Empty;
        ResumePreviewButton.Visibility     = Visibility.Collapsed;
        PreviewSpinner.Visibility          = _camera.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        StartInlinePreview();
    }

    // Call when the Camera tab loses focus or the page unloads.
    public void Deactivate()
    {
        _camera.CameraPropertyChanged -= OnCameraPropertyChanged;
        StopInlinePreview();
    }

    private void LoadCameraSettings()
    {
        if (!_camera.IsConnected)
        {
            CameraModelLabel.Text            = "No camera connected";
            CameraSettingStatusText.Text     = "Connect a camera and retry.";
            ReconnectCameraButton.Visibility = Visibility.Visible;
            IsoComboBox.IsEnabled = TvComboBox.IsEnabled = AvComboBox.IsEnabled =
                MeteringModeComboBox.IsEnabled = WhiteBalanceComboBox.IsEnabled =
                ImageQualityComboBox.IsEnabled = false;
            return;
        }

        ReconnectCameraButton.Visibility = Visibility.Collapsed;

        CameraModelLabel.Text        = _camera.ModelName ?? "Camera";
        CameraSettingStatusText.Text = "Loading valid values from camera…";
        IsoComboBox.IsEnabled = TvComboBox.IsEnabled = AvComboBox.IsEnabled =
            MeteringModeComboBox.IsEnabled = WhiteBalanceComboBox.IsEnabled =
            ImageQualityComboBox.IsEnabled = true;

        _camera.CameraPropertyChanged -= OnCameraPropertyChanged;
        _camera.CameraPropertyChanged += OnCameraPropertyChanged;

        RefreshCameraDropdown(IsoComboBox,          EDSDKLib.EDSDK.PropID_ISOSpeed,     CameraPropertyMaps.Iso,          CameraPropertyMaps.LookupIso);
        RefreshCameraDropdown(TvComboBox,           EDSDKLib.EDSDK.PropID_Tv,           CameraPropertyMaps.Tv,           CameraPropertyMaps.LookupTv);
        RefreshCameraDropdown(AvComboBox,           EDSDKLib.EDSDK.PropID_Av,           CameraPropertyMaps.Av,           CameraPropertyMaps.LookupAv);
        RefreshCameraDropdown(MeteringModeComboBox, EDSDKLib.EDSDK.PropID_MeteringMode, CameraPropertyMaps.MeteringMode, CameraPropertyMaps.LookupMeteringMode);
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
            if (!IsVisible) return;

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
                case EDSDKLib.EDSDK.PropID_MeteringMode:
                    RefreshCameraDropdown(MeteringModeComboBox, propId, CameraPropertyMaps.MeteringMode, CameraPropertyMaps.LookupMeteringMode);
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
                    : cb == MeteringModeComboBox  ? EDSDKLib.EDSDK.PropID_MeteringMode
                    : cb == WhiteBalanceComboBox  ? EDSDKLib.EDSDK.PropID_WhiteBalance
                    : cb == ImageQualityComboBox  ? EDSDKLib.EDSDK.PropID_ImageQuality
                    : 0u;

        if (propId == 0) return;
        Log.Information("Camera property 0x{PropId:X8} → 0x{Value:X8}", propId, value);
        _camera.SetProperty(propId, value);
    }

    private void StartInlinePreview()
    {
        if (!_camera.IsConnected) return;
        _inlineEvfPump = new EvfPump(
            _camera,
            Dispatcher,
            frame =>
            {
                InlinePreviewImage.Source          = frame;
                PreviewSpinner.Visibility          = Visibility.Collapsed;
                InlinePreviewStatusText.Visibility = Visibility.Collapsed;
            },
            onStall: () =>
            {
                PreviewSpinner.Visibility          = Visibility.Collapsed;
                InlinePreviewStatusText.Text       = "Camera preview unavailable.";
                InlinePreviewStatusText.Visibility = Visibility.Visible;
            },
            watchdogMs: 100);
        _inlineEvfPump.Start();
    }

    private void StopInlinePreview()
    {
        _inlineEvfPump?.Stop();
        _inlineEvfPump = null;
    }

    private async void TakeTestShot_Click(object sender, RoutedEventArgs e)
    {
        if (!_camera.IsConnected) return;

        TakeTestShotButton.IsEnabled   = false;
        ResumePreviewButton.Visibility = Visibility.Collapsed;

        _inlineEvfPump?.Stop();
        _inlineEvfPump = null;

        InlinePreviewStatusText.Text       = "Capturing…";
        InlinePreviewStatusText.Visibility = Visibility.Visible;
        InlinePreviewInfoText.Text         = string.Empty;
        PreviewSpinner.Visibility          = Visibility.Visible;

        try
        {
            string path = await _camera.TakePictureAsync();
            if (!IsVisible) return;

            var bitmap = BitmapHelper.LoadFromFile(path);

            InlinePreviewImage.Source          = bitmap;
            InlinePreviewStatusText.Visibility = Visibility.Collapsed;
            InlinePreviewInfoText.Text         = $"Saved: {Path.GetFileName(path)}";
            ResumePreviewButton.Visibility     = Visibility.Visible;

            Log.Information("Test shot saved: {Path}", path);
        }
        catch (OperationCanceledException)
        {
            if (!IsVisible) return;
            InlinePreviewStatusText.Text   = "Capture timed out.";
            ResumePreviewButton.Visibility = Visibility.Visible;
            Log.Warning("Test shot timed out");
        }
        catch (Exception ex)
        {
            if (!IsVisible) return;
            InlinePreviewStatusText.Text   = $"Error: {ex.Message}";
            ResumePreviewButton.Visibility = Visibility.Visible;
            Log.Error(ex, "Test shot failed");
        }
        finally
        {
            if (IsVisible) PreviewSpinner.Visibility = Visibility.Collapsed;
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
        PreviewSpinner.Visibility          = Visibility.Visible;
        StartInlinePreview();
    }

    private void ReconnectCamera_Click(object sender, RoutedEventArgs e)
    {
        ReconnectCameraButton.Visibility = Visibility.Collapsed;
        ReconnectSpinner.Visibility      = Visibility.Visible;
        CameraSettingStatusText.Text     = "Searching for camera…";
        bool ok = _camera.Initialize();
        ReconnectSpinner.Visibility      = Visibility.Collapsed;
        if (ok)
            Activate();
        else
        {
            ReconnectCameraButton.Visibility = Visibility.Visible;
            CameraSettingStatusText.Text     = "No camera found — check USB and retry.";
        }
    }
}
