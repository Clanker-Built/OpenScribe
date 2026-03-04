using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenScribe.App.ViewModels;

namespace OpenScribe.App.Views;

public sealed partial class ExportPage : Page
{
    public ExportViewModel ViewModel { get; }

    public ExportPage()
    {
        ViewModel = App.Host.Services.GetRequiredService<ExportViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadSessionsAsync();
    }
}
