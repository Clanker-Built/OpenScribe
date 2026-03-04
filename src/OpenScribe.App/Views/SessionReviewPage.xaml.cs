using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using OpenScribe.App.ViewModels;

namespace OpenScribe.App.Views;

public sealed partial class SessionReviewPage : Page
{
    public SessionReviewViewModel ViewModel { get; }

    public SessionReviewPage()
    {
        ViewModel = App.Host.Services.GetRequiredService<SessionReviewViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Guid sessionId)
        {
            await ViewModel.LoadSessionAsync(sessionId);
        }
        else
        {
            // No session ID provided — load the most recent session
            await ViewModel.LoadMostRecentSessionAsync();
        }
    }

    private async void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid stepId)
        {
            var step = ViewModel.Steps.FirstOrDefault(s => s.Id == stepId);
            if (step is not null)
            {
                await ViewModel.RemoveStepAsync(step);
            }
        }
    }
}
