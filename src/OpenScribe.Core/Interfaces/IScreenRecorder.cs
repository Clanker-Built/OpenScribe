using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Captures screenshots of the screen on demand or triggered by click events.
/// </summary>
public interface IScreenRecorder : IDisposable
{
    /// <summary>Start screen recording (video capture).</summary>
    Task StartRecordingAsync(string outputPath, CancellationToken ct = default);

    /// <summary>Stop screen recording.</summary>
    Task StopRecordingAsync(CancellationToken ct = default);

    /// <summary>Take a single screenshot and save it.</summary>
    /// <returns>Path to the saved screenshot PNG.</returns>
    Task<string> CaptureScreenshotAsync(string outputDirectory, CancellationToken ct = default);

    /// <summary>Take a scoped screenshot and save it.</summary>
    /// <returns>Path to the saved screenshot PNG.</returns>
    Task<string> CaptureScreenshotAsync(string outputDirectory, CaptureScope scope, CancellationToken ct = default);

    /// <summary>Whether recording is currently active.</summary>
    bool IsRecording { get; }
}
