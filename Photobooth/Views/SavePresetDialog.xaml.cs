using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Photobooth.Views;

public partial class SavePresetDialog : Window
{
    private readonly HashSet<string> _existingNames;

    public string PresetName { get; private set; } = "";

    public SavePresetDialog(IEnumerable<string> existingNames)
    {
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        OverwriteHintLabel.Visibility = (name.Length > 0 && _existingNames.Contains(name))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)  TrySave();
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void TrySave()
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorLabel.Visibility = Visibility.Visible;
            NameBox.Focus();
            return;
        }

        ErrorLabel.Visibility = Visibility.Collapsed;
        PresetName   = name;
        DialogResult = true;
    }
}
