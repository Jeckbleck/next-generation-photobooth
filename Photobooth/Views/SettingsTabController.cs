using System.Windows;
using System.Windows.Controls;

namespace Photobooth.Views;

internal sealed class SettingsTabController
{
    private readonly Button[]           _navButtons;
    private readonly FrameworkElement[] _contentPanels;
    private readonly FrameworkElement   _resourceOwner;

    public int CurrentIndex { get; private set; } = -1;

    public event Action<int, int>? TabChanged;  // (previousIndex, newIndex)

    public SettingsTabController(
        Button[]           navButtons,
        FrameworkElement[] contentPanels,
        FrameworkElement   resourceOwner)
    {
        _navButtons    = navButtons;
        _contentPanels = contentPanels;
        _resourceOwner = resourceOwner;
    }

    public void SelectTab(int index)
    {
        var previous = CurrentIndex;

        for (int i = 0; i < _navButtons.Length; i++)
        {
            _navButtons[i].Style = (Style)_resourceOwner.FindResource(
                i == index ? "SettingsNavButtonActive" : "SettingsNavButton");
            _contentPanels[i].Visibility = i == index
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        CurrentIndex = index;
        TabChanged?.Invoke(previous, index);
    }
}
