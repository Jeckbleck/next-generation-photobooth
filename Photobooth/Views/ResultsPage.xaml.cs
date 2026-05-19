using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Print;
using Serilog;

namespace Photobooth.Views
{
    public partial class ResultsPage : Page
    {
        private readonly List<string> _paths;
        private readonly int          _sessionId;
        private Timer?  _timer;
        private int     _secondsLeft = 5;
        private Bitmap? _strip;

        public ResultsPage(List<string> photoPaths, int sessionId)
        {
            InitializeComponent();
            _paths     = photoPaths;
            _sessionId = sessionId;
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
            Log.Information("Navigated to ResultsPage with {Count} photo(s), session {SessionId}",
                photoPaths.Count, sessionId);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartCountdown();
            _ = ComposeAndDisplayAsync();

            if (App.Settings.AutoPrint)
                _ = PrintAsync();
            else
            {
                PrintButton.Visibility = Visibility.Visible;
                PrintStatusText.Text   = "Press Print when you're ready.";
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _strip?.Dispose();
            _strip = null;
        }

        // --- Strip composition ---------------------------------------------------

        private async Task ComposeAndDisplayAsync()
        {
            try
            {
                var eventId = App.Settings.ActiveEventId ?? 0;
                _strip = await PrintHelper.ComposeStripAsync(eventId, _paths);
                var source = await Task.Run(() => BitmapToSource(_strip));
                StripPreview.Source = source;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to compose photostrip for display");
            }
        }

        private static BitmapImage BitmapToSource(Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption  = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        // --- Printing ------------------------------------------------------------

        private async Task PrintAsync()
        {
            PrintStatusText.Text = "Printing your strip…";

            // Reuse the strip already composed for display if available
            var eventId = App.Settings.ActiveEventId ?? 0;
            var strip   = _strip ?? await PrintHelper.ComposeStripAsync(eventId, _paths);

            var result = await PrintHelper.PrintSessionAsync(_sessionId, strip);
            PrintStatusText.Text = result.Message;
        }

        // --- Countdown -----------------------------------------------------------

        private void StartCountdown()
        {
            UpdateCountdownText();
            _timer = new Timer(_ =>
            {
                _secondsLeft--;
                Dispatcher.Invoke(() =>
                {
                    UpdateCountdownText();
                    if (_secondsLeft <= 0)
                    {
                        _timer?.Dispose();
                        ReturnToGreeting();
                    }
                });
            }, null, 1000, 1000);
        }

        private void UpdateCountdownText()
        {
            ReturnCountdown.Text = _secondsLeft > 0
                ? $"Returning to start in {_secondsLeft}…"
                : "See you next time!";
        }

        private void ReturnToGreeting()
        {
            Log.Information("Returning to GreetingPage");
            var window = Window.GetWindow(this) as MainWindow;
            window?.NavigateTo(new GreetingPage());
        }

        // --- Button handlers -----------------------------------------------------

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            PrintButton.IsEnabled = false;
            _ = PrintAsync();
        }

        private void StartAgain_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User tapped Start Again");
            _timer?.Dispose();
            ReturnToGreeting();
        }
    }
}
