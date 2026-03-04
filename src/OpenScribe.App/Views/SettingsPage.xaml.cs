using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using OpenScribe.App.ViewModels;

namespace OpenScribe.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Host.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }
}
