using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Serilog;

namespace Photobooth.Views
{
    public partial class ResultsPage : Page
    {
        private readonly List<string> _paths;
        private Timer? _timer;
        private int _secondsLeft = 5;

        public ResultsPage(List<string> photoPaths)
        {
            InitializeComponent();
            _paths = photoPaths;
            Loaded += OnLoaded;
            Log.Information("Navigated to ResultsPage with {Count} photo(s)", photoPaths.Count);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadPhotos();
            StartCountdown();
        }

        private void LoadPhotos()
        {
            LoadPhoto(Photo1, 0);
            LoadPhoto(Photo2, 1);
            LoadPhoto(Photo3, 2);
        }

        private void LoadPhoto(Image target, int index)
        {
            if (index >= _paths.Count) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(_paths[index]);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                target.Source = bmp;
                Log.Debug("Loaded photo {Index} from {Path}", index + 1, _paths[index]);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load photo {Index} from {Path}", index + 1, _paths[index]);
            }
        }

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

        private void StartAgain_Click(object sender, RoutedEventArgs e)
        {
            Log.Information("User tapped Start Again");
            _timer?.Dispose();
            ReturnToGreeting();
        }
    }
}
