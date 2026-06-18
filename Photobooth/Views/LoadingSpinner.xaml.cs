using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Photobooth.Views;

public partial class LoadingSpinner : UserControl
{
    public LoadingSpinner()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        };
    }
}
