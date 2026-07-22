using System.Windows;

namespace Photobooth;

public partial class SplashWindow : Window
{
    public SplashWindow(string brandingText)
    {
        InitializeComponent();
        BrandingTextBlock.Text = brandingText;
        StatusTextBlock.Text   = "Starting…";
    }

    public void SetBranding(string brandingText) => BrandingTextBlock.Text = brandingText;

    public void SetStatus(string status) => StatusTextBlock.Text = status;
}
