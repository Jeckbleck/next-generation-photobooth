using System.Windows;
using System.Windows.Input;
using Photobooth.Services;

namespace Photobooth.Views;

public partial class NewEventDialog : Window
{
    private readonly IEventService _events;

    public int CreatedEventId { get; private set; }

    public NewEventDialog(IEventService events)
    {
        _events = events;
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Create_Click(object sender, RoutedEventArgs e) => TryCreate();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  TryCreate();
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void TryCreate()
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        ErrorLabel.Visibility = Visibility.Collapsed;
        var ev = _events.Create(name,
            paywallEnabled:       false,
            saveImagesEnabled:    true,
            printLimitPerEvent:   null,
            printLimitPerSession: null);
        CreatedEventId = ev.Id;
        DialogResult   = true;
    }
}
