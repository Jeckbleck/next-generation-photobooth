using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Views;

namespace Photobooth
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            RootFrame.Navigate(App.Services.GetRequiredService<GreetingPage>());
        }

        public void NavigateTo(System.Windows.Controls.Page page)
            => RootFrame.Navigate(page);
    }
}
