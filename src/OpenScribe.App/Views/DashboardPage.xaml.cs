using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenScribe.App.ViewModels;
using OpenScribe.Core.Models;

namespace OpenScribe.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.Host.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadSessionsAsync();
    }

    private void NewCapture_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(CapturePage));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadSessionsAsync();
    }

    private void ReviewSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid sessionId)
        {
            Frame.Navigate(typeof(SessionReviewPage), sessionId);
        }
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid sessionId)
        {
            var session = ViewModel.RecentSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is not null)
            {
                await ViewModel.DeleteSessionAsync(session);
            }
        }
    }
}
