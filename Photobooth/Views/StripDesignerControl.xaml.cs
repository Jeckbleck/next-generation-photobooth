using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Photobooth.Data.Models;
using Photobooth.Services;
using Serilog;
using Photobooth.Print;
using System.Runtime.InteropServices;
using Line      = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Photobooth.Views
{
    public partial class StripDesignerControl : UserControl
    {
        private const double CanvasW     = 150;
        private const double CanvasH     = 450;
        private const int    MaxSlots    = 6;
        private const double HandleSize      = 12;
        private const double SlotHandleSize  = 22;  // photo-frame resize handles — larger touch target than text-box handles
        private const double MinSlotSize = 40;
        private const double ZoomStep    = 0.25;
        private const double ZoomMin     = 0.5;
        private const double ZoomMax     = 4.0;
        private const double GuideStep   = 7.5;    // 5 % of canvas width = one grid cell

        private double _zoom = 2.0;
        private bool   _guidelinesVisible;

        private string? _templateDir;
        private string? _eventSlug;
        private int?    _eventId;

        private readonly List<SlotControl> _slots      = new();
        private readonly List<Line>        _guideLines = new();

        // Drag state
        private SlotControl? _dragging;
        private Point        _dragOffset;
        private int          _topSlotZ = 10;   // bumped on click so overlapping frames layer to front on interaction

        // Resize state
        private SlotControl? _resizing;
        private int          _resizeHandle;   // 0=NW 1=NE 2=SW 3=SE
        private Point        _resizeOrigin;
        private Rect         _resizeStartRect;

        private readonly List<TextElementControl> _textElements = new();

        // Text drag state
        private TextElementControl? _draggingText;
        private Point                _dragOffsetText;

        // Text resize state
        private TextElementControl? _resizingText;
        private int                  _resizeHandleTextIdx;
        private Point                _resizeOriginText;
        private Rect                 _resizeStartRectText;

        // Print-space width the composer uses for a single strip (PhotostripComposer.StripW) —
        // mirrored here only to scale the designer's font-size preview proportionally.
        private const double PrintStripWidth = 620;

        // Pan state
        private bool   _panning;
        private Point  _panStart;
        private double _panHOrig, _panVOrig;
        private BitmapSource? _templateBitmapSource;
        // NEVER assign a punched/processed bitmap here. Detection correctness — and
        // immunity to the old frame-erosion bug (repeated Detect degrading the frame) —
        // depends on this always being the untouched upload, restored fresh from disk.
        private BitmapSource? _originalBitmapSource;
        private bool _autoDetectMode = true;

        // Tracks which mechanism produced the current slots, so a later Photo Overlap
        // slider drag re-runs the right detector. Purely in-memory — not persisted,
        // matching how the color path's sampled color/tolerance aren't persisted either.
        private enum SlotSource { None, Color, Transparency }
        private SlotSource _slotSource = SlotSource.None;
        private bool _eyedropperActive;
        private bool _hasColor;
        private bool _autoDetectBusy;
        private Color _sampledColor;
        private string? _backgroundColor;
        // Reset only in LoadForEvent (fresh session) and Clear_Click (hard reset).
        // Do NOT add a reset inside ClearCanvas() itself — LoadForEvent and Clear_Click
        // both call ClearCanvas(), and Clear_Click needs history wiped by its own explicit
        // call, not silently by a shared helper another caller might use differently later.
        private readonly UndoRedoStack<StripTemplateConfig> _history = new();

        public StripDesignerControl()
        {
            InitializeComponent();
            ApplyZoom();
            UpdateStatus();

            foreach (var slider in new[] { ToleranceSlider, EdgeMarginSlider, PhotoOverlapSlider })
            {
                slider.AddHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)((_, __) => RunAutoDetect()));
                slider.PreviewKeyUp += AutoDetectSlider_PreviewKeyUp;
            }
        }

        private void AutoDetectSlider_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                RunAutoDetect();
        }

        // --- Public API ----------------------------------------------------------

        public void LoadForEvent(int? eventId, string? slug, string? templateDir, string? templateImagePath)
        {
            _eventId     = eventId;
            _eventSlug   = slug;
            _templateDir = templateDir;
            _history.Clear();
            ClearCanvas();

            if (slug is null || templateDir is null)
            {
                UploadButton.IsEnabled = false;
                UpdateStatus();
                return;
            }

            UploadButton.IsEnabled = true;

            if (!string.IsNullOrEmpty(templateImagePath) && File.Exists(templateImagePath))
                LoadTemplateImage(templateImagePath);

            var originalPath = Directory.Exists(templateDir)
                ? Directory.GetFiles(templateDir, "template-original.*").FirstOrDefault()
                : null;
            if (originalPath is not null)
                LoadOriginalTemplate(originalPath);

            var jsonPath = JsonPath();
            if (!File.Exists(jsonPath)) { UpdateStatus(); return; }

            try
            {
                var config = JsonSerializer.Deserialize<StripTemplateConfig>(File.ReadAllText(jsonPath));
                if (config is null) return;

                ApplyConfig(config);
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
                if (_templateDir is not null)
                {
                    foreach (var stale in Directory.GetFiles(_templateDir, "template-original.*"))
                        File.Delete(stale);

                    var originalPath = Path.Combine(_templateDir, "template-original" + Path.GetExtension(dlg.FileName));
                    File.Copy(dlg.FileName, originalPath, overwrite: true);
                    LoadOriginalTemplate(originalPath);
                }

                LoadTemplateImage(dlg.FileName);

                if (_eventId.HasValue)
                    App.Services.GetRequiredService<IEventService>().SetPhotostripTemplatePath(_eventId.Value, dlg.FileName);

                _slotSource = SlotSource.None;
                TryAutoDetectFromTransparency();
                if (_slotSource != SlotSource.Transparency)
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

            _history.Push(CaptureConfig());

            // New slots default to a 3:2 landscape box, centered, all starting in the same
            // spot so they stack on top of one another rather than auto-arranging down the
            // canvas — the operator drags each apart afterward. The most recently added slot
            // renders on top, so it's always the one immediately grabbable.
            const double width = 0.80;
            double height = width * (CanvasW / CanvasH) * (2.0 / 3.0);

            CreateSlot(new StripSlotDefinition
            {
                Index  = nextIndex,
                X      = (1.0 - width)  / 2.0,
                Y      = (1.0 - height) / 2.0,
                Width  = width,
                Height = height,
            });
            PersistCurrentState();
            UpdateStatus();
        }

        private StripTemplateConfig CaptureConfig() => new StripTemplateConfig
        {
            Slots = _slots.Select(s => new StripSlotDefinition
            {
                Index    = s.Index,
                X        = s.Left     / CanvasW,
                Y        = s.Top      / CanvasH,
                Width    = s.Width    / CanvasW,
                Height   = s.Height   / CanvasH,
                Rotation = s.Rotation,
            }).OrderBy(s => s.Index).ToList(),
            BackgroundColor = _backgroundColor,
            TextElements = _textElements.Select(t => new TextElementDefinition
            {
                Content  = t.Content,
                X        = t.Left     / CanvasW,
                Y        = t.Top      / CanvasH,
                Width    = t.Width    / CanvasW,
                Height   = t.Height   / CanvasH,
                Color    = t.Color,
                FontSize = t.FontSize,
            }).ToList(),
        };

        private void ApplyConfig(StripTemplateConfig config)
        {
            foreach (var slot in _slots.ToList())
            {
                DesignerCanvas.Children.Remove(slot.Body);
                DesignerCanvas.Children.Remove(slot.RotateBtn);
                DesignerCanvas.Children.Remove(slot.DeleteBtn);
                foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
            }
            _slots.Clear();

            foreach (var element in _textElements.ToList())
            {
                DesignerCanvas.Children.Remove(element.Body);
                DesignerCanvas.Children.Remove(element.ShrinkBtn);
                DesignerCanvas.Children.Remove(element.GrowBtn);
                DesignerCanvas.Children.Remove(element.ColorBtn);
                DesignerCanvas.Children.Remove(element.DeleteBtn);
                foreach (var h in element.Handles) DesignerCanvas.Children.Remove(h);
            }
            _textElements.Clear();

            foreach (var def in config.Slots.OrderBy(s => s.Index))
                CreateSlot(def);

            foreach (var def in config.TextElements)
                CreateTextElement(def);

            ApplyBackgroundColor(config.BackgroundColor);
        }

        private void PersistCurrentState()
        {
            RefreshToolbarState();
            if (_templateDir is null) return;
            try
            {
                File.WriteAllText(JsonPath(),
                    JsonSerializer.Serialize(CaptureConfig(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-save strip template config for '{Slug}'", _eventSlug);
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            // Clear is a hard reset, not an undoable step — undoing a Clear would restore
            // slots/text/background from template.json while the DB's template-path
            // reference (also nulled here) stays cleared, leaving the two out of sync.
            // Wiping history instead avoids that divergence entirely.
            _history.Clear();
            ClearCanvas();
            UploadButton.IsEnabled = _eventSlug is not null;

            if (_eventId.HasValue)
                App.Services.GetRequiredService<IEventService>().SetPhotostripTemplatePath(_eventId.Value, null);

            PersistCurrentState();
            UpdateStatus();
        }

        private void BackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            var current = _backgroundColor is not null
                ? (Color)ColorConverter.ConvertFromString(_backgroundColor)
                : Colors.White;
            var popup  = new ColorPickerPopup(current) { Owner = Window.GetWindow(this) };
            var picked = popup.ShowPickedColor();
            if (picked is null) return;

            _history.Push(CaptureConfig());
            ApplyBackgroundColor($"#{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}");
            PersistCurrentState();
        }

        private void ClearBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            _history.Push(CaptureConfig());
            ApplyBackgroundColor(null);
            PersistCurrentState();
        }

        private void ApplyBackgroundColor(string? hex)
        {
            _backgroundColor = hex;
            DesignerCanvas.Background = hex is not null
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
                : Brushes.Transparent;

            BackgroundColorSwatch.Background      = DesignerCanvas.Background;
            BackgroundColorSwatch.Visibility      = hex is not null ? Visibility.Visible : Visibility.Collapsed;
            ClearBackgroundColorButton.Visibility = hex is not null ? Visibility.Visible : Visibility.Collapsed;

            RefreshToolbarState();
            UpdateStatus();
        }

        // --- Guidelines toggle ---------------------------------------------------

        private void GuidelinesToggle_Click(object sender, RoutedEventArgs e)
        {
            _guidelinesVisible = GuidelinesToggle.IsChecked == true;
            if (_guidelinesVisible)
                BuildGuidelines();
            else
                ClearGuidelines();
        }

        private void BuildGuidelines()
        {
            ClearGuidelines();
            var brush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));

            for (double x = GuideStep; x < CanvasW; x += GuideStep)
            {
                var line = new Line
                {
                    X1 = x, Y1 = 0, X2 = x, Y2 = CanvasH,
                    Stroke = brush, StrokeThickness = 0.5,
                    StrokeDashArray  = new DoubleCollection { 4, 4 },
                    IsHitTestVisible = false,
                };
                Panel.SetZIndex(line, 1);
                DesignerCanvas.Children.Add(line);
                _guideLines.Add(line);
            }

            for (double y = GuideStep; y < CanvasH; y += GuideStep)
            {
                var line = new Line
                {
                    X1 = 0, Y1 = y, X2 = CanvasW, Y2 = y,
                    Stroke = brush, StrokeThickness = 0.5,
                    StrokeDashArray  = new DoubleCollection { 4, 4 },
                    IsHitTestVisible = false,
                };
                Panel.SetZIndex(line, 1);
                DesignerCanvas.Children.Add(line);
                _guideLines.Add(line);
            }
        }

        private void ClearGuidelines()
        {
            foreach (var line in _guideLines)
                DesignerCanvas.Children.Remove(line);
            _guideLines.Clear();
        }

        // --- Slot creation -------------------------------------------------------

        private void CreateSlot(StripSlotDefinition def)
        {
            var slot = new SlotControl
            {
                Index    = def.Index,
                Left     = def.X      * CanvasW,
                Top      = def.Y      * CanvasH,
                Width    = def.Width  * CanvasW,
                Height   = def.Height * CanvasH,
                Rotation = def.Rotation,
            };

            BuildSlotVisuals(slot);
            _slots.Add(slot);
            RefreshToolbarState();
        }

        private void BuildSlotVisuals(SlotControl slot)
        {
            var accent = AccentColor();

            // Body — draggable slot rectangle
            var body = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
                BorderBrush     = Brushes.White,
                BorderThickness = new Thickness(2),
                CornerRadius    = new CornerRadius(2),
                Cursor          = Cursors.SizeAll,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(slot.Rotation),
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
            Panel.SetZIndex(body, 2);
            DesignerCanvas.Children.Add(body);
            slot.Body = body;

            // Rotate button — top-left corner of slot
            var rot = new Button
            {
                Content         = "↻",
                Width           = 22,
                Height          = 22,
                FontSize        = 13,
                Foreground      = Brushes.White,
                Background      = new SolidColorBrush(Color.FromArgb(200, 40, 80, 200)),
                BorderThickness = new Thickness(0),
                Cursor          = Cursors.Arrow,
                Padding         = new Thickness(0),
                ToolTip         = "Rotate 90°",
            };
            rot.Click += (_, _) => RotateSlot(slot);
            Panel.SetZIndex(rot, 4);
            DesignerCanvas.Children.Add(rot);
            slot.RotateBtn = rot;

            // Delete button — top-right corner of slot
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
            Panel.SetZIndex(del, 4);
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
                    Width           = SlotHandleSize,
                    Height          = SlotHandleSize,
                    Fill            = Brushes.White,
                    Stroke          = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x44)),
                    StrokeThickness = 1,
                    Cursor          = resizeCursors[h],
                };
                handle.MouseLeftButtonDown += (_, e) => { Handle_Down(slot, hi, e); e.Handled = true; };
                handle.MouseMove           += (_, e) => { Handle_Move(slot, hi, e); e.Handled = true; };
                handle.MouseLeftButtonUp   += (_, e) => { Handle_Up(slot, hi);      e.Handled = true; };
                Panel.SetZIndex(handle, 3);
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

            // Rotate/delete buttons are anchored to the frame's screen-space top corners
            // at all times — they don't follow the body's rotation, unlike the resize
            // handles below.
            Canvas.SetLeft(slot.RotateBtn, slot.Left + 3);
            Canvas.SetTop(slot.RotateBtn,  slot.Top  + 3);

            Canvas.SetLeft(slot.DeleteBtn, slot.Left + slot.Width - 24);
            Canvas.SetTop(slot.DeleteBtn,  slot.Top  + 3);

            // The body rotates in place around its own center (RenderTransform), so its
            // actual on-screen corners swing away from the unrotated Left/Top/Width/Height
            // rect whenever Rotation is 90/270. The resize handles are positioned by
            // rotating their unrotated offset-from-center through the same angle, so they
            // stay pinned to the frame's real, currently-visible corners.
            double cx = slot.Left + slot.Width  / 2.0;
            double cy = slot.Top  + slot.Height / 2.0;
            double hw = slot.Width  / 2.0;
            double hh = slot.Height / 2.0;
            var rotate = new RotateTransform(slot.Rotation);

            PlaceAtRotatedOffset(slot.Handles[0], cx, cy, rotate, -hw, -hh);
            PlaceAtRotatedOffset(slot.Handles[1], cx, cy, rotate,  hw, -hh);
            PlaceAtRotatedOffset(slot.Handles[2], cx, cy, rotate, -hw,  hh);
            PlaceAtRotatedOffset(slot.Handles[3], cx, cy, rotate,  hw,  hh);
        }

        // Places el so its center sits at (cx, cy) plus (dx, dy) rotated by `rotate` —
        // i.e. dx/dy is the element's offset from the slot's center when Rotation is 0.
        private static void PlaceAtRotatedOffset(FrameworkElement el, double cx, double cy, RotateTransform rotate, double dx, double dy)
        {
            Point p = rotate.Transform(new Point(dx, dy));
            Canvas.SetLeft(el, cx + p.X - el.Width  / 2.0);
            Canvas.SetTop (el, cy + p.Y - el.Height / 2.0);
        }

        // --- Rotation ------------------------------------------------------------

        private void RotateSlot(SlotControl slot)
        {
            _history.Push(CaptureConfig());
            slot.Rotation = (slot.Rotation + 90) % 360;
            ((RotateTransform)slot.Body.RenderTransform).Angle = slot.Rotation;
            LayoutSlot(slot);
            PersistCurrentState();
            Log.Debug("Slot {Index} rotated to {Deg}°", slot.Index, slot.Rotation);
        }

        // Raises every visual belonging to `slot` above every other slot's, so the frame
        // being interacted with is never buried under an overlapping neighbor — handy
        // now that resize handles are bigger and easier to mix up between close frames.
        private void BringSlotToFront(SlotControl slot)
        {
            _topSlotZ += 10;
            Panel.SetZIndex(slot.Body, _topSlotZ);
            foreach (var h in slot.Handles) Panel.SetZIndex(h, _topSlotZ + 1);
            Panel.SetZIndex(slot.RotateBtn, _topSlotZ + 2);
            Panel.SetZIndex(slot.DeleteBtn, _topSlotZ + 2);
        }

        // --- Drag ----------------------------------------------------------------

        private void SlotBody_Down(SlotControl slot, MouseButtonEventArgs e)
        {
            _history.Push(CaptureConfig());
            BringSlotToFront(slot);
            _dragging   = slot;
            var pos     = e.GetPosition(DesignerCanvas);
            _dragOffset = new Point(pos.X - slot.Left, pos.Y - slot.Top);
            slot.Body.CaptureMouse();
        }

        private void SlotBody_Move(SlotControl slot, MouseEventArgs e)
        {
            if (_dragging != slot || !slot.Body.IsMouseCaptured) return;
            var pos = e.GetPosition(DesignerCanvas);

            double rawX = Math.Clamp(pos.X - _dragOffset.X, 0, CanvasW - slot.Width);
            double rawY = Math.Clamp(pos.Y - _dragOffset.Y, 0, CanvasH - slot.Height);

            if (_guidelinesVisible)
            {
                rawX = Math.Clamp(Math.Round(rawX / GuideStep) * GuideStep, 0, CanvasW - slot.Width);
                rawY = Math.Clamp(Math.Round(rawY / GuideStep) * GuideStep, 0, CanvasH - slot.Height);
            }

            slot.Left = rawX;
            slot.Top  = rawY;
            LayoutSlot(slot);
        }

        private void SlotBody_Up(SlotControl slot)
        {
            _dragging = null;
            slot.Body.ReleaseMouseCapture();
            PersistCurrentState();
        }

        // --- Resize --------------------------------------------------------------

        private void Handle_Down(SlotControl slot, int handle, MouseButtonEventArgs e)
        {
            _history.Push(CaptureConfig());
            BringSlotToFront(slot);
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

            // ComputeResizedRect works in the slot's own unrotated coordinate space, so
            // the raw screen-space drag delta needs to be rotated back through the
            // frame's current angle before it means anything to it — otherwise dragging
            // straight down on a 90°-rotated frame would (correctly, in screen terms, but
            // uselessly) try to grow it sideways instead of taller.
            Point local = new RotateTransform(-slot.Rotation).Transform(
                new Point(pos.X - _resizeOrigin.X, pos.Y - _resizeOrigin.Y));

            var rect = ComputeResizedRect(
                _resizeStartRect, handle, local.X, local.Y, MinSlotSize, CanvasW, CanvasH, GuideStep, _guidelinesVisible);

            slot.Left   = rect.Left;
            slot.Top    = rect.Top;
            slot.Width  = rect.Width;
            slot.Height = rect.Height;
            LayoutSlot(slot);
        }

        private void Handle_Up(SlotControl slot, int handle)
        {
            _resizing = null;
            slot.Handles[handle].ReleaseMouseCapture();
            PersistCurrentState();
        }

        // --- Text element creation ------------------------------------------------

        private void AddText_Click(object sender, RoutedEventArgs e)
        {
            _history.Push(CaptureConfig());
            CreateTextElement(new TextElementDefinition
            {
                Content  = "Text",
                X        = 0.10,
                Y        = 0.40,
                Width    = 0.80,
                Height   = 0.12,
                Color    = "#FFFFFF",
                FontSize = 24,
            });
            PersistCurrentState();
            UpdateStatus();
        }

        private void CreateTextElement(TextElementDefinition def)
        {
            var element = new TextElementControl
            {
                Content  = def.Content,
                Color    = def.Color,
                FontSize = def.FontSize,
                Left     = def.X      * CanvasW,
                Top      = def.Y      * CanvasH,
                Width    = def.Width  * CanvasW,
                Height   = def.Height * CanvasH,
            };

            BuildTextVisuals(element);
            _textElements.Add(element);
            RefreshToolbarState();
        }

        private void BuildTextVisuals(TextElementControl element)
        {
            var label = new TextBlock
            {
                Text                = element.Content,
                FontSize            = Math.Max(1, element.FontSize * CanvasW / PrintStripWidth),
                FontWeight          = FontWeights.Bold,
                Foreground          = new SolidColorBrush((Color)ColorConverter.ConvertFromString(element.Color)),
                TextAlignment       = TextAlignment.Center,
                TextWrapping        = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };
            element.Label = label;

            var body = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                BorderBrush     = Brushes.White,
                BorderThickness = new Thickness(2),
                CornerRadius    = new CornerRadius(2),
                Cursor          = Cursors.SizeAll,
                Child           = label,
            };
            body.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) { BeginEditText(element); e.Handled = true; return; }
                TextBody_Down(element, e);
                e.Handled = true;
            };
            body.MouseMove         += (_, e) => TextBody_Move(element, e);
            body.MouseLeftButtonUp += (_, e) => { TextBody_Up(element); e.Handled = true; };
            Panel.SetZIndex(body, 2);
            DesignerCanvas.Children.Add(body);
            element.Body = body;

            var shrink = new Button
            {
                Content = "A−", Width = 26, Height = 22, FontSize = 11,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 40, 80, 200)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Arrow, Padding = new Thickness(0),
                ToolTip = "Decrease font size",
            };
            shrink.Click += (_, _) => ChangeTextFontSize(element, -2);
            Panel.SetZIndex(shrink, 4);
            DesignerCanvas.Children.Add(shrink);
            element.ShrinkBtn = shrink;

            var grow = new Button
            {
                Content = "A+", Width = 26, Height = 22, FontSize = 11,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 40, 80, 200)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Arrow, Padding = new Thickness(0),
                ToolTip = "Increase font size",
            };
            grow.Click += (_, _) => ChangeTextFontSize(element, 2);
            Panel.SetZIndex(grow, 4);
            DesignerCanvas.Children.Add(grow);
            element.GrowBtn = grow;

            var colorBtn = new Button
            {
                Width = 22, Height = 22,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(element.Color)),
                BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
                Cursor = Cursors.Arrow, Padding = new Thickness(0),
                ToolTip = "Text color",
            };
            colorBtn.Click += (_, _) => PickTextColor(element);
            Panel.SetZIndex(colorBtn, 4);
            DesignerCanvas.Children.Add(colorBtn);
            element.ColorBtn = colorBtn;

            var del = new Button
            {
                Content = "×", Width = 22, Height = 22, FontSize = 15,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(200, 180, 40, 40)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Arrow, Padding = new Thickness(0),
            };
            del.Click += (_, _) => RemoveTextElement(element);
            Panel.SetZIndex(del, 4);
            DesignerCanvas.Children.Add(del);
            element.DeleteBtn = del;

            var resizeCursors = new[] { Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNESW, Cursors.SizeNWSE };
            element.Handles = new Rectangle[4];
            for (int h = 0; h < 4; h++)
            {
                int hi = h;
                var handle = new Rectangle
                {
                    Width = HandleSize, Height = HandleSize,
                    Fill = Brushes.White,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x44)),
                    StrokeThickness = 1,
                    Cursor = resizeCursors[h],
                };
                handle.MouseLeftButtonDown += (_, e) => { TextHandle_Down(element, hi, e); e.Handled = true; };
                handle.MouseMove           += (_, e) => { TextHandle_Move(element, hi, e); e.Handled = true; };
                handle.MouseLeftButtonUp   += (_, e) => { TextHandle_Up(element, hi);      e.Handled = true; };
                Panel.SetZIndex(handle, 3);
                DesignerCanvas.Children.Add(handle);
                element.Handles[h] = handle;
            }

            LayoutTextElement(element);
        }

        private void LayoutTextElement(TextElementControl element)
        {
            Canvas.SetLeft(element.Body, element.Left);
            Canvas.SetTop(element.Body,  element.Top);
            element.Body.Width  = element.Width;
            element.Body.Height = element.Height;

            Canvas.SetLeft(element.ShrinkBtn, element.Left + 3);
            Canvas.SetTop(element.ShrinkBtn,  element.Top  + 3);

            Canvas.SetLeft(element.GrowBtn, element.Left + 3 + 28);
            Canvas.SetTop(element.GrowBtn,  element.Top  + 3);

            Canvas.SetLeft(element.ColorBtn, element.Left + element.Width - 48);
            Canvas.SetTop(element.ColorBtn,  element.Top  + 3);

            Canvas.SetLeft(element.DeleteBtn, element.Left + element.Width - 24);
            Canvas.SetTop(element.DeleteBtn,  element.Top  + 3);

            double hs = HandleSize / 2.0;
            Canvas.SetLeft(element.Handles[0], element.Left - hs);
            Canvas.SetTop(element.Handles[0],  element.Top  - hs);
            Canvas.SetLeft(element.Handles[1], element.Left + element.Width  - hs);
            Canvas.SetTop(element.Handles[1],  element.Top  - hs);
            Canvas.SetLeft(element.Handles[2], element.Left - hs);
            Canvas.SetTop(element.Handles[2],  element.Top  + element.Height - hs);
            Canvas.SetLeft(element.Handles[3], element.Left + element.Width  - hs);
            Canvas.SetTop(element.Handles[3],  element.Top  + element.Height - hs);
        }

        // --- Text drag ---------------------------------------------------------

        private void TextBody_Down(TextElementControl element, MouseButtonEventArgs e)
        {
            _history.Push(CaptureConfig());
            _draggingText   = element;
            var pos         = e.GetPosition(DesignerCanvas);
            _dragOffsetText = new Point(pos.X - element.Left, pos.Y - element.Top);
            element.Body.CaptureMouse();
        }

        private void TextBody_Move(TextElementControl element, MouseEventArgs e)
        {
            if (_draggingText != element || !element.Body.IsMouseCaptured) return;
            var pos = e.GetPosition(DesignerCanvas);

            double rawX = Math.Clamp(pos.X - _dragOffsetText.X, 0, CanvasW - element.Width);
            double rawY = Math.Clamp(pos.Y - _dragOffsetText.Y, 0, CanvasH - element.Height);

            if (_guidelinesVisible)
            {
                rawX = Math.Clamp(Math.Round(rawX / GuideStep) * GuideStep, 0, CanvasW - element.Width);
                rawY = Math.Clamp(Math.Round(rawY / GuideStep) * GuideStep, 0, CanvasH - element.Height);
            }

            element.Left = rawX;
            element.Top  = rawY;
            LayoutTextElement(element);
        }

        private void TextBody_Up(TextElementControl element)
        {
            _draggingText = null;
            element.Body.ReleaseMouseCapture();
            PersistCurrentState();
        }

        // --- Text resize ---------------------------------------------------------

        private void TextHandle_Down(TextElementControl element, int handle, MouseButtonEventArgs e)
        {
            _history.Push(CaptureConfig());
            _resizingText        = element;
            _resizeHandleTextIdx = handle;
            _resizeOriginText    = e.GetPosition(DesignerCanvas);
            _resizeStartRectText = new Rect(element.Left, element.Top, element.Width, element.Height);
            element.Handles[handle].CaptureMouse();
        }

        private void TextHandle_Move(TextElementControl element, int handle, MouseEventArgs e)
        {
            if (_resizingText != element || !element.Handles[handle].IsMouseCaptured) return;

            var pos = e.GetPosition(DesignerCanvas);
            double dx = pos.X - _resizeOriginText.X;
            double dy = pos.Y - _resizeOriginText.Y;

            var rect = ComputeResizedRect(
                _resizeStartRectText, handle, dx, dy, MinSlotSize, CanvasW, CanvasH, GuideStep, _guidelinesVisible);

            element.Left   = rect.Left;
            element.Top    = rect.Top;
            element.Width  = rect.Width;
            element.Height = rect.Height;
            LayoutTextElement(element);
        }

        private void TextHandle_Up(TextElementControl element, int handle)
        {
            _resizingText = null;
            element.Handles[handle].ReleaseMouseCapture();
            PersistCurrentState();
        }

        // --- Shared resize math (used by both slot and text-element handles) -----

        private static Rect ComputeResizedRect(
            Rect start, int handle, double dx, double dy,
            double minSize, double canvasW, double canvasH, double guideStep, bool snapToGuides)
        {
            double l = start.Left, t = start.Top, ri = start.Right, b = start.Bottom;

            switch (handle)
            {
                case 0: l  = Math.Min(start.Left  + dx, start.Right  - minSize); t = Math.Min(start.Top    + dy, start.Bottom - minSize); break;
                case 1: ri = Math.Max(start.Right + dx, start.Left   + minSize); t = Math.Min(start.Top    + dy, start.Bottom - minSize); break;
                case 2: l  = Math.Min(start.Left  + dx, start.Right  - minSize); b = Math.Max(start.Bottom + dy, start.Top    + minSize); break;
                case 3: ri = Math.Max(start.Right + dx, start.Left   + minSize); b = Math.Max(start.Bottom + dy, start.Top    + minSize); break;
            }

            if (snapToGuides)
            {
                switch (handle)
                {
                    case 0: l  = Math.Round(l  / guideStep) * guideStep; t = Math.Round(t / guideStep) * guideStep; break;
                    case 1: ri = Math.Round(ri / guideStep) * guideStep; t = Math.Round(t / guideStep) * guideStep; break;
                    case 2: l  = Math.Round(l  / guideStep) * guideStep; b = Math.Round(b / guideStep) * guideStep; break;
                    case 3: ri = Math.Round(ri / guideStep) * guideStep; b = Math.Round(b / guideStep) * guideStep; break;
                }
                if (ri - l < minSize) { if (handle is 0 or 2) l = ri - minSize; else ri = l + minSize; }
                if (b  - t < minSize) { if (handle is 0 or 1) t = b  - minSize; else b  = t + minSize; }
            }

            double left   = Math.Max(0, l);
            double top    = Math.Max(0, t);
            double width  = Math.Min(canvasW, ri) - left;
            double height = Math.Min(canvasH, b)  - top;
            return new Rect(left, top, width, height);
        }

        // --- Text content / color / size / removal --------------------------------

        private void BeginEditText(TextElementControl element)
        {
            _history.Push(CaptureConfig());

            var textBox = new TextBox
            {
                Text          = element.Content,
                FontSize      = element.Label.FontSize,
                FontWeight    = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                TextWrapping  = TextWrapping.Wrap,
                AcceptsReturn = false,
                Background    = Brushes.White,
                Foreground    = Brushes.Black,
            };

            void Commit()
            {
                element.Content    = string.IsNullOrWhiteSpace(textBox.Text) ? "Text" : textBox.Text;
                element.Label.Text = element.Content;
                element.Body.Child = element.Label;
                PersistCurrentState();
            }

            textBox.LostFocus += (_, _) => Commit();
            textBox.KeyDown   += (_, e) =>
            {
                if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); }
            };

            element.Body.Child = textBox;
            textBox.Focus();
            textBox.SelectAll();
        }

        private void ChangeTextFontSize(TextElementControl element, double delta)
        {
            _history.Push(CaptureConfig());
            element.FontSize       = Math.Clamp(element.FontSize + delta, 8, 96);
            element.Label.FontSize = Math.Max(1, element.FontSize * CanvasW / PrintStripWidth);
            PersistCurrentState();
        }

        private void PickTextColor(TextElementControl element)
        {
            var current = (Color)ColorConverter.ConvertFromString(element.Color);
            var popup   = new ColorPickerPopup(current) { Owner = Window.GetWindow(this) };
            var picked  = popup.ShowPickedColor();
            if (picked is null) return;

            _history.Push(CaptureConfig());
            element.Color                = $"#{picked.Value.R:X2}{picked.Value.G:X2}{picked.Value.B:X2}";
            element.Label.Foreground     = new SolidColorBrush(picked.Value);
            element.ColorBtn.Background  = new SolidColorBrush(picked.Value);
            PersistCurrentState();
        }

        private void RemoveTextElement(TextElementControl element)
        {
            _history.Push(CaptureConfig());
            DesignerCanvas.Children.Remove(element.Body);
            DesignerCanvas.Children.Remove(element.ShrinkBtn);
            DesignerCanvas.Children.Remove(element.GrowBtn);
            DesignerCanvas.Children.Remove(element.ColorBtn);
            DesignerCanvas.Children.Remove(element.DeleteBtn);
            foreach (var h in element.Handles) DesignerCanvas.Children.Remove(h);
            _textElements.Remove(element);
            RefreshToolbarState();
            PersistCurrentState();
            UpdateStatus();
        }

        // --- Zoom ----------------------------------------------------------------

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoom = Math.Min(_zoom + ZoomStep, ZoomMax);
            ApplyZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoom = Math.Max(_zoom - ZoomStep, ZoomMin);
            ApplyZoom();
        }

        private void DesignerCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            e.Handled = true;
            _zoom = e.Delta > 0
                ? Math.Min(_zoom + ZoomStep, ZoomMax)
                : Math.Max(_zoom - ZoomStep, ZoomMin);
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            ZoomTransform.ScaleX = _zoom;
            ZoomTransform.ScaleY = _zoom;
            ZoomLabel.Text = $"{_zoom:P0}";
        }

        // --- Canvas scroll / pan -------------------------------------------------

        private void CanvasScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            CanvasContainer.MinWidth  = CanvasScroller.ViewportWidth;
            CanvasContainer.MinHeight = CanvasScroller.ViewportHeight;
        }

        private void CanvasScroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Middle-mouse-drag always pans. A touch/left-click also pans, but only when
            // it lands on empty canvas background rather than a slot/handle/button —
            // PanningMode stays "None" on this ScrollViewer (see the "added panning mode
            // in main view" commit) specifically so a touch-drag on a frame moves the
            // frame instead of fighting WPF's built-in manipulation panning; this recreates
            // just the "scroll the zoomed canvas with a finger" part on top of that, without
            // reopening that conflict.
            bool isBackgroundTap = e.ChangedButton == MouseButton.Left
                && !_eyedropperActive
                && ReferenceEquals(e.OriginalSource, DesignerCanvas);
            if (e.ChangedButton != MouseButton.Middle && !isBackgroundTap) return;

            _panning  = true;
            _panStart = e.GetPosition(CanvasScroller);
            _panHOrig = CanvasScroller.HorizontalOffset;
            _panVOrig = CanvasScroller.VerticalOffset;
            CanvasScroller.CaptureMouse();
            CanvasScroller.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void CanvasScroller_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning || !CanvasScroller.IsMouseCaptured) return;
            var pos = e.GetPosition(CanvasScroller);
            CanvasScroller.ScrollToHorizontalOffset(_panHOrig - (pos.X - _panStart.X));
            CanvasScroller.ScrollToVerticalOffset  (_panVOrig - (pos.Y - _panStart.Y));
        }

        private void CanvasScroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_panning) return;
            if (e.ChangedButton != MouseButton.Middle && e.ChangedButton != MouseButton.Left) return;
            _panning = false;
            CanvasScroller.ReleaseMouseCapture();
            CanvasScroller.Cursor = null;
        }

        // --- Helpers -------------------------------------------------------------

        private void RemoveSlot(SlotControl slot)
        {
            _history.Push(CaptureConfig());
            DesignerCanvas.Children.Remove(slot.Body);
            DesignerCanvas.Children.Remove(slot.RotateBtn);
            DesignerCanvas.Children.Remove(slot.DeleteBtn);
            foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
            _slots.Remove(slot);
            RefreshToolbarState();
            PersistCurrentState();
            UpdateStatus();
        }

        private static BitmapImage LoadBitmapFile(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource     = new Uri(path);
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void LoadTemplateImage(string path)
        {
            var bmp = LoadBitmapFile(path);
            TemplateImage.Source  = bmp;
            _templateBitmapSource = bmp;
            RefreshToolbarState();
        }

        private void LoadOriginalTemplate(string path)
        {
            _originalBitmapSource = LoadBitmapFile(path);
        }

        private void ClearCanvas()
        {
            foreach (var slot in _slots.ToList())
            {
                DesignerCanvas.Children.Remove(slot.Body);
                DesignerCanvas.Children.Remove(slot.RotateBtn);
                DesignerCanvas.Children.Remove(slot.DeleteBtn);
                foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
            }
            _slots.Clear();

            foreach (var element in _textElements.ToList())
            {
                DesignerCanvas.Children.Remove(element.Body);
                DesignerCanvas.Children.Remove(element.ShrinkBtn);
                DesignerCanvas.Children.Remove(element.GrowBtn);
                DesignerCanvas.Children.Remove(element.ColorBtn);
                DesignerCanvas.Children.Remove(element.DeleteBtn);
                foreach (var h in element.Handles) DesignerCanvas.Children.Remove(h);
            }
            _textElements.Clear();

            TemplateImage.Source  = null;
            _templateBitmapSource = null;
            _originalBitmapSource = null;
            _hasColor = false;
            _slotSource = SlotSource.None;
            _eyedropperActive = false;
            ColorSwatch.Background = System.Windows.Media.Brushes.Transparent;
            ColorSwatch.Visibility = Visibility.Collapsed;
            DesignerCanvas.Cursor  = Cursors.Arrow;
            ApplyBackgroundColor(null);
            RefreshToolbarState();
        }

        private void RefreshToolbarState()
        {
            if (EyedropperButton is null) return;   // fires during InitializeComponent
            bool hasTemplate      = TemplateImage.Source is not null;
            bool eventSelected    = _eventSlug is not null;
            bool hasContent       = hasTemplate || _slots.Count > 0 || _backgroundColor is not null || _textElements.Count > 0;
            bool colorPicked      = hasTemplate && _autoDetectMode && _hasColor;
            bool transparencyMode = _slotSource == SlotSource.Transparency;

            AddSlotButton.IsEnabled         = eventSelected && _slots.Count < MaxSlots;
            AddTextButton.IsEnabled         = eventSelected;
            BackgroundColorButton.IsEnabled = eventSelected;
            UndoButton.IsEnabled            = _history.CanUndo;
            RedoButton.IsEnabled            = _history.CanRedo;
            ClearButton.IsEnabled           = hasContent;

            // Color-eyedropper controls don't apply when slots came from the template's own
            // transparency — hidden instead of just disabled, since there's nothing for the
            // operator to do with them in that mode.
            var colorControlsVisibility = transparencyMode ? Visibility.Collapsed : Visibility.Visible;
            EyedropperButton.Visibility = colorControlsVisibility;
            ToleranceRow.Visibility     = colorControlsVisibility;
            ToleranceSlider.Visibility  = colorControlsVisibility;
            EdgeMarginRow.Visibility    = colorControlsVisibility;
            EdgeMarginSlider.Visibility = colorControlsVisibility;

            EyedropperButton.IsEnabled      = hasTemplate && _autoDetectMode && !_autoDetectBusy;
            ToleranceSlider.IsEnabled       = colorPicked && !_autoDetectBusy;
            EdgeMarginSlider.IsEnabled      = colorPicked && !_autoDetectBusy;
            PhotoOverlapSlider.IsEnabled    = (colorPicked || transparencyMode) && !_autoDetectBusy;
        }

        private void UpdateStatus()
        {
            if (_eventSlug is null)
            {
                StatusText.Text = "Select an event to use the strip designer.";
                return;
            }
            if (_slots.Count == 0)
            {
                if (_autoDetectMode && TemplateImage.Source is null)
                {
                    StatusText.Text = "Upload a template PNG to auto-detect slots, or switch to Manual to add slots without one.";
                    return;
                }
                StatusText.Text = _autoDetectMode
                    ? "Pick a color with the eyedropper — slots detect automatically."
                    : "Click '+ Add Slot' to place photo slots manually.";
                return;
            }
            StatusText.Text = _slots.Count < MaxSlots
                ? $"{_slots.Count} of {MaxSlots} slots placed — add more as needed."
                : $"All {MaxSlots} slots placed. ↻ to rotate. Drag to reposition, corners to resize.";
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

        // --- Eyedropper -----------------------------------------------------------

        private void Eyedropper_Click(object sender, RoutedEventArgs e)
        {
            _eyedropperActive = true;
            DesignerCanvas.Cursor = Cursors.Cross;
        }

        private void DeactivateEyedropper()
        {
            _eyedropperActive = false;
            DesignerCanvas.Cursor = Cursors.Arrow;
        }

        private void DesignerCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_eyedropperActive) return;
            e.Handled = true;
            DeactivateEyedropper();

            var pos = e.GetPosition(DesignerCanvas);
            _sampledColor = SamplePixel(pos);
            _hasColor    = true;
            _slotSource  = SlotSource.Color;

            ColorSwatch.Background  = new SolidColorBrush(_sampledColor);
            ColorSwatch.Visibility  = Visibility.Visible;
            RefreshToolbarState();
            RunAutoDetect();
        }

        private Color SamplePixel(Point canvasPos)
        {
            if (_originalBitmapSource == null) return Colors.Transparent;

            // Map canvas coords (150×450) to BitmapSource pixel coords
            double scaleX = _originalBitmapSource.PixelWidth  / DesignerCanvas.ActualWidth;
            double scaleY = _originalBitmapSource.PixelHeight / DesignerCanvas.ActualHeight;
            int px = (int)(canvasPos.X * scaleX);
            int py = (int)(canvasPos.Y * scaleY);
            px = Math.Max(0, Math.Min(px, _originalBitmapSource.PixelWidth  - 1));
            py = Math.Max(0, Math.Min(py, _originalBitmapSource.PixelHeight - 1));

            // Read one pixel — FormatConvertedBitmap ensures Bgra32
            var conv = new FormatConvertedBitmap(_originalBitmapSource,
                                                  PixelFormats.Bgra32, null, 0);
            byte[] pixel = new byte[4];
            conv.CopyPixels(new Int32Rect(px, py, 1, 1), pixel, 4, 0);
            // Bgra32: [B, G, R, A]
            return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }

        // --- Auto-detect -----------------------------------------------------------

        private static System.Drawing.Bitmap ToBitmap(BitmapSource source)
        {
            var conv = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[stride * h];
            conv.CopyPixels(pixels, stride, 0);

            var bmp = new System.Drawing.Bitmap(w, h,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                                   System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                   System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, bd.Scan0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return bmp;
        }

        private void ToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ToleranceLabel != null)
                ToleranceLabel.Text = ((int)ToleranceSlider.Value).ToString();
        }

        private void EdgeMarginSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (EdgeMarginLabel != null)
                EdgeMarginLabel.Text = ((int)EdgeMarginSlider.Value).ToString();
        }

        private void PhotoOverlapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PhotoOverlapLabel != null)
                PhotoOverlapLabel.Text = ((int)PhotoOverlapSlider.Value).ToString();
        }

        private void RunAutoDetect()
        {
            if (_autoDetectBusy) return;

            if (_slotSource == SlotSource.Transparency)
            {
                _history.Push(CaptureConfig());
                _autoDetectBusy = true;
                RefreshToolbarState();
                try
                {
                    RedetectFromTransparency();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to re-run transparency auto-detect for '{Slug}'", _eventSlug);
                }
                finally
                {
                    _autoDetectBusy = false;
                    RefreshToolbarState();
                    UpdateStatus();
                    PersistCurrentState();
                }
                return;
            }

            if (!_hasColor) return;

            if (_originalBitmapSource == null)
            {
                StatusText.Text = "This template predates live auto-detect — re-upload it to enable adjustable slot detection.";
                return;
            }

            _history.Push(CaptureConfig());
            _autoDetectBusy = true;
            RefreshToolbarState();
            try
            {
                // Remove existing slot visuals only — do NOT call ClearCanvas()
                foreach (var slot in _slots.ToList())
                {
                    DesignerCanvas.Children.Remove(slot.Body);
                    DesignerCanvas.Children.Remove(slot.RotateBtn);
                    DesignerCanvas.Children.Remove(slot.DeleteBtn);
                    foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
                }
                _slots.Clear();

                int tolerance    = (int)ToleranceSlider.Value;
                int edgeMargin   = (int)EdgeMarginSlider.Value;
                int photoOverlap = (int)PhotoOverlapSlider.Value;
                edgeMargin       = Math.Min(edgeMargin, photoOverlap);
                using var bmp = ToBitmap(_originalBitmapSource);
                var drawingColor = System.Drawing.Color.FromArgb(
                    _sampledColor.A, _sampledColor.R, _sampledColor.G, _sampledColor.B);
                var defs = TemplateSegmenter.Detect(bmp, drawingColor, tolerance, expandPixels: photoOverlap);

                foreach (var def in defs)
                    CreateSlot(def);

                // The detected color marks a photo window, but the uploaded frame is a flat,
                // fully-opaque PNG — drawn on top of the photos at print time it would hide
                // them completely. Punch the matched pixels to real transparency and save that
                // as the frame actually used for preview and printing, instead of the raw upload.
                if (_templateDir is not null)
                {
                    using var punched = TemplateSegmenter.PunchTransparency(bmp, drawingColor, tolerance, dilatePixels: edgeMargin);
                    var punchedPath = Path.Combine(_templateDir, "frame-detected.png");
                    punched.Save(punchedPath, System.Drawing.Imaging.ImageFormat.Png);

                    LoadTemplateImage(punchedPath);
                    if (_eventId.HasValue)
                        App.Services.GetRequiredService<IEventService>().SetPhotostripTemplatePath(_eventId.Value, punchedPath);

                    Log.Information("Punched transparent photo windows into strip template for '{Slug}'", _eventSlug);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-detect/punch strip template for '{Slug}'", _eventSlug);
            }
            finally
            {
                _autoDetectBusy = false;
                RefreshToolbarState();
                UpdateStatus();
                PersistCurrentState();
            }
        }

        // Redetects slots from the template's own transparency and rebuilds the canvas.
        // Returns the number of photo windows found — 0 means this template has no
        // qualifying transparent region, which the caller uses to decide whether to fall
        // back to eyedropper color-picking.
        private int RedetectFromTransparency()
        {
            if (_originalBitmapSource is null) return 0;

            int photoOverlap = (int)PhotoOverlapSlider.Value;
            using var bmp = ToBitmap(_originalBitmapSource);
            var defs = TemplateSegmenter.DetectFromTransparency(bmp, expandPixels: photoOverlap);
            if (defs.Count == 0) return 0;

            foreach (var slot in _slots.ToList())
            {
                DesignerCanvas.Children.Remove(slot.Body);
                DesignerCanvas.Children.Remove(slot.RotateBtn);
                DesignerCanvas.Children.Remove(slot.DeleteBtn);
                foreach (var h in slot.Handles) DesignerCanvas.Children.Remove(h);
            }
            _slots.Clear();

            foreach (var def in defs)
                CreateSlot(def);

            return defs.Count;
        }

        // Called right after a template upload, before the operator has done anything else.
        // If the upload already has its photo windows punched out as real transparency,
        // place slots straight from those holes and skip the eyedropper step entirely.
        private void TryAutoDetectFromTransparency()
        {
            if (!_autoDetectMode) return;
            if (RedetectFromTransparency() == 0) return;

            _slotSource = SlotSource.Transparency;
            PersistCurrentState();
            StatusText.Text = "Photo windows detected automatically from the template's transparency.";
            Log.Information("Auto-detected {Count} photo window(s) from template transparency for '{Slug}'",
                _slots.Count, _eventSlug);
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (!_history.CanUndo) return;
            var restored = _history.Undo(CaptureConfig());
            ApplyConfig(restored);
            PersistCurrentState();
            RefreshToolbarState();
            UpdateStatus();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            if (!_history.CanRedo) return;
            var restored = _history.Redo(CaptureConfig());
            ApplyConfig(restored);
            PersistCurrentState();
            RefreshToolbarState();
            UpdateStatus();
        }

        private void SlotModeTab_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoDetectPanel is null) return;   // fires during InitializeComponent before panels exist
            _autoDetectMode = AutoDetectTab.IsChecked == true;
            AutoDetectPanel.Visibility = _autoDetectMode ? Visibility.Visible : Visibility.Collapsed;
            ManualPanel.Visibility     = _autoDetectMode ? Visibility.Collapsed : Visibility.Visible;
            RefreshToolbarState();
            UpdateStatus();
        }
    }

    internal class SlotControl
    {
        public int         Index     { get; set; }
        public Border      Body      { get; set; } = null!;
        public Button      RotateBtn { get; set; } = null!;
        public Button      DeleteBtn { get; set; } = null!;
        public Rectangle[] Handles   { get; set; } = Array.Empty<Rectangle>();
        public double      Left      { get; set; }
        public double      Top       { get; set; }
        public double      Width     { get; set; }
        public double      Height    { get; set; }
        public int         Rotation  { get; set; }
    }

    internal class TextElementControl
    {
        public string      Content   { get; set; } = "Text";
        public string      Color     { get; set; } = "#FFFFFF";
        public double      FontSize  { get; set; } = 24;
        public Border      Body      { get; set; } = null!;
        public TextBlock   Label     { get; set; } = null!;
        public Button      ShrinkBtn { get; set; } = null!;
        public Button      GrowBtn   { get; set; } = null!;
        public Button      ColorBtn  { get; set; } = null!;
        public Button      DeleteBtn { get; set; } = null!;
        public Rectangle[] Handles   { get; set; } = Array.Empty<Rectangle>();
        public double      Left      { get; set; }
        public double      Top       { get; set; }
        public double      Width     { get; set; }
        public double      Height    { get; set; }
    }
}
