using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Photobooth.Helpers;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class EventStatsPanel : UserControl
{
    private readonly IEventService       _events;
    private readonly AIEnhancementClient _aiClient;
    private readonly IFileStorageService _fileStorage;

    private int? _eventId;
    private int  _previewVersion;

    public EventStatsPanel(IEventService events, AIEnhancementClient aiClient, IFileStorageService fileStorage)
    {
        _events      = events;
        _aiClient    = aiClient;
        _fileStorage = fileStorage;
        InitializeComponent();
    }

    public void Refresh(int eventId)
    {
        _eventId = eventId;
        var (sessions, photos, prints, ai) = _events.GetStats(eventId);
        SessionCountText.Text = sessions.ToString();
        PhotoCountText.Text   = photos.ToString();
        PrintCountText.Text   = prints.ToString();
        AICountText.Text      = ai.ToString();
        _ = RefreshSessionPreviewAsync(eventId);
    }

    public void Clear()
    {
        _eventId                       = null;
        SessionCountText.Text          = "—";
        PhotoCountText.Text            = "—";
        PrintCountText.Text            = "—";
        AICountText.Text               = "—";
        SessionPreviewList.ItemsSource = null;
    }

    private sealed class SessionPreviewItem
    {
        public int    SessionId  { get; init; }
        public string Date       { get; init; } = "";
        public List<System.Windows.Media.ImageSource> Thumbnails { get; init; } = new();
    }

    private async Task RefreshSessionPreviewAsync(int eventId)
    {
        var version = ++_previewVersion;
        SessionPreviewList.ItemsSource = null;

        var items = await Task.Run(() =>
        {
            var sessions = _events.GetSessionsWithPhotos(eventId);
            return sessions.Select(s =>
            {
                var thumbs = s.Photos
                    .Where(p => p.FilePath != null && File.Exists(p.FilePath))
                    .OrderBy(p => p.Sequence)
                    .Select(p =>
                    {
                        try { return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(p.FilePath!, fallbackDecodeWidth: 80); }
                        catch { return null; }
                    })
                    .Where(b => b != null)
                    .Cast<System.Windows.Media.ImageSource>()
                    .ToList();

                return new SessionPreviewItem
                {
                    SessionId  = s.Id,
                    Date       = s.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
                    Thumbnails = thumbs,
                };
            }).ToList();
        });

        if (_previewVersion != version) return;
        SessionPreviewList.ItemsSource = items;
    }

    private void SessionPreviewCard_Click(object sender, RoutedEventArgs e)
    {
        if (!_eventId.HasValue) return;
        if (((Button)sender).DataContext is not SessionPreviewItem item) return;
        OpenSessionBrowserAt(item.SessionId);
    }

    private void OpenSessionBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (!_eventId.HasValue) return;
        OpenSessionBrowserAt(null);
    }

    private void OpenSessionBrowserAt(int? sessionId)
    {
        var browser = new SessionBrowserWindow(
            _eventId!.Value, _events, _aiClient, _fileStorage, sessionId)
        {
            Owner = Window.GetWindow(this)
        };
        browser.ShowDialog();
        Refresh(_eventId.Value);
    }

    private void ClearSessionData_Click(object sender, RoutedEventArgs e)
    {
        if (!_eventId.HasValue) return;

        var result = MessageBox.Show(
            "Clear all session data for this event?\n\nSession records and statistics will be deleted. Photos on disk are NOT deleted.",
            "Clear Session Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _events.ClearSessions(_eventId.Value);
        Refresh(_eventId.Value);
    }
}
