using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Capture.Interop;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.Capture.Services;

/// <summary>
/// Orchestrates the full capture process — coordinates screen capture,
/// input hooks, and audio recording into a unified session.
/// </summary>
public class CaptureSessionManager : ICaptureSessionManager
{
    private readonly IScreenRecorder _screenRecorder;
    private readonly IInputHookManager _inputHookManager;
    private readonly IAudioRecorder _audioRecorder;
    private readonly IVideoRecorder _videoRecorder;
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<CaptureSessionManager> _logger;
    private readonly OpenScribeSettings _settings;

    private CaptureSession? _activeSession;
    private CaptureScope? _captureScope;
    private string _screenshotDir = string.Empty;
    private int _stepCounter;
    private bool _recordingAudio;
    private bool _recordingVideo;
    private readonly List<ClickEvent> _clickLog = [];
    private readonly SemaphoreSlim _clickLock = new(1, 1);
    private DateTime _sessionStartTime;

    // ── Keyboard text accumulation ──────────────────────────────
    private string? _pendingTypedText;
    private int _pendingKeystrokeCount;
    private readonly object _pendingTextLock = new();

    public CaptureSession? ActiveSession => _activeSession;
    public event EventHandler<ProcessStep>? StepCaptured;

    public CaptureSessionManager(
        IScreenRecorder screenRecorder,
        IInputHookManager inputHookManager,
        IAudioRecorder audioRecorder,
        IVideoRecorder videoRecorder,
        ISessionRepository sessionRepository,
        IOptions<OpenScribeSettings> settings,
        ILogger<CaptureSessionManager> logger)
    {
        _screenRecorder = screenRecorder;
        _inputHookManager = inputHookManager;
        _audioRecorder = audioRecorder;
        _videoRecorder = videoRecorder;
        _sessionRepository = sessionRepository;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<CaptureSession> StartSessionAsync(string sessionName, bool recordAudio = true, bool recordVideo = false, CaptureScope? captureScope = null, int audioDeviceIndex = 0, CancellationToken ct = default)
    {
        if (_activeSession is not null)
            throw new InvalidOperationException("A capture session is already active. Stop it first.");

        // Create session
        var session = new CaptureSession
        {
            Name = sessionName,
            Status = SessionStatus.Recording,
            ArtifactPath = Path.Combine(_settings.DataDirectory, Guid.NewGuid().ToString("N"))
        };

        Directory.CreateDirectory(session.ArtifactPath);
        _screenshotDir = Path.Combine(session.ArtifactPath, "screenshots");
        Directory.CreateDirectory(_screenshotDir);

        // Persist
        await _sessionRepository.CreateAsync(session, ct);

        _activeSession = session;
        _captureScope = captureScope;
        _stepCounter = 0;
        _clickLog.Clear();
        _sessionStartTime = DateTime.UtcNow;

        // Wire up click and keyboard handlers and scope filtering
        _inputHookManager.CaptureScope = _captureScope;
        _inputHookManager.ClickDetected += OnClickDetected;
        _inputHookManager.TextInputDetected += OnTextInputDetected;
        _inputHookManager.Start();

        // Start screen recorder (screenshot mode)
        await _screenRecorder.StartRecordingAsync(_screenshotDir, ct);

        // Start audio recording
        if (recordAudio)
        {
            var audioPath = Path.Combine(session.ArtifactPath, "audio.wav");
            try
            {
                await _audioRecorder.StartRecordingAsync(audioPath, audioDeviceIndex, ct);
                session.AudioPath = audioPath;
                _recordingAudio = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start audio recording — continuing without audio");
                _recordingAudio = false;
            }
        }

        // Start video recording
        if (recordVideo)
        {
            var videoPath = Path.Combine(session.ArtifactPath, "video.avi");
            try
            {
                await _videoRecorder.StartRecordingAsync(videoPath, _captureScope, ct);
                session.VideoPath = videoPath;
                _recordingVideo = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start video recording — continuing without video");
                _recordingVideo = false;
            }
        }

        _logger.LogInformation("Capture session started: {Name} ({Id})", session.Name, session.Id);
        return session;
    }

    public async Task PauseSessionAsync(CancellationToken ct = default)
    {
        if (_activeSession is null)
            return;

        _inputHookManager.Stop();
        _activeSession.Status = SessionStatus.Paused;
        await _sessionRepository.UpdateAsync(_activeSession, ct);

        _logger.LogInformation("Session paused");
    }

    public async Task ResumeSessionAsync(CancellationToken ct = default)
    {
        if (_activeSession is null)
            return;

        _inputHookManager.Start();
        _activeSession.Status = SessionStatus.Recording;
        await _sessionRepository.UpdateAsync(_activeSession, ct);

        _logger.LogInformation("Session resumed");
    }

    public async Task<CaptureSession> StopSessionAsync(CancellationToken ct = default)
    {
        if (_activeSession is null)
            throw new InvalidOperationException("No active capture session to stop.");

        // Unhook
        _inputHookManager.ClickDetected -= OnClickDetected;
        _inputHookManager.TextInputDetected -= OnTextInputDetected;
        _inputHookManager.CaptureScope = null;
        _inputHookManager.Stop();

        // Stop recording
        await _screenRecorder.StopRecordingAsync(ct);

        if (_recordingAudio)
        {
            await _audioRecorder.StopRecordingAsync(ct);
            _recordingAudio = false;
        }

        if (_recordingVideo)
        {
            try
            {
                await _videoRecorder.StopRecordingAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping video recording");
            }
            _recordingVideo = false;
        }

        _activeSession.Status = SessionStatus.Captured;
        _activeSession.CompletedAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(_activeSession, ct);

        _logger.LogInformation("Session stopped: {Name} — {StepCount} steps captured",
            _activeSession.Name, _activeSession.Steps.Count);

        var completed = _activeSession;
        _activeSession = null;
        _captureScope = null;
        return completed;
    }

    public async Task CaptureManualScreenshotAsync(CancellationToken ct = default)
    {
        if (_activeSession is null)
            return;

        var scope = _captureScope;
        var screenshotPath = scope is not null
            ? await _screenRecorder.CaptureScreenshotAsync(_screenshotDir, scope, ct)
            : await _screenRecorder.CaptureScreenshotAsync(_screenshotDir, ct);

        _stepCounter++;
        var step = new ProcessStep
        {
            SessionId = _activeSession.Id,
            SequenceNumber = _stepCounter,
            Timestamp = DateTime.UtcNow - _sessionStartTime,
            ScreenshotPath = screenshotPath,
            WindowTitle = "Manual Capture"
        };

        _activeSession.Steps.Add(step);
        await _sessionRepository.AddStepAsync(step, ct);
        StepCaptured?.Invoke(this, step);
    }

    private async void OnClickDetected(object? sender, ClickEvent click)
    {
        if (_activeSession is null)
            return;

        // Flush keyboard buffer so pending text is available for this click step
        try { _inputHookManager.FlushTextBuffer(); } catch { /* best effort */ }

        await _clickLock.WaitAsync();
        try
        {
            if (_activeSession is null)
                return;

            _clickLog.Add(click);

            // Consume any pending typed text
            string? typedText;
            int keystrokeCount;
            lock (_pendingTextLock)
            {
                typedText = _pendingTypedText;
                keystrokeCount = _pendingKeystrokeCount;
                _pendingTypedText = null;
                _pendingKeystrokeCount = 0;
            }

            // Capture screenshot (scoped if configured)
            var scope = _captureScope;
            var screenshotPath = scope is not null
                ? await _screenRecorder.CaptureScreenshotAsync(_screenshotDir, scope)
                : await _screenRecorder.CaptureScreenshotAsync(_screenshotDir);

            // Translate click coordinates to image-relative space
            var (imageX, imageY) = TranslateClickToImageCoords(click.X, click.Y, scope);

            _stepCounter++;
            var step = new ProcessStep
            {
                SessionId = _activeSession.Id,
                SequenceNumber = _stepCounter,
                Timestamp = click.Timestamp,
                ClickX = imageX,
                ClickY = imageY,
                ClickType = click.ClickType,
                WindowTitle = click.WindowTitle,
                ControlName = click.ControlName,
                ApplicationName = click.ApplicationName,
                ScreenshotPath = screenshotPath,
                UiaControlType = click.UiaControlType,
                UiaElementName = click.UiaElementName,
                UiaAutomationId = click.UiaAutomationId,
                UiaClassName = click.UiaClassName,
                UiaElementBounds = click.UiaElementBounds,
                DpiScale = click.DpiScale,
                TypedText = typedText,
                KeystrokeCount = keystrokeCount
            };

            _activeSession.Steps.Add(step);
            await _sessionRepository.AddStepAsync(step);

            StepCaptured?.Invoke(this, step);

            _logger.LogDebug("Step {Seq} captured: click at ({X},{Y}) in {Window}{TypedInfo}",
                step.SequenceNumber, click.X, click.Y, click.WindowTitle,
                typedText is not null ? $" (typed: {typedText.Length} chars)" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing click event");
        }
        finally
        {
            _clickLock.Release();
        }
    }

    private async void OnTextInputDetected(object? sender, KeyboardInputEvent textEvent)
    {
        if (_activeSession is null)
            return;

        // Store as pending text — will be consumed by the next click step.
        // If no click arrives (idle timeout flush), we create a standalone typing step.
        // Use a short delay to see if a click is imminent.
        lock (_pendingTextLock)
        {
            _pendingTypedText = (_pendingTypedText ?? "") + textEvent.AccumulatedText;
            _pendingKeystrokeCount += textEvent.KeystrokeCount;
        }

        // Wait briefly to see if a click consumes this text
        await Task.Delay(200);

        // If text is still pending (no click consumed it), create a standalone typing step
        string? remainingText;
        int remainingCount;
        lock (_pendingTextLock)
        {
            remainingText = _pendingTypedText;
            remainingCount = _pendingKeystrokeCount;
            if (remainingText is null)
                return; // Click already consumed it
        }

        await _clickLock.WaitAsync();
        try
        {
            // Double-check: still pending and session still active?
            lock (_pendingTextLock)
            {
                if (_pendingTypedText is null || _activeSession is null)
                    return;
                remainingText = _pendingTypedText;
                remainingCount = _pendingKeystrokeCount;
                _pendingTypedText = null;
                _pendingKeystrokeCount = 0;
            }

            // Capture a screenshot for the standalone typing step
            var scope = _captureScope;
            var screenshotPath = scope is not null
                ? await _screenRecorder.CaptureScreenshotAsync(_screenshotDir, scope)
                : await _screenRecorder.CaptureScreenshotAsync(_screenshotDir);

            _stepCounter++;
            var step = new ProcessStep
            {
                SessionId = _activeSession.Id,
                SequenceNumber = _stepCounter,
                Timestamp = textEvent.Timestamp,
                WindowTitle = textEvent.WindowTitle,
                ApplicationName = textEvent.ApplicationName,
                ScreenshotPath = screenshotPath,
                TypedText = remainingText,
                KeystrokeCount = remainingCount
            };

            _activeSession.Steps.Add(step);
            await _sessionRepository.AddStepAsync(step);

            StepCaptured?.Invoke(this, step);

            _logger.LogDebug("Step {Seq} captured: typing in {Window} ({Count} chars)",
                step.SequenceNumber, textEvent.WindowTitle, remainingText?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing text input event");
        }
        finally
        {
            _clickLock.Release();
        }
    }

    private static (int x, int y) TranslateClickToImageCoords(int screenX, int screenY, CaptureScope? scope)
    {
        if (scope is null || scope.ScopeType == CaptureScopeType.EntireScreen)
        {
            // Translate from virtual screen coords to image coords
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            return (screenX - vs.X, screenY - vs.Y);
        }

        if (scope.ScopeType == CaptureScopeType.SingleMonitor)
        {
            return (screenX - scope.MonitorLeft, screenY - scope.MonitorTop);
        }

        // SingleWindow — offset by window rect
        if (NativeMethods.IsWindow(scope.WindowHandle) &&
            NativeMethods.GetWindowRect(scope.WindowHandle, out var rect))
        {
            return (screenX - rect.Left, screenY - rect.Top);
        }

        return (screenX, screenY);
    }
}
