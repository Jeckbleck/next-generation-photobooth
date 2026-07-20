using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Camera;
using Photobooth.Helpers;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views;

public partial class CameraSettingsPanel : UserControl
{
    private readonly CameraService   _camera;
    private readonly SettingsManager _settings;
    private bool     _settingCameraControls;
    private bool     _presetStatusNoteActive;
    private bool     _presetApplyInFlight;
    private EvfPump? _inlineEvfPump;

    public CameraSettingsPanel(CameraService camera, SettingsManager settings)
    {
        _camera   = camera;
        _settings = settings;
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
        if (_presetApplyInFlight) return;
        if (!_camera.IsConnected)
        {
            CameraModelLabel.Text            = "No camera connected";
            CameraSettingStatusText.Text     = "Connect a camera and retry.";
            _presetStatusNoteActive          = false;
            ReconnectCameraButton.Visibility = Visibility.Visible;
            IsoComboBox.IsEnabled = TvComboBox.IsEnabled = AvComboBox.IsEnabled =
                MeteringModeComboBox.IsEnabled = WhiteBalanceComboBox.IsEnabled =
                ImageQualityComboBox.IsEnabled = false;
            RefreshPresetDropdown();
            PresetComboBox.IsEnabled = SavePresetButton.IsEnabled = false;
            return;
        }

        ReconnectCameraButton.Visibility = Visibility.Collapsed;

        CameraModelLabel.Text        = _camera.ModelName ?? "Camera";
        CameraSettingStatusText.Text = "Loading valid values from camera…";
        _presetStatusNoteActive      = false;
        IsoComboBox.IsEnabled = TvComboBox.IsEnabled = AvComboBox.IsEnabled =
            MeteringModeComboBox.IsEnabled = WhiteBalanceComboBox.IsEnabled =
            ImageQualityComboBox.IsEnabled = true;
        PresetComboBox.IsEnabled = SavePresetButton.IsEnabled = true;
        RefreshPresetDropdown();

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

    private void RefreshPresetDropdown()
    {
        _settingCameraControls = true;
        try
        {
            PresetComboBox.Items.Clear();
            PresetComboBox.Items.Add(new ComboBoxItem { Content = "— Select preset —", Tag = null });
            foreach (var preset in _settings.CameraPresets)
            {
                PresetComboBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset.Name });
            }
            PresetComboBox.SelectedIndex = 0;
            DeletePresetButton.IsEnabled = false;
        }
        finally
        {
            _settingCameraControls = false;
        }
    }

    private async void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingCameraControls || _presetApplyInFlight) return;
        if (PresetComboBox.SelectedItem is not ComboBoxItem { Tag: string presetName })
        {
            DeletePresetButton.IsEnabled = false;
            return;
        }

        DeletePresetButton.IsEnabled = true;

        var preset = _settings.CameraPresets.FirstOrDefault(p => p.Name == presetName);
        if (preset is null) return;

        await ApplyPresetAsync(preset);
    }

    private async Task ApplyPresetAsync(CameraPreset preset)
    {
        _presetApplyInFlight = true;
        try
        {
            SetPresetControlsEnabled(false);
            _inlineEvfPump?.Stop();
            _inlineEvfPump = null;
            PresetApplySpinner.Visibility = Visibility.Visible;
            CameraSettingStatusText.Text  = "Applying preset…";
            _presetStatusNoteActive       = true;

            var skipped = new List<string>();
            var pending = new List<(string Label, Task<bool> Task)>();

            QueuePresetValue(EDSDKLib.EDSDK.PropID_ISOSpeed, preset.Iso, "Iso", skipped, pending);
            QueuePresetValue(EDSDKLib.EDSDK.PropID_Tv,       preset.Tv,  "Tv",  skipped, pending);
            QueuePresetValue(EDSDKLib.EDSDK.PropID_Av,       preset.Av,  "Av",  skipped, pending);

            await Task.WhenAll(pending.Select(p => p.Task));

            var applied  = new List<string>();
            var timedOut = new List<string>();
            foreach (var (label, task) in pending)
            {
                if (task.Result) applied.Add(label);
                else timedOut.Add(label);
            }

            CameraSettingStatusText.Text = BuildPresetStatusMessage(applied, skipped, timedOut);
            _presetStatusNoteActive      = skipped.Count > 0 || timedOut.Count > 0;
            PresetApplySpinner.Visibility = Visibility.Collapsed;

            // If the camera disconnected mid-apply, no PropertyChanged event will ever
            // arrive for the pending properties — they simply resolve false when
            // PropertyConfirmTimeout elapses, and this method still unwinds normally
            // (spinner hidden, controls re-enabled) without needing to listen for
            // CameraDisconnected explicitly.
            if (_camera.IsConnected) StartInlinePreview();
            SetPresetControlsEnabled(true);
        }
        finally
        {
            _presetApplyInFlight = false;
        }
    }

    private void QueuePresetValue(uint propId, uint value, string label,
        List<string> skipped, List<(string Label, Task<bool> Task)> pending)
    {
        int[]? desc = _camera.GetPropertyDesc(propId);
        if (CameraPropertyMaps.IsSupportedValue(desc, value))
            pending.Add((label, _camera.SetPropertyAsync(propId, value)));
        else
            skipped.Add(label);
    }

    private static string BuildPresetStatusMessage(List<string> applied, List<string> skipped, List<string> timedOut)
    {
        if (skipped.Count == 0 && timedOut.Count == 0)
            return $"Applied {string.Join(", ", applied)}";

        var problems = new List<string>();
        if (skipped.Count  > 0) problems.Add($"{string.Join(", ", skipped)} not supported on this camera");
        if (timedOut.Count > 0) problems.Add($"{string.Join(", ", timedOut)} did not confirm in time");

        if (applied.Count == 0)
            return $"None applied — {string.Join("; ", problems)}.";

        return $"Applied {string.Join(", ", applied)} — {string.Join("; ", problems)}.";
    }

    private void SetPresetControlsEnabled(bool enabled)
    {
        PresetComboBox.IsEnabled     = enabled;
        IsoComboBox.IsEnabled        = enabled;
        TvComboBox.IsEnabled         = enabled;
        AvComboBox.IsEnabled         = enabled;
        SavePresetButton.IsEnabled   = enabled;
        DeletePresetButton.IsEnabled = enabled && PresetComboBox.SelectedItem is ComboBoxItem { Tag: string };
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedTag(IsoComboBox) is not uint iso) return;
        if (GetSelectedTag(TvComboBox)  is not uint tv)  return;
        if (GetSelectedTag(AvComboBox)  is not uint av)  return;

        var dialog = new SavePresetDialog(_settings.CameraPresets.Select(p => p.Name))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true) return;

        _settings.SaveCameraPreset(dialog.PresetName, iso, tv, av);
        RefreshPresetDropdown();
    }

    private static uint? GetSelectedTag(ComboBox cb) =>
        cb.SelectedItem is ComboBoxItem { Tag: uint v } ? v : null;

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is not ComboBoxItem { Tag: string name }) return;

        var result = MessageBox.Show(
            $"Delete preset \"{name}\"?",
            "Delete Preset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _settings.DeleteCameraPreset(name);
        RefreshPresetDropdown();
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

            if (!_presetStatusNoteActive)
                CameraSettingStatusText.Text = string.Empty;
        });
    }

    private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingCameraControls) return;
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;
        if (item.Tag is not uint value) return;
        _presetStatusNoteActive = false;

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
        _inlineEvfPump?.Stop();
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
