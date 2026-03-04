using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenScribe.App.Services;
using OpenScribe.App.Views;

namespace OpenScribe.App;

/// <summary>
/// Main application window with NavigationView shell.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "OpenScribe — Process Documentation";
        ExtendsContentIntoTitleBar = true;

        // Set window/taskbar icon
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "OpenScribe.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Wire up the NavigationService to the ContentFrame and NavView
        var navService = App.Host.Services.GetRequiredService<NavigationService>();
        navService.Frame = ContentFrame;
        navService.NavView = NavView;
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateIfNeeded(typeof(SettingsPage));
        }
        else if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            var pageType = tag switch
            {
                "Dashboard" => typeof(DashboardPage),
                "Capture" => typeof(CapturePage),
                "Review" => typeof(SessionReviewPage),
                "Export" => typeof(ExportPage),
                _ => typeof(DashboardPage)
            };
            NavigateIfNeeded(pageType);
        }
    }

    private void NavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        // Select Dashboard by default
        if (NavView.MenuItems[0] is NavigationViewItem firstItem)
        {
            NavView.SelectedItem = firstItem;
            ContentFrame.Navigate(typeof(DashboardPage));
        }
    }

    private void NavigateIfNeeded(Type pageType)
    {
        // Guard: skip if the frame is already showing this page type
        // (prevents double-navigation when selection is set programmatically)
        if (ContentFrame.CurrentSourcePageType == pageType)
            return;

        ContentFrame.Navigate(pageType);
    }
}
