using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Print;
using Photobooth.Services;
using Serilog;

namespace Photobooth.Views
{
    public partial class ResultsPage : Page
    {
        private readonly IEventService       _events;
        private readonly SettingsManager     _settings;
        private readonly IFileStorageService _fileStorage;
        private readonly AIEnhancementClient _aiClient;
        private readonly PrintService        _printer;
        private readonly FlowController      _flow;

        private readonly List<string> _paths;
        private readonly int          _sessionId;
        private readonly bool         _aiFlow;
        private readonly string?      _aiStyleId;
        private readonly string?      _aiStyleName;

        private Timer?  _timer;
        private int     _secondsLeft;
        private Bitmap? _strip;

        public ResultsPage(
            IEventService       events,
            SettingsManager     settings,
            IFileStorageService fileStorage,
            AIEnhancementClient aiClient,
            PrintService        printer,
            FlowController      flow)
        {
            _events      = events;
            _settings    = settings;
            _fileStorage = fileStorage;
            _aiClient    = aiClient;
            _printer     = printer;
            _flow        = flow;
            InitializeComponent();

            // Snapshot session context from FlowController (set before Navigate was called)
            _paths       = flow.SessionPhotos;
            _sessionId   = flow.SessionId;
            _aiFlow      = flow.AIFlowActive;
            _aiStyleId   = flow.AIStyleId;
            _aiStyleName = flow.AIStyleName;

            // Reset AI state so a subsequent non-AI session starts clean
            _flow.AIFlowActive = false;

            _secondsLeft = _settings.PreviewHoldSeconds;

            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;

            Log.Information("Navigated to ResultsPage with {Count} photo(s), session {SessionId}, AI={AI}",
                _paths.Count, _sessionId, _aiFlow);
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

            if (_settings.AutoPrint)
                _ = PrintAsync();
            else
            {
                if (_settings.ExperimentalFeatures)
                    PrintButton.Visibility = Visibility.Visible;
                else
                    PrintButtonPlain.Visibility = Visibility.Visible;
                PrintStatusText.Text = "Press Print when you're ready.";
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
                var eventId = _settings.ActiveEventId ?? 0;
                var ev      = _events.GetById(eventId);
                var outDir  = ev is not null
                    ? _fileStorage.GetEnhancedPath(ev.Slug)
                    : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_paths[0])!)!, "Enhanced");

                var enhancedPaths = await _aiClient.AugmentImagesAsync(_paths, _aiStyleId!, outDir);
                Log.Information("AI enhancement complete — {Count} image(s) in {Dir}", enhancedPaths.Count, outDir);

                if (_sessionId > 0)
                {
                    for (int i = 0; i < enhancedPaths.Count; i++)
                        _events.RecordEnhancedVariant(_sessionId, i + 1, _aiStyleId!, _aiStyleName ?? _aiStyleId!, enhancedPaths[i]);
                }

                AIStatusText.Text = "Composing enhanced strip…";

                _strip?.Dispose();
                _strip = await PrintHelper.ComposeStripAsync(eventId, enhancedPaths);
                var full   = await Task.Run(() => BitmapToSource(_strip));
                var single = CropToSingleStrip(full);
                StripPreview.Source = single;
                StripBack.Source    = single;

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
                var eventId = _settings.ActiveEventId ?? 0;
                _strip = await PrintHelper.ComposeStripAsync(eventId, _paths);
                var full   = await Task.Run(() => BitmapToSource(_strip));
                var single = CropToSingleStrip(full);
                StripPreview.Source = single;
                StripBack.Source    = single;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to compose photostrip for display");
            }
        }

        private static System.Windows.Media.Imaging.BitmapSource CropToSingleStrip(
            System.Windows.Media.Imaging.BitmapSource full)
        {
            var rect = new System.Windows.Int32Rect(0, 0, full.PixelWidth / 2, full.PixelHeight);
            return new System.Windows.Media.Imaging.CroppedBitmap(full, rect);
        }

        private static System.Windows.Media.Imaging.BitmapSource BitmapToSource(Bitmap bitmap)
        {
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

            PrintResult result;
            if (_strip is not null)
            {
                result = await PrintHelper.PrintSessionAsync(_sessionId, _strip);
            }
            else
            {
                var eventId = _settings.ActiveEventId ?? 0;
                result = await PrintHelper.PrintSessionAsync(_sessionId, eventId, _paths);
            }

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
            _flow.Trigger(FlowTrigger.PreviewDone);
        }

        // --- Button handlers -----------------------------------------------------

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            PrintButton.IsEnabled      = false;
            PrintButtonPlain.IsEnabled = false;
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
