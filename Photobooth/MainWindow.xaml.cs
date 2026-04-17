using System.Windows;
using Photobooth.Views;

namespace Photobooth
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            RootFrame.Navigate(new GreetingPage());
        }

        public void NavigateTo(System.Windows.Controls.Page page)
            => RootFrame.Navigate(page);
    }
}
