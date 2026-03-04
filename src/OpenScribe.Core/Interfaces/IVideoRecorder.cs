using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Records screen video as an AVI file.
/// </summary>
public interface IVideoRecorder : IDisposable
{
    /// <summary>Start recording video to the specified output path.</summary>
    Task StartRecordingAsync(string outputPath, CaptureScope? scope = null, CancellationToken ct = default);

    /// <summary>Stop recording and finalize the video file.</summary>
    Task StopRecordingAsync(CancellationToken ct = default);

    /// <summary>Whether recording is currently active.</summary>
    bool IsRecording { get; }
}
