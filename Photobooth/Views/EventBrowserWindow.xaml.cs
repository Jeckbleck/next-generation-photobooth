using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Photobooth.Helpers;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class EventBrowserWindow : Window
{
    private readonly IEventService   _events;
    private readonly DispatcherTimer _debounce;
    private const    int             BatchSize = 12;

    private readonly ObservableCollection<EventCardItem> _items = new();
    private int  _loadedCount;
    private int  _totalCount = int.MaxValue;
    private bool _loading;

    public int SelectedEventId { get; private set; }

    public EventBrowserWindow(IEventService events)
    {
        _events = events;
        InitializeComponent();
        CardGrid.ItemsSource = _items;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); ResetAndLoad(); };
        Loaded += (_, _) => ResetAndLoad();
    }

    private EventQuery BuildQuery(int page) => new()
    {
        Search          = SearchBox.Text.Trim(),
        Sort            = SortComboBox.SelectedIndex switch
                          {
                              1 => EventSortOrder.OldestFirst,
                              2 => EventSortOrder.NameAZ,
                              3 => EventSortOrder.NameZA,
                              _ => EventSortOrder.NewestFirst,
                          },
        IncludeArchived = ShowArchivedToggle.IsChecked  == true,
        HasPhotostrip   = HasPhotostripToggle.IsChecked == true,
        Page            = page,
        PageSize        = BatchSize,
    };

    private void ResetAndLoad()
    {
        _loadedCount = 0;
        _totalCount  = int.MaxValue;
        _loading     = false;
        _items.Clear();
        EmptyLabel.Visibility = Visibility.Collapsed;
        LoadMore();
    }

    private void LoadMore()
    {
        if (_loading || _loadedCount >= _totalCount) return;
        _loading = true;

        int nextPage = _loadedCount / BatchSize + 1;
        var (events, total) = _events.QueryEvents(BuildQuery(nextPage));
        _totalCount = total;

        var newItems = events.Select(e => new EventCardItem
        {
            EventId    = e.Id,
            Name       = e.Name,
            DateLabel  = e.CreatedAt.ToLocalTime().ToString("MMM d, yyyy"),
            IsArchived = e.ArchivedAt.HasValue,
            PhotoPath  = e.PhotostripTemplatePath,
        }).ToList();

        foreach (var item in newItems)
            _items.Add(item);

        _loadedCount += newItems.Count;
        EmptyLabel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _loading = false;
        _ = LoadThumbnailsAsync(newItems);
    }

    private async Task LoadThumbnailsAsync(List<EventCardItem> items)
    {
        foreach (var item in items.Where(i => i.PhotoPath is not null))
        {
            var path = item.PhotoPath!;
            var bmp  = await Task.Run(() =>
            {
                try   { return (System.Windows.Media.ImageSource?)BitmapHelper.LoadThumbnail(path, 200); }
                catch { return null; }
            });
            if (bmp is not null)
                item.Thumbnail = bmp;
        }
    }

    private void CardScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.ScrollableHeight > 0 && sv.VerticalOffset >= sv.ScrollableHeight - 300)
            LoadMore();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ResetAndLoad();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        ResetAndLoad();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => DialogResult = false;

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

        public Visibility PlaceholderVisibility =>
            _thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

        private System.Windows.Media.ImageSource? _thumbnail;
        public System.Windows.Media.ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new(nameof(Thumbnail)));
                PropertyChanged?.Invoke(this, new(nameof(PlaceholderVisibility)));
            }
        }
    }
}
