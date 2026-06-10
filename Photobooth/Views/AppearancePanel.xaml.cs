using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views;

public partial class AppearancePanel : UserControl
{
    private readonly IEventService   _events;
    private readonly SettingsManager _settings;

    private const string DefaultAccent     = "#E94560";
    private const string DefaultBackground = "#1A1A2E";
    private const string DefaultSurface    = "#16213E";

    public int? SelectedEventId { get; set; }

    public event EventHandler<BitmapImage?> BackgroundImageChanged = delegate { };

    public AppearancePanel(IEventService events, SettingsManager settings)
    {
        _events   = events;
        _settings = settings;
        InitializeComponent();
    }

    // Called by GreetingPage.OnLoaded to restore saved appearance without an event arg.
    public void ApplyActiveEventAppearance()
    {
        var id = _settings.ActiveEventId;
        if (!id.HasValue) return;
        var ev = _events.GetById(id.Value);
        if (ev is null) return;

        SelectedEventId = id;

        AccentHexBox.Text  = ev.AccentColor     ?? DefaultAccent;
        BgColorHexBox.Text = ev.BackgroundColor ?? DefaultBackground;
        SurfaceHexBox.Text = ev.SurfaceColor    ?? DefaultSurface;

        if (!string.IsNullOrEmpty(ev.AccentColor))     ApplyBrushColor("AccentBrush",     ev.AccentColor);
        if (!string.IsNullOrEmpty(ev.BackgroundColor)) ApplyBrushColor("BackgroundBrush", ev.BackgroundColor);
        if (!string.IsNullOrEmpty(ev.SurfaceColor))    ApplyBrushColor("SurfaceBrush",    ev.SurfaceColor);

        if (!string.IsNullOrEmpty(ev.BackgroundImagePath) && File.Exists(ev.BackgroundImagePath))
        {
            try
            {
                var bmp = new BitmapImage(new Uri(ev.BackgroundImagePath));
                BackgroundImageChanged(this, bmp);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restore background image on load");
            }
        }
    }

    // Called by GreetingPage.OnActiveEventChanged when a new event is selected.
    public void LoadEventAppearance(Data.Models.Event ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var accent = ev.AccentColor     ?? DefaultAccent;
        var bg     = ev.BackgroundColor ?? DefaultBackground;
        var surf   = ev.SurfaceColor    ?? DefaultSurface;

        AccentHexBox.Text  = accent;
        BgColorHexBox.Text = bg;
        SurfaceHexBox.Text = surf;

        ApplyBrushColor("AccentBrush",     accent);
        ApplyBrushColor("BackgroundBrush", bg);
        ApplyBrushColor("SurfaceBrush",    surf);

        if (!string.IsNullOrEmpty(ev.BackgroundImagePath) && File.Exists(ev.BackgroundImagePath))
        {
            try
            {
                var bmp = new BitmapImage(new Uri(ev.BackgroundImagePath));
                BgPathBox.Text             = ev.BackgroundImagePath;
                BgPreviewImage.Source      = bmp;
                BgPreviewBorder.Visibility = Visibility.Visible;
                BackgroundImageChanged(this, bmp);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restore background image for event {Id}", ev.Id);
                ClearBackground();
            }
        }
        else
        {
            ClearBackground();
        }
    }

    private void ApplyAccentColor_Click(object sender, RoutedEventArgs e)
    {
        var hex = AccentHexBox.Text.Trim();
        ApplyBrushColor("AccentBrush", hex);
        if (SelectedEventId.HasValue)
            _events.SetAccentColor(SelectedEventId.Value, hex);
    }

    private void ApplyBgColor_Click(object sender, RoutedEventArgs e)
    {
        var hex = BgColorHexBox.Text.Trim();
        ApplyBrushColor("BackgroundBrush", hex);
        if (SelectedEventId.HasValue)
            _events.SetBackgroundColor(SelectedEventId.Value, hex);
    }

    private void ApplySurfaceColor_Click(object sender, RoutedEventArgs e)
    {
        var hex = SurfaceHexBox.Text.Trim();
        ApplyBrushColor("SurfaceBrush", hex);
        if (SelectedEventId.HasValue)
            _events.SetSurfaceColor(SelectedEventId.Value, hex);
    }

    private void RevertAppearance_Click(object sender, RoutedEventArgs e)
    {
        AccentHexBox.Text  = DefaultAccent;
        BgColorHexBox.Text = DefaultBackground;
        SurfaceHexBox.Text = DefaultSurface;

        ApplyBrushColor("AccentBrush",     DefaultAccent);
        ApplyBrushColor("BackgroundBrush", DefaultBackground);
        ApplyBrushColor("SurfaceBrush",    DefaultSurface);

        ClearBackground();

        if (SelectedEventId.HasValue)
        {
            _events.SetAccentColor(SelectedEventId.Value, null);
            _events.SetBackgroundColor(SelectedEventId.Value, null);
            _events.SetSurfaceColor(SelectedEventId.Value, null);
            _events.SetBackgroundImagePath(SelectedEventId.Value, null);
        }

        Log.Information("Appearance reverted to defaults");
    }

    private void BrowseGreetingBg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select greeting background image",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.tiff|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bmp = new BitmapImage(new Uri(dlg.FileName));
            BgPathBox.Text             = dlg.FileName;
            BgPreviewImage.Source      = bmp;
            BgPreviewBorder.Visibility = Visibility.Visible;
            BackgroundImageChanged(this, bmp);
            Log.Information("Greeting background set: {Path}", dlg.FileName);

            if (SelectedEventId.HasValue)
                _events.SetBackgroundImagePath(SelectedEventId.Value, dlg.FileName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load background image");
        }
    }

    private void ClearGreetingBg_Click(object sender, RoutedEventArgs e)
    {
        ClearBackground();
        if (SelectedEventId.HasValue)
            _events.SetBackgroundImagePath(SelectedEventId.Value, null);
    }

    private void ClearBackground()
    {
        BgPathBox.Text             = string.Empty;
        BgPreviewImage.Source      = null;
        BgPreviewBorder.Visibility = Visibility.Collapsed;
        BackgroundImageChanged(this, null);
        Log.Information("Greeting background cleared");
    }

    private static void ApplyBrushColor(string resourceKey, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Application.Current.Resources[resourceKey] = new SolidColorBrush(color);

            if (resourceKey == "AccentBrush")
            {
                Application.Current.Resources["AccentHoverBrush"]   = new SolidColorBrush(Lighten(color, 0.12));
                Application.Current.Resources["AccentPressedBrush"] = new SolidColorBrush(Darken(color, 0.22));
            }

            Log.Debug("Applied {Key} = {Hex}", resourceKey, hex);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Invalid color value '{Hex}' for {Key}", hex, resourceKey);
        }
    }

    private static Color Lighten(Color c, double amount) => Color.FromArgb(c.A,
        (byte)Math.Min(255, c.R + (int)((255 - c.R) * amount)),
        (byte)Math.Min(255, c.G + (int)((255 - c.G) * amount)),
        (byte)Math.Min(255, c.B + (int)((255 - c.B) * amount)));

    private static Color Darken(Color c, double amount) => Color.FromArgb(c.A,
        (byte)(c.R * (1.0 - amount)),
        (byte)(c.G * (1.0 - amount)),
        (byte)(c.B * (1.0 - amount)));
}
