using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Photobooth.Data.Models;
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
                (string? templatePath, List<StripSlotDefinition> slots) = LoadTemplateConfig();

                _strip = await Task.Run(() =>
                    templatePath is not null && slots.Count > 0
                        ? PhotostripComposer.ComposeFromTemplate(templatePath, slots, _paths)
                        : PhotostripComposer.Compose(_paths, App.Settings.BrandingText));

                var source = await Task.Run(() => BitmapToSource(_strip));
                StripPreview.Source = source;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to compose photostrip for display");
            }
        }

        private (string? templatePath, List<StripSlotDefinition> slots) LoadTemplateConfig()
        {
            var eventId = App.Settings.ActiveEventId;
            if (!eventId.HasValue) return (null, new());

            var ev = App.Events.GetById(eventId.Value);
            if (ev is null) return (null, new());

            // Template image path comes from the DB record
            var templatePath = ev.PhotostripTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                return (null, new());

            // Slot definitions always live in the event's Strip template folder
            var jsonPath = Path.Combine(App.FileStorage.GetStripTemplatePath(ev.Slug), "template.json");
            if (!File.Exists(jsonPath)) return (null, new());

            try
            {
                var config = JsonSerializer.Deserialize<StripTemplateConfig>(File.ReadAllText(jsonPath));
                if (config is null || config.Slots.Count == 0) return (null, new());

                return (templatePath, config.Slots);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read strip slot config for event {Id}", eventId);
                return (null, new());
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
            if (_sessionId > 0)
            {
                try   { App.Events.RecordPrint(_sessionId); }
                catch (InvalidOperationException limitEx)
                {
                    Log.Warning(limitEx, "Print limit reached for session {SessionId}", _sessionId);
                    PrintStatusText.Text = "Print limit reached for this session.";
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Could not record print for session {SessionId}", _sessionId);
                }
            }

            try
            {
                PrintStatusText.Text = "Printing your strip…";

                // Wait for the strip to be composed if it hasn't finished yet
                var strip = _strip ?? await Task.Run(() =>
                    PhotostripComposer.Compose(_paths, App.Settings.BrandingText));

                await App.Printer.PrintStripAsync(strip);
                PrintStatusText.Text = "Your strip is printing!";
                Log.Information("Print job submitted for session {SessionId}", _sessionId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Print failed");
                PrintStatusText.Text = "Print failed — please see staff.";
            }
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
