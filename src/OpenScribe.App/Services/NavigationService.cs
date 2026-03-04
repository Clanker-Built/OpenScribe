using Microsoft.UI.Xaml.Controls;

namespace OpenScribe.App.Services;

/// <summary>
/// Service for navigating between pages in the main frame.
/// </summary>
public class NavigationService
{
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set => _frame = value;
    }

    /// <summary>
    /// Reference to the NavigationView so programmatic navigation
    /// can keep the selected item in sync.
    /// </summary>
    public NavigationView? NavView { get; set; }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }

    public bool Navigate(Type pageType, object? parameter = null)
    {
        if (_frame is null)
            return false;

        return _frame.Navigate(pageType, parameter);
    }

    /// <summary>
    /// Navigate to a page and sync the NavigationView selected item by tag.
    /// </summary>
    public bool NavigateAndSelect(Type pageType, string tag, object? parameter = null)
    {
        if (_frame is null)
            return false;

        var result = _frame.Navigate(pageType, parameter);

        if (result && NavView is not null)
        {
            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == tag)
                {
                    NavView.SelectedItem = navItem;
                    break;
                }
            }
        }

        return result;
    }
}
