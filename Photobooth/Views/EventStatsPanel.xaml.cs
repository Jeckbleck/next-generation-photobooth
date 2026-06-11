using System.Windows;
using System.Windows.Controls;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class EventStatsPanel : UserControl
{
    private readonly IEventService _events;

    private int? _eventId;

    public EventStatsPanel(IEventService events)
    {
        _events = events;
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
    }

    public void Clear()
    {
        _eventId              = null;
        SessionCountText.Text = "—";
        PhotoCountText.Text   = "—";
        PrintCountText.Text   = "—";
        AICountText.Text      = "—";
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
