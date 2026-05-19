using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Data.Models;
using Photobooth.Print;
using Serilog;

namespace Photobooth.Views
{
    public partial class SessionBrowserWindow : Window
    {
        private readonly int  _eventId;
        private readonly int? _openSessionId;
        private int           _selectedSessionId;
        private List<string>  _selectedPhotoPaths = new();

        private sealed class SessionCardItem
        {
            public int    SessionId  { get; init; }
            public string Label      { get; init; } = "";
            public string Date       { get; init; } = "";
            public string PhotoLabel { get; init; } = "";
            public List<System.Windows.Media.ImageSource> Thumbnails { get; init; } = new();
            public List<string> PhotoPaths { get; init; } = new();
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
            LoadSessions();

            if (_openSessionId.HasValue &&
                SessionCardsControl.ItemsSource is IEnumerable<SessionCardItem> cards)
            {
                var target = cards.FirstOrDefault(c => c.SessionId == _openSessionId.Value);
                if (target != null) OpenDetail(target);
            }
        }

        private void LoadSessions()
        {
            var sessions = App.Events.GetSessionsWithPhotos(_eventId);

            if (sessions.Count == 0)
            {
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            int total = sessions.Count;
            var items = sessions.Select((s, idx) =>
            {
                var paths = s.Photos
                    .Where(p => p.FilePath != null && File.Exists(p.FilePath))
                    .OrderBy(p => p.Sequence)
                    .Select(p => p.FilePath!)
                    .ToList();

                var thumbs = paths.Select(path =>
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource        = new Uri(path);
                        bmp.CacheOption      = BitmapCacheOption.OnLoad;
                        bmp.DecodePixelWidth = 150;
                        bmp.EndInit();
                        bmp.Freeze();
                        return (System.Windows.Media.ImageSource?)bmp;
                    }
                    catch { return null; }
                })
                .Where(b => b != null)
                .Cast<System.Windows.Media.ImageSource>()
                .ToList();

                int n = s.Photos.Count;
                return new SessionCardItem
                {
                    SessionId  = s.Id,
                    Label      = $"Session {total - idx}",
                    Date       = s.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
                    PhotoLabel = $"{n} photo{(n == 1 ? "" : "s")}",
                    Thumbnails = thumbs,
                    PhotoPaths = paths,
                };
            }).ToList();

            SessionCardsControl.ItemsSource = items;
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

            DetailLabel.Text      = item.Label;
            DetailDate.Text       = item.Date;
            PrintStatusText.Text  = string.Empty;
            PrintButton.IsEnabled = true;

            LoadDetailPhoto(DetailPhoto1, 0);
            LoadDetailPhoto(DetailPhoto2, 1);
            LoadDetailPhoto(DetailPhoto3, 2);

            SessionsScrollViewer.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility          = Visibility.Visible;
            BackButton.Visibility           = Visibility.Visible;
            HeaderTitle.Visibility          = Visibility.Collapsed;

            Log.Debug("Session browser opened detail for session {Id}", item.SessionId);
        }

        private void LoadDetailPhoto(Image target, int index)
        {
            if (index >= _selectedPhotoPaths.Count)
            {
                target.Source = null;
                return;
            }
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource   = new Uri(_selectedPhotoPaths[index]);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                target.Source = bmp;
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

        private (string? templatePath, List<StripSlotDefinition> slots) LoadTemplateConfig()
        {
            var ev = App.Events.GetById(_eventId);
            if (ev is null) return (null, new());

            var templatePath = ev.PhotostripTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                return (null, new());

            var jsonPath = Path.Combine(App.FileStorage.GetStripTemplatePath(ev.Slug), "template.json");
            if (!File.Exists(jsonPath)) return (null, new());

            try
            {
                var config = JsonSerializer.Deserialize<StripTemplateConfig>(File.ReadAllText(jsonPath));
                if (config is null || config.Slots.Count == 0) return (null, new());
                return (templatePath, config.Slots);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read strip template config for event {Id}", _eventId);
                return (null, new());
            }
        }

        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPhotoPaths.Count == 0)
            {
                PrintStatusText.Text = "No photos to print.";
                return;
            }

            PrintButton.IsEnabled = false;
            PrintStatusText.Text  = "Printing…";

            if (_selectedSessionId > 0)
            {
                try
                {
                    App.Events.RecordPrint(_selectedSessionId);
                }
                catch (InvalidOperationException limitEx)
                {
                    Log.Warning(limitEx, "Print limit reached for session {Id}", _selectedSessionId);
                    PrintStatusText.Text  = "Print limit reached for this session.";
                    PrintButton.IsEnabled = true;
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not record print for session {Id}", _selectedSessionId);
                }
            }

            try
            {
                var paths    = _selectedPhotoPaths;
                var branding = App.Settings.BrandingText;
                (string? templatePath, List<StripSlotDefinition> slots) = LoadTemplateConfig();

                using var strip = await Task.Run(() =>
                    templatePath is not null && slots.Count > 0
                        ? PhotostripComposer.ComposeFromTemplate(templatePath, slots, paths)
                        : PhotostripComposer.Compose(paths, branding));

                await App.Printer.PrintStripAsync(strip);
                PrintStatusText.Text = "Strip sent to printer!";
                Log.Information("Print submitted from session browser for session {Id}", _selectedSessionId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Print failed in session browser for session {Id}", _selectedSessionId);
                PrintStatusText.Text  = "Print failed — please see staff.";
                PrintButton.IsEnabled = true;
            }
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    }
}
