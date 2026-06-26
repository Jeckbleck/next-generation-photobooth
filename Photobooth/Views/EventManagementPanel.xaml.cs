using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Photobooth.Helpers;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views;

public partial class EventManagementPanel : UserControl
{
    private readonly IEventService       _events;
    private readonly SettingsManager     _settings;
    private readonly IFileStorageService _fileStorage;
    private readonly AIEnhancementClient _aiClient;

    private int?  _selectedEventId;
    private bool  _loadingEvents;
    private int   _previewVersion;

    public int? SelectedEventId => _selectedEventId;

    public event EventHandler<Data.Models.Event?> ActiveEventChanged = delegate { };

    public EventManagementPanel(
        IEventService       events,
        SettingsManager     settings,
        IFileStorageService fileStorage,
        AIEnhancementClient aiClient)
    {
        _events      = events;
        _settings    = settings;
        _fileStorage = fileStorage;
        _aiClient    = aiClient;
        InitializeComponent();
    }

    // Called by GreetingPage.OpenSettings() when the settings panel opens.
    public void Refresh()
    {
        LoadEvents();
        RefreshStoragePath();
    }

    // --- Event Management ------------------------------------------------

    private void LoadEvents()
    {
        var savedId = _settings.ActiveEventId;

        _loadingEvents = true;
        try
        {
            EventsComboBox.Items.Clear();
            EventsComboBox.Items.Add(new ComboBoxItem { Content = "— none —", Tag = null });

            var recent = _events.GetRecent(6);

            // If the saved event isn't in the recent 6, fetch it and show it at the top.
            if (savedId.HasValue && recent.All(e => e.Id != savedId.Value))
            {
                var active = _events.GetById(savedId.Value);
                if (active is not null)
                    recent.Insert(0, active);
            }

            foreach (var ev in recent)
                EventsComboBox.Items.Add(new ComboBoxItem { Content = ev.Name, Tag = ev.Id });

            EventsComboBox.SelectedIndex = 0;
        }
        finally
        {
            _loadingEvents = false;
        }

        if (savedId.HasValue)
            SelectEventById(savedId.Value);
        else
        {
            _selectedEventId = null;
            ClearEventFields();
        }
    }

    private void EventsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEvents) return;
        if (EventsComboBox.SelectedItem is not ComboBoxItem item) return;

        if (item.Tag is int id)
        {
            SetSelectedEvent(id);
            var ev = _events.GetById(id);
            if (ev is not null)
            {
                PopulateEventFields(ev.PaywallEnabled, ev.SaveImagesEnabled, ev.PrintLimitPerEvent, ev.PrintLimitPerSession);
                ActiveEventChanged.Invoke(this, ev);
            }
            _ = RefreshSessionPreviewAsync(id);
        }
        else
        {
            SetSelectedEvent(null);
            ClearEventFields();
            ActiveEventChanged.Invoke(this, null);
        }
    }

    private void SetSelectedEvent(int? id)
    {
        _selectedEventId = id;
        _settings.SetActiveEventId(id);
    }

    private void PopulateEventFields(bool paywallEnabled, bool saveImagesEnabled,
        int? printLimitPerEvent, int? printLimitPerSession)
    {
        PaywallToggle.IsChecked        = paywallEnabled;
        SaveImagesToggle.IsChecked     = saveImagesEnabled;
        PrintLimitBox.Text             = printLimitPerEvent?.ToString()   ?? string.Empty;
        SessionLimitBox.Text           = printLimitPerSession?.ToString() ?? string.Empty;
        ArchiveEventButton.IsEnabled   = true;
    }

    private void ClearEventFields()
    {
        PaywallToggle.IsChecked         = false;
        SaveImagesToggle.IsChecked      = true;
        PrintLimitBox.Text              = string.Empty;
        SessionLimitBox.Text            = string.Empty;
        ArchiveEventButton.IsEnabled    = false;
        SessionPreviewList.ItemsSource  = null;
    }

    private void NewEvent_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewEventDialog(_events) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            LoadEvents();
            SelectEventById(dialog.CreatedEventId);
        }
    }

    private void BrowseEvents_Click(object sender, RoutedEventArgs e)
    {
        var browser = new EventBrowserWindow(_events) { Owner = Window.GetWindow(this) };
        if (browser.ShowDialog() == true)
        {
            LoadEvents();
            SelectEventById(browser.SelectedEventId);
        }
    }

    private void ArchiveEvent_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;

        var ev = _events.GetById(_selectedEventId.Value);
        if (ev is null) return;

        var result = MessageBox.Show(
            $"Archive \"{ev.Name}\"?\n\nThe event will be hidden from the list. All sessions and photos on disk are kept and can be recovered by an administrator.",
            "Archive Event",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _events.Archive(_selectedEventId.Value);
        SetSelectedEvent(null);
        LoadEvents();
        ActiveEventChanged.Invoke(this, null);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;

        if (sender == PaywallToggle)
            _events.SetPaywall(_selectedEventId.Value, PaywallToggle.IsChecked == true);
        else if (sender == SaveImagesToggle)
            _events.SetSaveImages(_selectedEventId.Value, SaveImagesToggle.IsChecked == true);
    }

    private void PrintLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void PrintLimitBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        int? limit = int.TryParse(PrintLimitBox.Text.Trim(), out int parsed) && parsed > 0 ? parsed : null;
        _events.SetEventPrintLimit(_selectedEventId.Value, limit);
        PrintLimitBox.Text = limit?.ToString() ?? string.Empty;
    }

    private void PrintLimitMinus_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        int current = int.TryParse(PrintLimitBox.Text.Trim(), out int v) ? v : 0;
        if (current <= 0) return;
        int next = Math.Max(0, current - EventPrintLimitStep(current));
        int? limit = next > 0 ? next : (int?)null;
        _events.SetEventPrintLimit(_selectedEventId.Value, limit);
        PrintLimitBox.Text = limit?.ToString() ?? string.Empty;
    }

    private void PrintLimitPlus_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        int current = int.TryParse(PrintLimitBox.Text.Trim(), out int v) ? v : 0;
        int next = current + EventPrintLimitStep(current);
        _events.SetEventPrintLimit(_selectedEventId.Value, next);
        PrintLimitBox.Text = next.ToString();
    }

    // Step size scales with current value so reaching 300 doesn't require 300 taps.
    private static int EventPrintLimitStep(int current) => current switch
    {
        < 10  => 1,
        < 100 => 10,
        _     => 25,
    };

    private void SessionLimitBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void SessionLimitBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        int? limit = int.TryParse(SessionLimitBox.Text.Trim(), out int parsed) && parsed > 0 ? parsed : null;
        _events.SetSessionPrintLimit(_selectedEventId.Value, limit);
        SessionLimitBox.Text = limit?.ToString() ?? string.Empty;
    }

    private void SessionLimitMinus_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        int current = int.TryParse(SessionLimitBox.Text.Trim(), out int v) ? v : 0;
        if (current <= 0) return;
        int next = current - 1;
        int? limit = next > 0 ? next : (int?)null;
        _events.SetSessionPrintLimit(_selectedEventId.Value, limit);
        SessionLimitBox.Text = limit?.ToString() ?? string.Empty;
    }

    private void SessionLimitPlus_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        int current = int.TryParse(SessionLimitBox.Text.Trim(), out int v) ? v : 0;
        int next = current + 1;
        _events.SetSessionPrintLimit(_selectedEventId.Value, next);
        SessionLimitBox.Text = next.ToString();
    }

    private void SelectEventById(int id)
    {
        var ev = _events.GetById(id);
        if (ev is null) return;

        _loadingEvents = true;
        try
        {
            foreach (ComboBoxItem item in EventsComboBox.Items)
            {
                if (item.Tag is int itemId && itemId == id)
                {
                    EventsComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _loadingEvents = false;
        }

        SetSelectedEvent(id);
        PopulateEventFields(ev.PaywallEnabled, ev.SaveImagesEnabled, ev.PrintLimitPerEvent, ev.PrintLimitPerSession);
        ActiveEventChanged.Invoke(this, ev);
        _ = RefreshSessionPreviewAsync(id);
    }

    // --- Session preview -----------------------------------------------------

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
        if (!_selectedEventId.HasValue) return;
        if (((Button)sender).DataContext is not SessionPreviewItem item) return;
        OpenSessionBrowserAt(item.SessionId);
    }

    private void OpenSessionBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedEventId.HasValue) return;
        OpenSessionBrowserAt(null);
    }

    private void OpenSessionBrowserAt(int? sessionId)
    {
        var browser = new SessionBrowserWindow(
            _selectedEventId!.Value, _events, _aiClient, _fileStorage, sessionId)
        {
            Owner = Window.GetWindow(this)
        };
        browser.ShowDialog();
        _ = RefreshSessionPreviewAsync(_selectedEventId.Value);
    }

    // --- Storage path --------------------------------------------------------

    private void RefreshStoragePath()
    {
        StoragePathBox.Text = _settings.StorageRoot;
        StoragePathWarning.Visibility = _settings.IsStorageConfigured
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BrowseStoragePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title            = "Select storage root folder",
            InitialDirectory = _settings.StorageRoot,
        };

        if (dlg.ShowDialog() != true) return;

        _settings.SetStorageRoot(dlg.FolderName);
        StoragePathBox.Text = dlg.FolderName;
        StoragePathWarning.Visibility = Visibility.Collapsed;
        Log.Information("Storage root changed to {Path}", dlg.FolderName);
    }
}
