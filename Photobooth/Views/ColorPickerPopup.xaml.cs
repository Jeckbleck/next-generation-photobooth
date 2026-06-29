using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Photobooth.Views;

public partial class ColorPickerPopup : Window
{
    private double _h, _s, _v;
    private bool   _draggingSb, _draggingHue;
    private bool   _suppressHexUpdate;
    private Color? _result;

    public ColorPickerPopup(Color initial)
    {
        InitializeComponent();
        RgbToHsv(initial, out _h, out _s, out _v);
        BeforePreview.Background = new SolidColorBrush(initial);
        Loaded += (_, _) => UpdateAll();
    }

    public Color? ShowPickedColor()
    {
        ShowDialog();
        return _result;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        DragMove();

    // ── SB canvas ──────────────────────────────────────────────────────────────

    private void SbCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SbCanvas.CaptureMouse();
        _draggingSb = true;
        UpdateSbFromPoint(e.GetPosition(SbCanvas));
    }

    private void SbCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingSb) UpdateSbFromPoint(e.GetPosition(SbCanvas));
    }

    private void SbCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSb = false;
        SbCanvas.ReleaseMouseCapture();
    }

    private void UpdateSbFromPoint(Point p)
    {
        _s = Math.Clamp(p.X / SbCanvas.ActualWidth,       0.0, 1.0);
        _v = Math.Clamp(1.0 - p.Y / SbCanvas.ActualHeight, 0.0, 1.0);
        UpdateThumbAndPreview();
    }

    // ── Hue strip ──────────────────────────────────────────────────────────────

    private void HueCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        HueCanvas.CaptureMouse();
        _draggingHue = true;
        UpdateHueFromPoint(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingHue) UpdateHueFromPoint(e.GetPosition(HueCanvas));
    }

    private void HueCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = false;
        HueCanvas.ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point p)
    {
        _h = Math.Clamp(p.X / HueCanvas.ActualWidth, 0.0, 1.0) * 360.0;
        UpdateAll();
    }

    // ── Render helpers ─────────────────────────────────────────────────────────

    private void UpdateAll()
    {
        // Keep the SB canvas background synced to the current hue
        SbHueStop.Color = HsvToRgb(_h, 1.0, 1.0);

        // Slide the hue indicator
        var indicatorX = Math.Clamp(_h / 360.0 * HueCanvas.ActualWidth, 0, HueCanvas.ActualWidth - 4);
        System.Windows.Controls.Canvas.SetLeft(HueIndicator, indicatorX);

        UpdateThumbAndPreview();
    }

    private void UpdateThumbAndPreview()
    {
        // Centre the 28×28 thumb on the HSV point
        System.Windows.Controls.Canvas.SetLeft(SbThumb, _s * SbCanvas.ActualWidth  - 14);
        System.Windows.Controls.Canvas.SetTop( SbThumb, (1.0 - _v) * SbCanvas.ActualHeight - 14);

        var color = HsvToRgb(_h, _s, _v);
        AfterPreview.Background = new SolidColorBrush(color);

        if (!_suppressHexUpdate)
            HexInput.Text = $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // ── Hex input ──────────────────────────────────────────────────────────────

    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHexUpdate) return;
        var text = HexInput.Text.Trim();
        if (text.Length != 6) return;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + text);
            RgbToHsv(color, out _h, out _s, out _v);
            _suppressHexUpdate = true;
            UpdateAll();
            _suppressHexUpdate = false;
        }
        catch { }
    }

    // ── Buttons ────────────────────────────────────────────────────────────────

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        _result = HsvToRgb(_h, _s, _v);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    // ── HSV ↔ RGB ──────────────────────────────────────────────────────────────

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max   = Math.Max(r, Math.Max(g, b));
        double min   = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max == 0.0 ? 0.0 : delta / max;

        if (delta == 0.0) { h = 0.0; return; }

        if      (max == r) h = 60.0 * (((g - b) / delta) % 6.0);
        else if (max == g) h = 60.0 * (((b - r) / delta) + 2.0);
        else               h = 60.0 * (((r - g) / delta) + 4.0);

        if (h < 0.0) h += 360.0;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        if (s == 0.0)
        {
            var ch = (byte)(v * 255);
            return Color.FromRgb(ch, ch, ch);
        }

        double hh = h / 60.0;
        int    i  = (int)Math.Floor(hh) % 6;
        double f  = hh - Math.Floor(hh);
        double p  = v * (1.0 - s);
        double q  = v * (1.0 - s * f);
        double t  = v * (1.0 - s * (1.0 - f));

        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };

        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
