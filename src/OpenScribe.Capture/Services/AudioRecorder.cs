using Microsoft.Extensions.Logging;
using NAudio.Wave;
using OpenScribe.Core.Interfaces;

namespace OpenScribe.Capture.Services;

/// <summary>
/// Records audio from a microphone using NAudio.
/// Outputs a WAV file that can later be sent for transcription.
/// </summary>
public class AudioRecorder : IAudioRecorder
{
    private readonly ILogger<AudioRecorder> _logger;
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private WaveInEvent? _monitor;
    private bool _disposed;

    public bool IsRecording => _waveIn is not null;
    public bool IsMonitoring => _monitor is not null;

    public event EventHandler<float>? LevelAvailable;

    public AudioRecorder(ILogger<AudioRecorder> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<AudioDevice> GetAvailableDevices()
    {
        var devices = new List<AudioDevice>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            devices.Add(new AudioDevice(i, caps.ProductName));
        }
        return devices;
    }

    public Task StartMonitoringAsync(int deviceIndex = 0, CancellationToken ct = default)
    {
        StopMonitorInternal();

        _monitor = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = new WaveFormat(44100, 16, 1),
            BufferMilliseconds = 100
        };

        _monitor.DataAvailable += (_, e) =>
        {
            FireLevelFromBuffer(e.Buffer, e.BytesRecorded);
        };

        _monitor.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
                _logger.LogWarning(e.Exception, "Audio monitor stopped with error");
        };

        _monitor.StartRecording();
        _logger.LogInformation("Audio monitoring started on device {DeviceIndex}", deviceIndex);

        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync(CancellationToken ct = default)
    {
        StopMonitorInternal();
        return Task.CompletedTask;
    }

    public Task StartRecordingAsync(string outputPath, int deviceIndex = 0, CancellationToken ct = default)
    {
        if (IsRecording)
            throw new InvalidOperationException("Audio recording is already in progress.");

        // Stop any active monitor — recording takes over level reporting
        StopMonitorInternal();

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = new WaveFormat(44100, 16, 1), // 44.1 kHz, 16-bit, mono
            BufferMilliseconds = 100
        };

        _writer = new WaveFileWriter(outputPath, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (_, e) =>
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            FireLevelFromBuffer(e.Buffer, e.BytesRecorded);
        };

        _waveIn.RecordingStopped += (_, e) =>
        {
            _writer?.Dispose();
            _writer = null;

            if (e.Exception is not null)
                _logger.LogError(e.Exception, "Audio recording stopped with error");
            else
                _logger.LogInformation("Audio recording stopped");
        };

        _waveIn.StartRecording();
        _logger.LogInformation("Audio recording started: {Path} (device {DeviceIndex})", outputPath, deviceIndex);

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync(CancellationToken ct = default)
    {
        if (_waveIn is null)
            return Task.CompletedTask;

        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;

        return Task.CompletedTask;
    }

    private void FireLevelFromBuffer(byte[] buffer, int bytesRecorded)
    {
        // Compute RMS from 16-bit PCM samples
        long sumOfSquares = 0;
        var sampleCount = bytesRecorded / 2;
        if (sampleCount == 0)
            return;

        for (var i = 0; i < bytesRecorded - 1; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumOfSquares += (long)sample * sample;
        }

        var rms = Math.Sqrt(sumOfSquares / (double)sampleCount) / short.MaxValue;
        // Scale up for speech visibility and clamp
        var level = (float)Math.Min(rms * 3.0, 1.0);
        LevelAvailable?.Invoke(this, level);
    }

    private void StopMonitorInternal()
    {
        if (_monitor is null)
            return;

        try
        {
            _monitor.StopRecording();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping audio monitor");
        }
        _monitor.Dispose();
        _monitor = null;

        _logger.LogInformation("Audio monitoring stopped");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        StopMonitorInternal();

        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _writer?.Dispose();

        GC.SuppressFinalize(this);
    }
}
