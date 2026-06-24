using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Photobooth.Helpers;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class EventBrowserWindow : Window
{
    private readonly IEventService    _events;
    private readonly DispatcherTimer  _debounce;
    private          int              _currentPage = 1;
    private          int              _totalPages  = 1;
    private const    int              PageSize     = 9;

    public int SelectedEventId { get; private set; }

    public EventBrowserWindow(IEventService events)
    {
        _events = events;
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _currentPage = 1; LoadPage(); };
        Loaded += (_, _) => LoadPage();
    }

    private EventQuery BuildQuery() => new()
    {
        Search          = SearchBox.Text.Trim(),
        Sort            = SortComboBox.SelectedIndex switch
        {
            1 => EventSortOrder.OldestFirst,
            2 => EventSortOrder.NameAZ,
            3 => EventSortOrder.NameZA,
            _ => EventSortOrder.NewestFirst,
        },
        IncludeArchived = ShowArchivedToggle.IsChecked == true,
        Page            = _currentPage,
        PageSize        = PageSize,
    };

    private void LoadPage()
    {
        var (events, total) = _events.QueryEvents(BuildQuery());
        _totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));

        var items = events.Select(e => new EventCardItem
        {
            EventId    = e.Id,
            Name       = e.Name,
            DateLabel  = e.CreatedAt.ToLocalTime().ToString("MMM d, yyyy"),
            IsArchived = e.ArchivedAt.HasValue,
            PhotoPath  = _events.GetRecentPhotos(e.Id, 1).FirstOrDefault()?.FilePath,
        }).ToList();

        CardGrid.ItemsSource  = items.Count > 0 ? items : null;
        EmptyLabel.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        TotalLabel.Text       = $"{total} event{(total == 1 ? "" : "s")}";
        PageLabel.Text        = $"Page {_currentPage} of {_totalPages}";
        PrevButton.IsEnabled  = _currentPage > 1;
        NextButton.IsEnabled  = _currentPage < _totalPages;

        _ = LoadThumbnailsAsync(items);
    }

    private async Task LoadThumbnailsAsync(List<EventCardItem> items)
    {
        foreach (var item in items.Where(i => i.PhotoPath is not null))
        {
            var path = item.PhotoPath!;
            var bmp  = await Task.Run(() =>
            {
                try   { return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(path, 160); }
                catch { return null; }
            });
            if (bmp is not null)
                item.Thumbnail = bmp;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    // SelectionChanged on ComboBox requires SelectionChangedEventArgs — separate from FilterChanged.
    private void SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _currentPage = 1;
        LoadPage();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        LoadPage();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 1) { _currentPage--; LoadPage(); }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage < _totalPages) { _currentPage++; LoadPage(); }
    }

    private void EventCard_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is EventCardItem item)
        {
            SelectedEventId = item.EventId;
            DialogResult    = true;
        }
    }

    private sealed class EventCardItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public int     EventId    { get; init; }
        public string  Name       { get; init; } = "";
        public string  DateLabel  { get; init; } = "";
        public bool    IsArchived { get; init; }
        public string? PhotoPath  { get; init; }
        public double  CardOpacity => IsArchived ? 0.5 : 1.0;

        private System.Windows.Media.ImageSource? _thumbnail;
        public System.Windows.Media.ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new(nameof(Thumbnail)));
            }
        }
    }
}
