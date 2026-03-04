using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Orchestrates the full capture process — coordinates screen recording,
/// input hooks, and audio recording into a unified session.
/// </summary>
public interface ICaptureSessionManager
{
    /// <summary>Create and start a new capture session.</summary>
    Task<CaptureSession> StartSessionAsync(string sessionName, bool recordAudio = true, bool recordVideo = false, CaptureScope? captureScope = null, int audioDeviceIndex = 0, CancellationToken ct = default);

    /// <summary>Pause the current session.</summary>
    Task PauseSessionAsync(CancellationToken ct = default);

    /// <summary>Resume a paused session.</summary>
    Task ResumeSessionAsync(CancellationToken ct = default);

    /// <summary>Stop the current session and finalize capture data.</summary>
    Task<CaptureSession> StopSessionAsync(CancellationToken ct = default);

    /// <summary>Manually trigger a screenshot capture outside of a click event.</summary>
    Task CaptureManualScreenshotAsync(CancellationToken ct = default);

    /// <summary>The active session, or null if none.</summary>
    CaptureSession? ActiveSession { get; }

    /// <summary>Fired when a step is captured during recording.</summary>
    event EventHandler<ProcessStep>? StepCaptured;
}
