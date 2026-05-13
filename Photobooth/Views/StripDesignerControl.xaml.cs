using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;
using Microsoft.Win32;
using Photobooth.Data.Models;
using Serilog;

namespace Photobooth.Views
{
    public partial class StripDesignerControl : UserControl
    {
        private const double CanvasW     = 300;
        private const double CanvasH     = 450;
        private const int    MaxSlots    = 3;
        private const double HandleSize  = 12;
        private const double MinSlotSize = 40;

        private string? _templateDir;
        private string? _eventSlug;
        private int?    _eventId;
        private readonly List<SlotControl> _slots = new();

        // Drag state
        private SlotControl? _dragging;
        private Point        _dragOffset;

        // Resize state
        private SlotControl? _resizing;
        private int          _resizeHandle;   // 0=NW 1=NE 2=SW 3=SE
        private Point        _resizeOrigin;
        private Rect         _resizeStartRect;

        public StripDesignerControl()
        {
            InitializeComponent();
            UpdateStatus();
        }

        // --- Public API ----------------------------------------------------------

        public void LoadForEvent(int? eventId, string? slug, string? templateDir, string? templateImagePath)
        {
            _eventId     = eventId;
            _eventSlug   = slug;
            _templateDir = templateDir;
            ClearCanvas();

            if (slug is null || templateDir is null)
            {
                UploadButton.IsEnabled = false;
                UpdateStatus();
                return;
            }

            UploadButton.IsEnabled = true;

            // Load template image from the DB-stored path
            if (!string.IsNullOrEmpty(templateImagePath) && File.Exists(templateImagePath))
                LoadTemplateImage(templateImagePath);

            // Load slot definitions from the event's Strip template folder
            var jsonPath = JsonPath();
            if (!File.Exists(jsonPath)) { UpdateStatus(); return; }

            try
            {
                var config = JsonSerializer.Deserialize<StripTemplateConfig>(File.ReadAllText(jsonPath));
                if (config is null) return;

                foreach (var def in config.Slots.OrderBy(s => s.Index))
                    CreateSlot(def);

                UpdateStatus();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load strip slot config for '{Slug}'", slug);
            }
        }

        // --- Toolbar handlers ----------------------------------------------------

        private void Upload_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select strip template image",
                Filter = "PNG images|*.png|All files|*.*",
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                LoadTemplateImage(dlg.FileName);

                if (_eventId.HasValue)
                    App.Events.SetPhotostripTemplatePath(_eventId.Value, dlg.FileName);

                UpdateStatus();
                Log.Information("Strip template set to: {Path}", dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to set strip template image");
                StatusText.Text = "Could not load the selected image.";
            }
        }

        private void AddSlot_Click(object sender, RoutedEventArgs e)
        {
            if (_slots.Count >= MaxSlots) return;
            int nextIndex = Enumerable.Range(1, MaxSlots).First(i => _slots.All(s => s.Index != i));

            CreateSlot(new StripSlotDefinition
            {
                Index  = nextIndex,
                X      = 0.10,
                Y      = 0.05 + (_slots.Count * 0.31),
                Width  = 0.80,
                Height = 0.26,
            });
            UpdateStatus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_templateDir is null) return;
            try
            {
                var config = new StripTemplateConfig
                {
                    Slots = _slots.Select(s => new StripSlotDefinition
                    {
                        Index  = s.Index,
                        X      = s.Left   / CanvasW,
                        Y      = s.Top    / CanvasH,
                        Width  = s.Width  / CanvasW,
                        Height = s.Height / CanvasH,
                    }).OrderBy(s => s.Index).ToList(),
                };

                File.WriteAllText(JsonPath(),
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

                StatusText.Text = "Saved.";
                Log.Information("Strip template saved for event '{Slug}'", _eventSlug);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save strip template config");
                StatusText.Text = "Save failed.";
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ClearCanvas();
            UploadButton.IsEnabled = _eventSlug is not null;

            if (_eventId.HasValue)
                App.Events.SetPhotostripTemplatePath(_eventId.Value, null);

            UpdateStatus();
        }

        // --- Slot creation -------------------------------------------------------

        private void CreateSlot(StripSlotDefinition def)
        {
            var slot = new SlotControl
            {
                Index  = def.Index,
                Left   = def.X      * CanvasW,
                Top    = def.Y      * CanvasH,
                Width  = def.Width  * CanvasW,
                Height = def.Height * CanvasH,
            };

            BuildSlotVisuals(slot);
            _slots.Add(slot);
            RefreshToolbarState();
        }

        private void BuildSlotVisuals(SlotControl slot)
        {
            var accent = AccentColor();

            // Body — the draggable slot rectangle
            var body = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
                BorderBrush     = Brushes.White,
                BorderThickness = new Thickness(2),
                CornerRadius    = new CornerRadius(2),
                Cursor          = Cursors.SizeAll,
                Child           = new TextBlock
                {
                    Text                = slot.Index.ToString(),
                    FontSize            = 42,
                    FontWeight          = FontWeights.Bold,
                    Foreground          = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Opacity             = 0.85,
                },
            };
            body.MouseLeftButtonDown += (_, e) => { SlotBody_Down(slot, e);  e.Handled = true; };
            body.MouseMove           += (_, e) => SlotBody_Move(slot, e);
            body.MouseLeftButtonUp   += (_, e) => { SlotBody_Up(slot);        e.Handled = true; };
            Panel.SetZIndex(body, 1);
            DesignerCanvas.Children.Add(body);
            slot.Body = body;

            // Delete button — separate canvas child so it never competes with drag
            var del = new Button
            {
                Content         = "×",
                Width           = 22,
                Height          = 22,
                FontSize        = 15,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(Color.FromArgb(200, 180, 40, 40)),
                BorderThickness = new Thickness(0),
                Cursor          = Cursors.Arrow,
                Padding         = new Thickness(0),
            };
            del.Click += (_, _) => RemoveSlot(slot);
            Panel.SetZIndex(del, 3);
            DesignerCanvas.Children.Add(del);
            slot.DeleteBtn = del;

            // 4 corner resize handles (NW NE SW SE)
            var resizeCursors = new[] { Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNESW, Cursors.SizeNWSE };
            slot.Handles = new Rectangle[4];
            for (int h = 0; h < 4; h++)
            {
                int hi = h;
                var handle = new Rectangle
                {
                    Width           = HandleSize,
                    Height          = HandleSize,
                    Fill            = Brushes.White,
                    Stroke          = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x44)),
                    StrokeThickness = 1,
                    Cursor          = resizeCursors[h],
                };
                handle.MouseLeftButtonDown += (_, e) => { Handle_Down(slot, hi, e); e.Handled = true; };
                handle.MouseMove           += (_, e) => { Handle_Move(slot, hi, e); e.Handled = true; };
                handle.MouseLeftButtonUp   += (_, e) => { Handle_Up(slot, hi);      e.Handled = true; };
                Panel.SetZIndex(handle, 2);
                DesignerCanvas.Children.Add(handle);
                slot.Handles[h] = handle;
            }

            LayoutSlot(slot);
        }

        private void LayoutSlot(SlotControl slot)
        {
            Canvas.SetLeft(slot.Body, slot.Left);
            Canvas.SetTop(slot.Body,  slot.Top);
            slot.Body.Width  = slot.Width;
            slot.Body.Height = slot.Height;

            Canvas.SetLeft(slot.DeleteBtn, slot.Left + slot.Width - 24);
            Canvas.SetTop(slot.DeleteBtn,  slot.Top  + 3);

            double hs = HandleSize / 2.0;
            Canvas.SetLeft(slot.Handles[0], slot.Left - hs);
            Canvas.SetTop(slot.Handles[0],  slot.Top  - hs);
            Canvas.SetLeft(slot.Handles[1], slot.Left + slot.Width  - hs);
            Canvas.SetTop(slot.Handles[1],  slot.Top  - hs);
            Canvas.SetLeft(slot.Handles[2], slot.Left - hs);
            Canvas.SetTop(slot.Handles[2],  slot.Top  + slot.Height - hs);
            Canvas.SetLeft(slot.Handles[3], slot.Left + slot.Width  - hs);
            Canvas.SetTop(slot.Handles[3],  slot.Top  + slot.Height - hs);
        }

        // --- Drag ----------------------------------------------------------------

        private void SlotBody_Down(SlotControl slot, MouseButtonEventArgs e)
        {
            _dragging   = slot;
            var pos     = e.GetPosition(DesignerCanvas);
            _dragOffset = new Point(pos.X - slot.Left, pos.Y - slot.Top);
            slot.Body.CaptureMouse();
        }

        private void SlotBody_Move(SlotControl slot, MouseEventArgs e)
        {
            if (_dragging != slot || !slot.Body.IsMouseCaptured) return;
            var pos  = e.GetPosition(DesignerCanvas);
            slot.Left = Math.Clamp(pos.X - _dragOffset.X, 0, CanvasW - slot.Width);
            slot.Top  = Math.Clamp(pos.Y - _dragOffset.Y, 0, CanvasH - slot.Height);
            LayoutSlot(slot);
        }

        private void SlotBody_Up(SlotControl slot)
        {
            _dragging = null;
            slot.Body.ReleaseMouseCapture();
        }

        // --- Resize --------------------------------------------------------------

        private void Handle_Down(SlotControl slot, int handle, MouseButtonEventArgs e)
        {
            _resizing        = slot;
            _resizeHandle    = handle;
            _resizeOrigin    = e.GetPosition(DesignerCanvas);
            _resizeStartRect = new Rect(slot.Left, slot.Top, slot.Width, slot.Height);
            slot.Handles[handle].CaptureMouse();
        }

        private void Handle_Move(SlotControl slot, int handle, MouseEventArgs e)
        {
            if (_resizing != slot || !slot.Handles[handle].IsMouseCaptured) return;

            var pos = e.GetPosition(DesignerCanvas);
            double dx = pos.X - _resizeOrigin.X;
            double dy = pos.Y - _resizeOrigin.Y;
            var r = _resizeStartRect;

            double l = r.Left, t = r.Top, ri = r.Right, b = r.Bottom;

            switch (_resizeHandle)
            {
                case 0: l  = Math.Min(r.Left   + dx, r.Right  - MinSlotSize); t  = Math.Min(r.Top    + dy, r.Bottom - MinSlotSize); break;
                case 1: ri = Math.Max(r.Right  + dx, r.Left   + MinSlotSize); t  = Math.Min(r.Top    + dy, r.Bottom - MinSlotSize); break;
                case 2: l  = Math.Min(r.Left   + dx, r.Right  - MinSlotSize); b  = Math.Max(r.Bottom + dy, r.Top    + MinSlotSize); break;
                case 3: ri = Math.Max(r.Right  + dx, r.Left   + MinSlotSize); b  = Math.Max(r.Bottom + dy, r.Top    + MinSlotSize); break;
            }

            slot.Left   = Math.Max(0, l);
            slot.Top    = Math.Max(0, t);
            slot.Width  = Math.Min(CanvasW, ri) - slot.Left;
            slot.Height = Math.Min(CanvasH, b)  - slot.Top;
            LayoutSlot(slot);
        }

        private void Handle_Up(SlotControl slot, int handle)
        {
            _resizing = null;
            slot.Handles[handle].ReleaseMouseCapture();
        }

        // --- Helpers -------------------------------------------------------------

        private void RemoveSlot(SlotControl slot)
        {
            DesignerCanvas.Children.Remove(slot.Body);
            DesignerCanvas.Children.Remove(slot.DeleteBtn);
            foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
            _slots.Remove(slot);
            RefreshToolbarState();
            UpdateStatus();
        }

        private void LoadTemplateImage(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            TemplateImage.Source = bmp;
            RefreshToolbarState();
        }

        private void ClearCanvas()
        {
            foreach (var slot in _slots.ToList())
            {
                DesignerCanvas.Children.Remove(slot.Body);
                DesignerCanvas.Children.Remove(slot.DeleteBtn);
                foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
            }
            _slots.Clear();
            TemplateImage.Source = null;
            RefreshToolbarState();
        }

        private void RefreshToolbarState()
        {
            bool hasTemplate = TemplateImage.Source is not null;
            AddSlotButton.IsEnabled = hasTemplate && _slots.Count < MaxSlots;
            SaveButton.IsEnabled    = hasTemplate || _slots.Count > 0;
            ClearButton.IsEnabled   = hasTemplate || _slots.Count > 0;
        }

        private void UpdateStatus()
        {
            if (_eventSlug is null)
            {
                StatusText.Text = "Select an event to use the strip designer.";
                return;
            }
            if (TemplateImage.Source is null)
            {
                StatusText.Text = "Upload a template PNG to get started.";
                return;
            }
            if (_slots.Count == 0)
            {
                StatusText.Text = "Click '+ Add Slot' to mark where each photo will appear.";
                return;
            }
            StatusText.Text = _slots.Count < MaxSlots
                ? $"{_slots.Count} of {MaxSlots} slots placed — add more or Save."
                : "All 3 slots placed. Drag to reposition, corners to resize, × to remove.";
        }

        private string JsonPath() => Path.Combine(_templateDir!, "template.json");

        private static Color AccentColor()
        {
            try
            {
                if (Application.Current.Resources["AccentBrush"] is SolidColorBrush b) return b.Color;
            }
            catch { /* fall through */ }
            return Color.FromRgb(0xE9, 0x45, 0x60);
        }
    }

    internal class SlotControl
    {
        public int         Index     { get; set; }
        public Border      Body      { get; set; } = null!;
        public Button      DeleteBtn { get; set; } = null!;
        public Rectangle[] Handles   { get; set; } = Array.Empty<Rectangle>();
        public double      Left      { get; set; }
        public double      Top       { get; set; }
        public double      Width     { get; set; }
        public double      Height    { get; set; }
    }
}
