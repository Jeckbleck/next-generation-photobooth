using System.Windows;
using System.Windows.Controls;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views
{
    public partial class StylePickerPage : Page
    {
        public StylePickerPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingText.Visibility  = Visibility.Visible;
                StylesScroller.Visibility = Visibility.Collapsed;
                LoadingText.Text        = "Loading styles…";

                var styles = await App.AIClient.GetStylesAsync();

                StylesListBox.ItemsSource = styles;
                LoadingText.Visibility    = Visibility.Collapsed;
                StylesScroller.Visibility = Visibility.Visible;

                Log.Information("StylePickerPage loaded {Count} styles", styles.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load AI styles");
                LoadingText.Text = $"Could not connect to AI server.\n{ex.Message}";
            }
        }

        private void StylesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfirmButton.IsEnabled = StylesListBox.SelectedItem != null;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (StylesListBox.SelectedItem is not AugmentationStyle style) return;

            App.AIFlowActive        = true;
            App.AISelectedStyleId   = style.Id;
            App.AISelectedStyleName = style.Name;

            Log.Information("AI style selected: {StyleId} ({StyleName})", style.Id, style.Name);
            App.Flow.Trigger(FlowTrigger.StyleChosen);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            App.AIFlowActive = false;
            App.Flow.Trigger(FlowTrigger.StyleCancelled);
        }
    }
}
