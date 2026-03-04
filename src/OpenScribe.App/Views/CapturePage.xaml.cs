using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenScribe.App.ViewModels;

namespace OpenScribe.App.Views;

public sealed partial class CapturePage : Page
{
    public CaptureViewModel ViewModel { get; }

    public CapturePage()
    {
        ViewModel = App.Host.Services.GetRequiredService<CaptureViewModel>();
        InitializeComponent();
        // Ensure the DispatcherQueue is captured from the UI thread
        ViewModel.EnsureDispatcher(DispatcherQueue.GetForCurrentThread());
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Reset state so previous session data doesn't bleed through
        ViewModel.ResetForNewCapture();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Release the microphone when leaving the page
        _ = ViewModel.StopMonitoringForNavigationAsync();
    }
}
