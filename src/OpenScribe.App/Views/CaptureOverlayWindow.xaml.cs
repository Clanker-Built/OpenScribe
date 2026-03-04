using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenScribe.App.ViewModels;
using WinRT.Interop;

namespace OpenScribe.App.Views;

public sealed partial class CaptureOverlayWindow : Window
{
    public CaptureOverlayViewModel ViewModel { get; }

    public CaptureOverlayWindow(CaptureOverlayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Set DataContext for {Binding} expressions in XAML
        RootGrid.DataContext = viewModel;

        // Update pause/resume icon when state changes
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CaptureOverlayViewModel.IsPaused))
            {
                PauseResumeIcon.Glyph = ViewModel.IsPaused ? "\uE768" : "\uE769";
            }
        };

        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        Title = "OpenScribe — Recording";
        ExtendsContentIntoTitleBar = true;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Always on top, no resize, no minimize/maximize
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        // Size: compact bar
        appWindow.Resize(new Windows.Graphics.SizeInt32(360, 72));

        // Position top-center of primary display
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var centerX = (displayArea.WorkArea.Width - 360) / 2;
        appWindow.Move(new Windows.Graphics.PointInt32(centerX, 8));
    }
}
