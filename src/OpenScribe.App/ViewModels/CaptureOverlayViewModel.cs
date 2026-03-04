using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// Presentation-layer ViewModel for the capture overlay window.
/// Thin layer — no direct capture manager dependency. Events are
/// handled by CaptureViewModel which orchestrates the overlay lifecycle.
/// </summary>
public partial class CaptureOverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private int _countdownValue;

    [ObservableProperty]
    private bool _isCountingDown = true;

    [ObservableProperty]
    private string _elapsedTime = "00:00";

    [ObservableProperty]
    private int _stepCount;

    [ObservableProperty]
    private bool _isPaused;

    /// <summary>Raised when the user clicks Stop on the overlay.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Raised when the user clicks Pause on the overlay.</summary>
    public event EventHandler? PauseRequested;

    /// <summary>Raised when the user clicks Resume on the overlay.</summary>
    public event EventHandler? ResumeRequested;

    [RelayCommand]
    private void RequestStop() => StopRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
            ResumeRequested?.Invoke(this, EventArgs.Empty);
        else
            PauseRequested?.Invoke(this, EventArgs.Empty);
    }
}
