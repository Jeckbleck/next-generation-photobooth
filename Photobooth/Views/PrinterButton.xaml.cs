using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Photobooth.Views;

public partial class PrinterButton : UserControl
{
    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Click),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(PrinterButton));

    public event RoutedEventHandler Click
    {
        add    => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public PrinterButton()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            StartStripAnimation();
        else
            PaperStrip.BeginAnimation(TranslateTransform.YProperty, null);
    }

    // ── Animation ────────────────────────────────────────────────────────────

    private void StartStripAnimation()
    {
        var transform = (TranslateTransform)PaperStrip.RenderTransform;

        var anim = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-68, T(0.0)));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-68, T(0.8)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0,   T(2.2)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,   T(3.4)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-68, T(3.9)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn  } });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-68, T(4.8)));

        transform.BeginAnimation(TranslateTransform.YProperty, anim);
    }

    private static KeyTime T(double seconds) =>
        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds));

    // ── Disabled state ───────────────────────────────────────────────────────

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty)
            Opacity = IsEnabled ? 1.0 : 0.40;
    }

    // ── Interaction states ───────────────────────────────────────────────────

    private void Root_MouseEnter(object sender, MouseEventArgs e)
        => PrinterBody.Opacity = 0.78;

    private void Root_MouseLeave(object sender, MouseEventArgs e)
        => PrinterBody.Opacity = 1.0;

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => PrinterBody.Opacity = 0.55;

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        PrinterBody.Opacity = 1.0;
        RaiseEvent(new RoutedEventArgs(ClickEvent));
    }
}
