using System.Windows;
using System.Windows.Controls;

namespace Photobooth.Views;

public partial class InfoIcon : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(InfoIcon), new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public InfoIcon()
    {
        InitializeComponent();
    }
}
