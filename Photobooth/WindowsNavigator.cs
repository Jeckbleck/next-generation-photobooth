using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Views;

namespace Photobooth;

public sealed class WindowsNavigator : INavigator
{
    private readonly IServiceProvider _provider;

    public WindowsNavigator(IServiceProvider provider) => _provider = provider;

    public void NavigateTo(BoothState state)
    {
        Page page = state switch
        {
            BoothState.Idle      => _provider.GetRequiredService<GreetingPage>(),
            BoothState.StylePick => _provider.GetRequiredService<StylePickerPage>(),
            BoothState.Shooting  => _provider.GetRequiredService<ShootPage>(),
            BoothState.Preview   => _provider.GetRequiredService<ResultsPage>(),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
        if (Application.Current.MainWindow is MainWindow w)
            w.NavigateTo(page);
    }
}
