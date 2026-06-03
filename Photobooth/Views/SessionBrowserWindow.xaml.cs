using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Helpers;
using Photobooth.Print;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views
{
    public partial class SessionBrowserWindow : Window
    {
        private readonly int  _eventId;
        private readonly int? _openSessionId;
        private int           _selectedSessionId;
        private List<string>  _selectedPhotoPaths = new();
        private bool          _showingEnhanced    = false;

        private sealed class SessionCardItem
        {
            public int    SessionId  { get; init; }
            public string Label      { get; init; } = "";
            public string Date       { get; init; } = "";
            public string PhotoLabel { get; init; } = "";
            public List<System.Windows.Media.ImageSource> Thumbnails    { get; init; } = new();
            public List<string>                           PhotoPaths    { get; init; } = new();
            public List<VariantRowItem>                   VariantRows   { get; init; } = new();
        }

        private sealed class VariantRowItem
        {
            public string StyleId   { get; init; } = "";
            public string StyleName { get; init; } = "";
            public List<System.Windows.Media.ImageSource> Thumbs { get; init; } = new();
            public List<string>                           Paths  { get; init; } = new();
        }

        public SessionBrowserWindow(int eventId, int? openSessionId = null)
        {
            InitializeComponent();
            _eventId       = eventId;
            _openSessionId = openSessionId;
            Loaded        += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _ = LoadSessionsAsync();
        }

        private async Task LoadSessionsAsync()
        {
            // DB query + all thumbnail decoding on a background thread.
            // BitmapHelper.LoadFromFile freezes each BitmapImage before returning,
            // so the frozen sources cross the thread boundary safely.
            List<SessionCardItem>? items = await Task.Run(() =>
            {
                var sessions = App.Events.GetSessionsWithPhotos(_eventId);
                if (sessions.Count == 0) return null;

                int total = sessions.Count;
                return sessions.Select((s, idx) =>
                {
                    var ordered = s.Photos.OrderBy(p => p.Sequence).ToList();

                    var paths = ordered
                        .Where(p => p.FilePath != null && File.Exists(p.FilePath))
                        .Select(p => p.FilePath!)
                        .ToList();

                    var thumbs = paths.Select(path =>
                    {
                        try { return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(path, fallbackDecodeWidth: 150); }
                        catch { return null; }
                    })
                    .Where(b => b != null)
                    .Cast<System.Windows.Media.ImageSource>()
                    .ToList();

                    // Collect distinct styles across all photos, ordered by first appearance
                    var styleIds = ordered
                        .SelectMany(p => p.EnhancedVariants)
                        .GroupBy(v => v.StyleId)
                        .Select(g => (g.Key, g.First().StyleName))
                        .ToList();

                    var variantRows = styleIds.Select(style =>
                    {
                        var variantPaths = ordered
                            .Select(p => p.EnhancedVariants
                                .FirstOrDefault(v => v.StyleId == style.Key)?.FilePath)
                            .Where(f => f != null && File.Exists(f))
                            .Select(f => f!)
                            .ToList();

                        var variantThumbs = variantPaths.Select(path =>
                        {
                            try { return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(path, fallbackDecodeWidth: 150); }
                            catch { return null; }
                        })
                        .Where(b => b != null)
                        .Cast<System.Windows.Media.ImageSource>()
                        .ToList();

                        return new VariantRowItem
                        {
                            StyleId   = style.Key,
                            StyleName = style.StyleName,
                            Thumbs    = variantThumbs,
                            Paths     = variantPaths,
                        };
                    }).ToList();

                    int n = s.Photos.Count;
                    return new SessionCardItem
                    {
                        SessionId   = s.Id,
                        Label       = $"Session {total - idx}",
                        Date        = s.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
                        PhotoLabel  = $"{n} photo{(n == 1 ? "" : "s")}",
                        Thumbnails  = thumbs,
                        PhotoPaths  = paths,
                        VariantRows = variantRows,
                    };
                }).ToList();
            });

            if (items is null)
            {
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            SessionCardsControl.ItemsSource = items;

            // Auto-open a specific session if the caller requested it
            if (_openSessionId.HasValue)
            {
                var target = items.FirstOrDefault(c => c.SessionId == _openSessionId.Value);
                if (target != null) OpenDetail(target);
            }
        }

        private void SessionCard_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not SessionCardItem item) return;
            OpenDetail(item);
        }

        private void OpenDetail(SessionCardItem item)
        {
            _selectedSessionId  = item.SessionId;
            _selectedPhotoPaths = item.PhotoPaths;
            _showingEnhanced    = false;

            DetailLabel.Text = item.Label;
            DetailDate.Text  = item.Date;

            var ev    = App.Events.GetById(_eventId);
            var limit = ev?.PrintLimitPerSession;
            if (limit.HasValue && App.Events.GetEventPrintCount(_eventId) >= limit.Value)
            {
                PrintButton.IsEnabled = false;
                PrintStatusText.Text  = $"Print limit of {limit.Value} reached for this event.";
            }
            else
            {
                PrintButton.IsEnabled = true;
                PrintStatusText.Text  = string.Empty;
            }

            LoadDetailPhotos(_selectedPhotoPaths);

            LoadEnhancedTray(item.VariantRows);

            SessionsScrollViewer.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility          = Visibility.Visible;
            BackButton.Visibility           = Visibility.Visible;
            HeaderTitle.Visibility          = Visibility.Collapsed;

            Log.Debug("Session browser opened detail for session {Id}", item.SessionId);
        }

        private void LoadEnhancedTray(List<VariantRowItem> rows)
        {
            VariantsList.SelectionChanged -= VariantsList_SelectionChanged;
            VariantsList.ItemsSource       = rows;
            VariantsList.SelectedItem      = null;
            VariantsList.SelectionChanged += VariantsList_SelectionChanged;

            ShowOriginalsButton.Visibility = Visibility.Collapsed;
            EnhancedTray.BorderBrush       = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x2F, 0x4A));
            EnhancedTray.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void VariantsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VariantsList.SelectedItem is not VariantRowItem row) return;
            SetEnhancedView(row.Paths);
        }

        private void ShowOriginalsButton_Click(object sender, RoutedEventArgs e)
        {
            _showingEnhanced = false;
            VariantsList.SelectionChanged -= VariantsList_SelectionChanged;
            VariantsList.SelectedItem      = null;
            VariantsList.SelectionChanged += VariantsList_SelectionChanged;

            LoadDetailPhotos(_selectedPhotoPaths);

            ShowOriginalsButton.Visibility = Visibility.Collapsed;
            EnhancedTray.BorderBrush       = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x1E, 0x2F, 0x4A));
        }

        private void SetEnhancedView(List<string> paths)
        {
            _showingEnhanced = true;

            LoadDetailPhotos(paths);

            ShowOriginalsButton.Visibility = Visibility.Visible;
            EnhancedTray.BorderBrush       = (System.Windows.Media.Brush)FindResource("AccentBrush");
        }

        private void LoadDetailPhotos(List<string> paths)
        {
            _ = Task.WhenAll(
                LoadDetailPhotoAsync(DetailPhoto1, paths, 0),
                LoadDetailPhotoAsync(DetailPhoto2, paths, 1),
                LoadDetailPhotoAsync(DetailPhoto3, paths, 2));
        }

        private async Task LoadDetailPhotoAsync(Image target, List<string> paths, int index)
        {
            if (index >= paths.Count)
            {
                target.Source = null;
                return;
            }
            try
            {
                var source = await Task.Run(() => BitmapHelper.LoadFromFile(paths[index], decodeWidth: 800));
                target.Source = source;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load detail photo {Index}", index);
                target.Source = null;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            DetailPanel.Visibility          = Visibility.Collapsed;
            SessionsScrollViewer.Visibility = Visibility.Visible;
            BackButton.Visibility           = Visibility.Collapsed;
            HeaderTitle.Visibility          = Visibility.Visible;
            PrintStatusText.Text            = string.Empty;
        }

        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var activePaths  = (VariantsList.SelectedItem as VariantRowItem)?.Paths ?? new List<string>();
            var pathsToPrint = _showingEnhanced ? activePaths : _selectedPhotoPaths;

            if (pathsToPrint.Count == 0)
            {
                PrintStatusText.Text = "No photos to print.";
                return;
            }

            PrintButton.IsEnabled = false;
            PrintStatusText.Text  = "Printing…";

            var result = await PrintHelper.PrintSessionAsync(_selectedSessionId, _eventId, pathsToPrint);
            PrintStatusText.Text = result.Message;

            if (result.CanRetry)
                PrintButton.IsEnabled = true;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

        // --- AI Enhancement overlay ----------------------------------------------

        private async void EnhanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPhotoPaths.Count == 0) return;

            // Reset overlay to loading state
            StyleLoadingText.Text              = "Loading styles…";
            StyleLoadingText.Visibility        = Visibility.Visible;
            OverlayStylesScroller.Visibility   = Visibility.Collapsed;
            EnhanceProgressPanel.Visibility    = Visibility.Collapsed;
            EnhanceConfirmButton.IsEnabled     = false;
            OverlayStylesList.SelectedItem     = null;
            EnhanceOverlay.Visibility          = Visibility.Visible;

            try
            {
                var styles = await App.AIClient.GetStylesAsync();
                OverlayStylesList.ItemsSource      = styles;
                StyleLoadingText.Visibility        = Visibility.Collapsed;
                OverlayStylesScroller.Visibility   = Visibility.Visible;
                Log.Debug("Enhancement overlay loaded {Count} styles", styles.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load AI styles in session browser");
                StyleLoadingText.Text = $"Could not connect to AI server.\n{ex.Message}";
            }
        }

        private void OverlayStylesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            EnhanceConfirmButton.IsEnabled = OverlayStylesList.SelectedItem != null;
        }

        private async void EnhanceConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (OverlayStylesList.SelectedItem is not AugmentationStyle style) return;

            OverlayStylesScroller.Visibility = Visibility.Collapsed;
            EnhanceProgressPanel.Visibility  = Visibility.Visible;
            EnhanceProgressText.Text         = $"Enhancing with \"{style.Name}\"…";
            EnhanceConfirmButton.IsEnabled   = false;

            try
            {
                var ev     = App.Events.GetById(_eventId);
                var outDir = ev is not null
                    ? App.FileStorage.GetEnhancedPath(ev.Slug)
                    : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_selectedPhotoPaths[0])!)!, "Enhanced");

                var enhancedPaths = await App.AIClient.AugmentImagesAsync(_selectedPhotoPaths, style.Id, outDir);

                for (int i = 0; i < enhancedPaths.Count; i++)
                    App.Events.RecordEnhancedVariant(_selectedSessionId, i + 1, style.Id, style.Name, enhancedPaths[i]);

                Log.Information("Session {Id} enhanced with style {Style} — {Count} image(s)",
                    _selectedSessionId, style.Id, enhancedPaths.Count);

                var thumbs = enhancedPaths.Select(path =>
                {
                    try { return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(path, fallbackDecodeWidth: 150); }
                    catch { return null; }
                })
                .Where(b => b != null)
                .Cast<System.Windows.Media.ImageSource>()
                .ToList();

                var newRow = new VariantRowItem
                {
                    StyleId   = style.Id,
                    StyleName = style.Name,
                    Thumbs    = thumbs,
                    Paths     = enhancedPaths,
                };

                var existingRows = (VariantsList.ItemsSource as List<VariantRowItem>) ?? new List<VariantRowItem>();
                var updatedRows  = existingRows.Where(r => r.StyleId != style.Id).Concat(new[] { newRow }).ToList();

                EnhanceOverlay.Visibility = Visibility.Collapsed;
                LoadEnhancedTray(updatedRows);
                SetEnhancedView(enhancedPaths);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AI enhancement failed for session {Id}", _selectedSessionId);
                EnhanceProgressText.Text       = $"Enhancement failed:\n{ex.Message}";
                EnhanceConfirmButton.IsEnabled = true;
            }
        }

        private void EnhanceCancel_Click(object sender, RoutedEventArgs e)
        {
            EnhanceOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
