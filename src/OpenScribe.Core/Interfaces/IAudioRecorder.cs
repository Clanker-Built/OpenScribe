namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Records audio from a microphone.
/// </summary>
public interface IAudioRecorder : IDisposable
{
    /// <summary>Fired when an audio level sample is available (0.0–1.0 normalized RMS).</summary>
    event EventHandler<float>? LevelAvailable;

    /// <summary>Start recording audio to the specified WAV file path.</summary>
    Task StartRecordingAsync(string outputPath, int deviceIndex = 0, CancellationToken ct = default);

    /// <summary>Stop audio recording.</summary>
    Task StopRecordingAsync(CancellationToken ct = default);

    /// <summary>Whether recording is currently active.</summary>
    bool IsRecording { get; }

    /// <summary>Whether monitoring (preview-only listening) is active.</summary>
    bool IsMonitoring { get; }

    /// <summary>Start monitoring audio input for level metering (no file recording).</summary>
    Task StartMonitoringAsync(int deviceIndex = 0, CancellationToken ct = default);

    /// <summary>Stop monitoring audio input.</summary>
    Task StopMonitoringAsync(CancellationToken ct = default);

    /// <summary>Get available audio input devices.</summary>
    IReadOnlyList<AudioDevice> GetAvailableDevices();
}

/// <summary>
/// Represents an audio input device.
/// </summary>
public record AudioDevice(int DeviceIndex, string Name);
