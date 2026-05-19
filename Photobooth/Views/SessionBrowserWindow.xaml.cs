using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Helpers;
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
                        return (System.Windows.Media.ImageSource?)BitmapHelper.LoadFromFile(path, decodeWidth: 150);
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
                target.Source = BitmapHelper.LoadFromFile(_selectedPhotoPaths[index]);
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
            if (_selectedPhotoPaths.Count == 0)
            {
                PrintStatusText.Text = "No photos to print.";
                return;
            }

            PrintButton.IsEnabled = false;
            PrintStatusText.Text  = "Printing…";

            var result = await PrintHelper.PrintSessionAsync(_selectedSessionId, _eventId, _selectedPhotoPaths);
            PrintStatusText.Text = result.Message;

            if (result.CanRetry)
                PrintButton.IsEnabled = true;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    }
}
