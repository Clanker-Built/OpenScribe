using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;
using OpenScribe.App.Services;
using OpenScribe.App.Views;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.App.ViewModels;

/// <summary>
/// ViewModel for the Capture screen — manages the recording lifecycle,
/// displays captured steps in real time, and orchestrates the overlay window.
/// </summary>
public partial class CaptureViewModel : ObservableObject
{
    private readonly ICaptureSessionManager _captureManager;
    private readonly IAudioRecorder _audioRecorder;
    private readonly ICaptureTargetEnumerator _targetEnumerator;
    private readonly NavigationService _navigationService;
    private readonly ILogger<CaptureViewModel> _logger;
    private DispatcherQueue? _dispatcherQueue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCaptureCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCaptureCommand))]
    private bool _isCapturing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCaptureCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private string _sessionName = string.Empty;

    [ObservableProperty]
    private bool _recordAudio = true;

    [ObservableProperty]
    private bool _recordVideo;

    [ObservableProperty]
    private int _stepCount;

    [ObservableProperty]
    private string _statusMessage = "Ready to capture";

    [ObservableProperty]
    private string _elapsedTime = "00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonitorScopeSelected))]
    [NotifyPropertyChangedFor(nameof(IsWindowScopeSelected))]
    private int _selectedScopeTypeIndex;

    [ObservableProperty]
    private MonitorInfo? _selectedMonitor;

    [ObservableProperty]
    private CaptureWindowInfo? _selectedWindow;

    [ObservableProperty]
    private AudioDevice? _selectedAudioDevice;

    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudioDevicePickerVisible))]
    private bool _isAudioDevicePickerVisibleInternal;

    public ObservableCollection<MonitorInfo> AvailableMonitors { get; } = [];
    public ObservableCollection<CaptureWindowInfo> AvailableWindows { get; } = [];
    public ObservableCollection<AudioDevice> AvailableAudioDevices { get; } = [];

    public bool IsMonitorScopeSelected => SelectedScopeTypeIndex == 1;
    public bool IsWindowScopeSelected => SelectedScopeTypeIndex == 2;
    public bool IsAudioDevicePickerVisible => IsAudioDevicePickerVisibleInternal;

    public CaptureScopeType SelectedScopeType => (CaptureScopeType)SelectedScopeTypeIndex;

    public ObservableCollection<ProcessStep> CapturedSteps { get; } = [];

    private System.Timers.Timer? _elapsedTimer;
    private DateTime _captureStartTime;

    private CaptureOverlayWindow? _overlayWindow;
    private CaptureOverlayViewModel? _overlayVm;

    public CaptureViewModel(
        ICaptureSessionManager captureManager,
        IAudioRecorder audioRecorder,
        ICaptureTargetEnumerator targetEnumerator,
        NavigationService navigationService,
        IOptions<OpenScribeSettings> appSettings,
        ILogger<CaptureViewModel> logger)
    {
        _captureManager = captureManager;
        _audioRecorder = audioRecorder;
        _targetEnumerator = targetEnumerator;
        _navigationService = navigationService;
        _logger = logger;
        RecordAudio = appSettings.Value.RecordAudioByDefault;
        RecordVideo = appSettings.Value.RecordVideoByDefault;
        // Capture the UI thread's DispatcherQueue at construction time
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _audioRecorder.LevelAvailable += OnAudioLevelAvailable;
    }

    /// <summary>
    /// Must be called from code-behind to ensure dispatcher is set from the UI thread.
    /// </summary>
    public void EnsureDispatcher(DispatcherQueue dispatcher)
    {
        _dispatcherQueue ??= dispatcher;
    }

    /// <summary>
    /// Resets all UI state for a fresh capture session.
    /// Called when navigating to the Capture page so the previous
    /// session's data doesn't bleed through (Singleton lifetime).
    /// </summary>
    public void ResetForNewCapture()
    {
        if (IsCapturing)
            return; // Don't reset while actively recording

        SessionName = string.Empty;
        StepCount = 0;
        ElapsedTime = "00:00";
        StatusMessage = "Ready to capture";
        CapturedSteps.Clear();
        IsPaused = false;
        SelectedScopeTypeIndex = 0;
        IsAudioDevicePickerVisibleInternal = RecordAudio;
        RefreshCaptureTargets();
        RefreshAudioDevices();

        if (RecordAudio)
            StartMonitoringSelectedDevice();
    }

    partial void OnRecordAudioChanged(bool value)
    {
        IsAudioDevicePickerVisibleInternal = value;

        if (value)
        {
            if (AvailableAudioDevices.Count == 0)
                RefreshAudioDevices();
            if (!IsCapturing)
                StartMonitoringSelectedDevice();
        }
        else
        {
            if (!IsCapturing)
                _ = _audioRecorder.StopMonitoringAsync();
            AudioLevel = 0;
        }
    }

    partial void OnSelectedAudioDeviceChanged(AudioDevice? value)
    {
        if (value is not null && RecordAudio && !IsCapturing)
            StartMonitoringSelectedDevice();
    }

    private bool CanStart => !IsCapturing;
    private bool CanStop => IsCapturing;
    private bool CanPause => IsCapturing && !IsPaused;
    private bool CanResume => IsCapturing && IsPaused;

    [RelayCommand]
    public void RefreshCaptureTargets()
    {
        try
        {
            AvailableMonitors.Clear();
            foreach (var monitor in _targetEnumerator.EnumerateMonitors())
                AvailableMonitors.Add(monitor);
            if (AvailableMonitors.Count > 0 && SelectedMonitor is null)
                SelectedMonitor = AvailableMonitors[0];

            AvailableWindows.Clear();
            foreach (var window in _targetEnumerator.EnumerateWindows())
                AvailableWindows.Add(window);
            if (AvailableWindows.Count > 0 && SelectedWindow is null)
                SelectedWindow = AvailableWindows[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate capture targets");
        }
    }

    [RelayCommand]
    public void RefreshAudioDevices()
    {
        try
        {
            AvailableAudioDevices.Clear();
            foreach (var device in _audioRecorder.GetAvailableDevices())
                AvailableAudioDevices.Add(device);
            if (AvailableAudioDevices.Count > 0 && SelectedAudioDevice is null)
                SelectedAudioDevice = AvailableAudioDevices[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate audio devices");
        }
    }

    private void StartMonitoringSelectedDevice()
    {
        try
        {
            var deviceIndex = SelectedAudioDevice?.DeviceIndex ?? 0;
            _ = _audioRecorder.StopMonitoringAsync();
            _ = _audioRecorder.StartMonitoringAsync(deviceIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start audio monitoring");
        }
    }

    public async Task StopMonitoringForNavigationAsync()
    {
        try
        {
            await _audioRecorder.StopMonitoringAsync();
            AudioLevel = 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping audio monitoring on navigation");
        }
    }

    private void OnAudioLevelAvailable(object? sender, float level)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            AudioLevel = level * 100.0;
        });
    }

    private CaptureScope BuildCaptureScope()
    {
        switch (SelectedScopeType)
        {
            case CaptureScopeType.SingleMonitor when SelectedMonitor is not null:
                return new CaptureScope
                {
                    ScopeType = CaptureScopeType.SingleMonitor,
                    MonitorLeft = SelectedMonitor.Left,
                    MonitorTop = SelectedMonitor.Top,
                    MonitorWidth = SelectedMonitor.Width,
                    MonitorHeight = SelectedMonitor.Height,
                    MonitorDeviceName = SelectedMonitor.DeviceName
                };
            case CaptureScopeType.SingleWindow when SelectedWindow is not null:
                return new CaptureScope
                {
                    ScopeType = CaptureScopeType.SingleWindow,
                    WindowHandle = SelectedWindow.Handle,
                    ProcessId = SelectedWindow.ProcessId,
                    WindowTitle = SelectedWindow.Title
                };
            default:
                return CaptureScope.EntireScreen();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartCaptureAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SessionName))
                SessionName = $"Session {DateTime.Now:yyyy-MM-dd HH:mm}";

            // Stop monitoring — recording will take over level reporting
            await _audioRecorder.StopMonitoringAsync();

            // Create and show overlay with countdown
            _overlayVm = new CaptureOverlayViewModel { CountdownValue = 3, IsCountingDown = true };
            _overlayVm.StopRequested += OnOverlayStopRequested;
            _overlayVm.PauseRequested += OnOverlayPauseRequested;
            _overlayVm.ResumeRequested += OnOverlayResumeRequested;

            _overlayWindow = new CaptureOverlayWindow(_overlayVm);
            _overlayWindow.Activate();

            // 3-2-1 countdown
            for (var i = 3; i >= 1; i--)
            {
                _overlayVm.CountdownValue = i;
                await Task.Delay(1000);
            }
            _overlayVm.IsCountingDown = false;

            // Start capture
            var scope = BuildCaptureScope();
            var deviceIndex = SelectedAudioDevice?.DeviceIndex ?? 0;
            _captureManager.StepCaptured += OnStepCaptured;
            await _captureManager.StartSessionAsync(SessionName, RecordAudio, RecordVideo, scope, deviceIndex);

            IsCapturing = true;
            IsPaused = false;
            StepCount = 0;
            CapturedSteps.Clear();
            StatusMessage = "Recording... Click anywhere to capture steps.";

            _captureStartTime = DateTime.UtcNow;
            StartElapsedTimer();

            _logger.LogInformation("Capture started: {Name}", SessionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start capture");
            StatusMessage = $"Error: {ex.Message}";
            CloseOverlay();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    public async Task StopCaptureAsync()
    {
        try
        {
            _captureManager.StepCaptured -= OnStepCaptured;
            var session = await _captureManager.StopSessionAsync();

            IsCapturing = false;
            IsPaused = false;
            StopElapsedTimer();

            StatusMessage = $"Capture complete: {session.Steps.Count} steps captured.";
            _logger.LogInformation("Capture stopped: {Count} steps", session.Steps.Count);

            // Resume monitoring after recording stops
            if (RecordAudio)
                StartMonitoringSelectedDevice();

            CloseOverlay();
            BringMainWindowToForeground();

            // Auto-navigate to Review page with this session
            _navigationService.NavigateAndSelect(
                typeof(SessionReviewPage), "Review", session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop capture");
            StatusMessage = $"Error: {ex.Message}";
            CloseOverlay();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    public async Task PauseCaptureAsync()
    {
        await _captureManager.PauseSessionAsync();
        IsPaused = true;
        StatusMessage = "Paused";
        if (_overlayVm is not null)
            _overlayVm.IsPaused = true;
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    public async Task ResumeCaptureAsync()
    {
        await _captureManager.ResumeSessionAsync();
        IsPaused = false;
        StatusMessage = "Recording...";
        if (_overlayVm is not null)
            _overlayVm.IsPaused = false;
    }

    [RelayCommand]
    public async Task CaptureManualScreenshotAsync()
    {
        if (IsCapturing)
            await _captureManager.CaptureManualScreenshotAsync();
    }

    private void OnOverlayStopRequested(object? sender, EventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() => _ = StopCaptureAsync());
    }

    private void OnOverlayPauseRequested(object? sender, EventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() => _ = PauseCaptureAsync());
    }

    private void OnOverlayResumeRequested(object? sender, EventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() => _ = ResumeCaptureAsync());
    }

    private void OnStepCaptured(object? sender, ProcessStep step)
    {
        // Marshal to UI thread using stored dispatcher reference
        _dispatcherQueue?.TryEnqueue(() =>
        {
            CapturedSteps.Add(step);
            StepCount = CapturedSteps.Count;
            StatusMessage = $"Recording... {StepCount} step(s) captured.";

            // Forward to overlay
            if (_overlayVm is not null)
                _overlayVm.StepCount = StepCount;
        });
    }

    private void StartElapsedTimer()
    {
        _elapsedTimer = new System.Timers.Timer(1000);
        _elapsedTimer.Elapsed += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _captureStartTime;
            // Marshal timer update to UI thread
            _dispatcherQueue?.TryEnqueue(() =>
            {
                ElapsedTime = elapsed.ToString(@"mm\:ss");

                // Forward to overlay
                if (_overlayVm is not null)
                    _overlayVm.ElapsedTime = ElapsedTime;
            });
        };
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer?.Dispose();
        _elapsedTimer = null;
    }

    private void CloseOverlay()
    {
        if (_overlayVm is not null)
        {
            _overlayVm.StopRequested -= OnOverlayStopRequested;
            _overlayVm.PauseRequested -= OnOverlayPauseRequested;
            _overlayVm.ResumeRequested -= OnOverlayResumeRequested;
            _overlayVm = null;
        }

        _overlayWindow?.Close();
        _overlayWindow = null;
    }

    private static void BringMainWindowToForeground()
    {
        App.MainWindow?.Activate();
    }
}
