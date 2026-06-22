using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
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

    private void PickAccentColor_Click(object sender, RoutedEventArgs e) =>
        OpenColorPicker("AccentBrush",
            hex => { if (SelectedEventId.HasValue) _events.SetAccentColor(SelectedEventId.Value, hex); });

    private void PickBgColor_Click(object sender, RoutedEventArgs e) =>
        OpenColorPicker("BackgroundBrush",
            hex => { if (SelectedEventId.HasValue) _events.SetBackgroundColor(SelectedEventId.Value, hex); });

    private void PickSurfaceColor_Click(object sender, RoutedEventArgs e) =>
        OpenColorPicker("SurfaceBrush",
            hex => { if (SelectedEventId.HasValue) _events.SetSurfaceColor(SelectedEventId.Value, hex); });

    private void RevertAppearance_Click(object sender, RoutedEventArgs e)
    {
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

    private static Color GetCurrentColor(string resourceKey)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush b) return b.Color;
        return Colors.Gray;
    }

    private void OpenColorPicker(string resourceKey, Action<string> save)
    {
        var current = GetCurrentColor(resourceKey);
        var picked  = ShowNativeColorDialog(current);
        if (picked is null) return;

        var hex = $"#{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
        ApplyBrushColor(resourceKey, hex);
        save(hex);
    }

    // Win32 ChooseColor — same dialog ColorDialog wraps, no WinForms dependency needed.
    [StructLayout(LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public uint   lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public uint   rgbResult;
        public IntPtr lpCustColors;
        public uint   Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    [DllImport("comdlg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref CHOOSECOLOR cc);

    private static readonly uint[] _customColors = new uint[16];

    private Color? ShowNativeColorDialog(Color current)
    {
        var owner = Window.GetWindow(this);
        uint rgb = (uint)(current.R | (current.G << 8) | (current.B << 16));
        var cc = new CHOOSECOLOR
        {
            lStructSize = (uint)Marshal.SizeOf<CHOOSECOLOR>(),
            hwndOwner   = owner is not null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero,
            rgbResult   = rgb,
            Flags       = 0x0001 | 0x0002, // CC_RGBINIT | CC_FULLOPEN
        };

        var handle = GCHandle.Alloc(_customColors, GCHandleType.Pinned); // pin uint[] so GC doesn't move it during P/Invoke
        try
        {
            cc.lpCustColors = handle.AddrOfPinnedObject();
            if (!ChooseColor(ref cc)) return null;

            byte r = (byte)(cc.rgbResult & 0xFF);
            byte g = (byte)((cc.rgbResult >> 8) & 0xFF);
            byte b = (byte)((cc.rgbResult >> 16) & 0xFF);
            return Color.FromRgb(r, g, b);
        }
        finally
        {
            handle.Free();
        }
    }
}
