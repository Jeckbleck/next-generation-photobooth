using System.Drawing;
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
        private readonly bool         _aiFlow;
        private readonly string?      _aiStyleId;
        private readonly string?      _aiStyleName;
        private Timer?  _timer;
        private int     _secondsLeft = App.Settings.PreviewHoldSeconds;
        private Bitmap? _strip;

        public ResultsPage(List<string> photoPaths, int sessionId)
        {
            InitializeComponent();
            _paths     = photoPaths;
            _sessionId = sessionId;
            _aiFlow      = App.AIFlowActive;
            _aiStyleId   = App.AISelectedStyleId;
            _aiStyleName = App.AISelectedStyleName;
            App.AIFlowActive = false;
            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
            Log.Information("Navigated to ResultsPage with {Count} photo(s), session {SessionId}, AI={AI}",
                photoPaths.Count, sessionId, _aiFlow);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _ = ComposeAndDisplayAsync();

            if (_aiFlow)
            {
                AIStatusPanel.Visibility = Visibility.Visible;
                AIStatusText.Text        = "Enhancing your photos…";
                _ = RunAIEnhancementAsync();
            }
            else
            {
                StartCountdown();
            }

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

        // --- AI enhancement ------------------------------------------------------

        private async Task RunAIEnhancementAsync()
        {
            try
            {
                var eventId = App.Settings.ActiveEventId ?? 0;
                var ev      = App.Events.GetById(eventId);
                var outDir  = ev is not null
                    ? App.FileStorage.GetEnhancedPath(ev.Slug)
                    : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_paths[0])!)!, "Enhanced");

                var enhancedPaths = await App.AIClient.AugmentImagesAsync(_paths, _aiStyleId!, outDir);
                Log.Information("AI enhancement complete — {Count} image(s) in {Dir}", enhancedPaths.Count, outDir);

                // Persist enhanced paths to DB (sequence is 1-based, matching ShootPage)
                if (_sessionId > 0)
                {
                    for (int i = 0; i < enhancedPaths.Count; i++)
                        App.Events.RecordEnhancedVariant(_sessionId, i + 1, _aiStyleId!, _aiStyleName ?? _aiStyleId!, enhancedPaths[i]);
                }

                AIStatusText.Text = "Composing enhanced strip…";

                _strip?.Dispose();
                _strip = await PrintHelper.ComposeStripAsync(eventId, enhancedPaths);
                var source = await Task.Run(() => BitmapToSource(_strip));
                StripPreview.Source = source;

                AIStatusText.Text = "Enhancement complete!";
                AIStatusDot.Fill  = System.Windows.Media.Brushes.LimeGreen;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AI enhancement failed");
                AIStatusText.Text = "Enhancement failed — showing original photos.";
            }
            finally
            {
                StartCountdown();
            }
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

        private static System.Windows.Media.Imaging.BitmapSource BitmapToSource(Bitmap bitmap)
        {
            // Direct GDI→WPF conversion via HBitmap handle — no PNG encode/decode roundtrip.
            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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
