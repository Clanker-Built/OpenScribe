using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Capture.Interop;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;
using SharpAvi;
using SharpAvi.Output;
using SharpAvi.Codecs;

namespace OpenScribe.Capture.Services;

/// <summary>
/// Records the screen as an AVI file using SharpAvi with Motion-JPEG codec.
/// Captures frames at a configurable FPS using the same GDI+ approach as ScreenRecorder.
/// </summary>
public class VideoRecorder : IVideoRecorder
{
    private readonly ILogger<VideoRecorder> _logger;
    private readonly int _frameRate;
    private readonly int _quality;
    private bool _disposed;

    private AviWriter? _writer;
    private IAviVideoStream? _videoStream;
    private System.Threading.Timer? _frameTimer;
    private CaptureScope? _scope;
    private bool _isRecording;
    private readonly object _writeLock = new();

    public bool IsRecording => _isRecording;

    public VideoRecorder(ILogger<VideoRecorder> logger, IOptions<OpenScribeSettings> settings)
    {
        _logger = logger;
        _frameRate = settings.Value.VideoFrameRate;
        _quality = settings.Value.VideoQuality;
    }

    public Task StartRecordingAsync(string outputPath, CaptureScope? scope = null, CancellationToken ct = default)
    {
        if (_isRecording)
            throw new InvalidOperationException("Video recording is already active.");

        _scope = scope;

        // Determine capture dimensions
        var (width, height) = GetCaptureDimensions(scope);

        // Ensure dimensions are even (required by many codecs)
        width = width % 2 == 0 ? width : width + 1;
        height = height % 2 == 0 ? height : height + 1;

        _writer = new AviWriter(outputPath)
        {
            FramesPerSecond = _frameRate,
            EmitIndex1 = true
        };

        _videoStream = _writer.AddMJpegWpfVideoStream(width, height, _quality);
        _videoStream.Name = "Screen Capture";

        _isRecording = true;

        // Start frame capture timer
        var intervalMs = 1000 / _frameRate;
        _frameTimer = new System.Threading.Timer(CaptureFrame, null, 0, intervalMs);

        _logger.LogInformation("Video recording started: {Path} ({Width}x{Height} @ {FPS}fps)",
            outputPath, width, height, _frameRate);

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync(CancellationToken ct = default)
    {
        if (!_isRecording)
            return Task.CompletedTask;

        _isRecording = false;

        _frameTimer?.Dispose();
        _frameTimer = null;

        lock (_writeLock)
        {
            try
            {
                _writer?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing AVI writer");
            }
            _writer = null;
            _videoStream = null;
        }

        _logger.LogInformation("Video recording stopped");
        return Task.CompletedTask;
    }

    private void CaptureFrame(object? state)
    {
        if (!_isRecording)
            return;

        try
        {
            var scope = _scope;
            var (width, height) = GetCaptureDimensions(scope);

            // Ensure dimensions match stream (even)
            width = width % 2 == 0 ? width : width + 1;
            height = height % 2 == 0 ? height : height + 1;

            using var bitmap = CaptureScreen(scope, width, height);
            if (bitmap is null)
                return;

            // Convert bitmap to byte array (BGR32 for SharpAvi)
            var bits = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var bufferSize = bits.Stride * bits.Height;
                var frameData = new byte[bufferSize];
                Marshal.Copy(bits.Scan0, frameData, 0, bufferSize);

                lock (_writeLock)
                {
                    if (_videoStream is not null && _isRecording)
                    {
                        _videoStream.WriteFrame(true, frameData, 0, frameData.Length);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bits);
            }
        }
        catch (Exception ex)
        {
            // Frame drop — log at debug level to avoid flooding
            _logger.LogDebug(ex, "Frame capture failed (frame dropped)");
        }
    }

    private Bitmap? CaptureScreen(CaptureScope? scope, int width, int height)
    {
        if (scope is null || scope.ScopeType == CaptureScopeType.EntireScreen)
        {
            var bounds = GetVirtualScreenBounds();
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0,
                new Size(width, height), CopyPixelOperation.SourceCopy);
            return bitmap;
        }

        if (scope.ScopeType == CaptureScopeType.SingleMonitor)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(scope.MonitorLeft, scope.MonitorTop, 0, 0,
                new Size(width, height), CopyPixelOperation.SourceCopy);
            return bitmap;
        }

        if (scope.ScopeType == CaptureScopeType.SingleWindow)
        {
            var hwnd = scope.WindowHandle;
            if (!NativeMethods.IsWindow(hwnd))
            {
                // Window closed — fall back to entire screen
                var bounds = GetVirtualScreenBounds();
                var fallback = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(fallback);
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                return fallback;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                return null;

            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0)
                return null;

            var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
            return bitmap;
        }

        return null;
    }

    private static (int width, int height) GetCaptureDimensions(CaptureScope? scope)
    {
        if (scope is null || scope.ScopeType == CaptureScopeType.EntireScreen)
        {
            var bounds = GetVirtualScreenBounds();
            return (bounds.Width, bounds.Height);
        }

        if (scope.ScopeType == CaptureScopeType.SingleMonitor)
        {
            return (scope.MonitorWidth, scope.MonitorHeight);
        }

        if (scope.ScopeType == CaptureScopeType.SingleWindow)
        {
            if (NativeMethods.IsWindow(scope.WindowHandle) &&
                NativeMethods.GetWindowRect(scope.WindowHandle, out var rect))
            {
                var w = rect.Right - rect.Left;
                var h = rect.Bottom - rect.Top;
                if (w > 0 && h > 0)
                    return (w, h);
            }
            // Fall back to entire screen if window isn't available
            var bounds = GetVirtualScreenBounds();
            return (bounds.Width, bounds.Height);
        }

        var fb = GetVirtualScreenBounds();
        return (fb.Width, fb.Height);
    }

    private static Rectangle GetVirtualScreenBounds()
    {
        return new Rectangle(
            System.Windows.Forms.SystemInformation.VirtualScreen.X,
            System.Windows.Forms.SystemInformation.VirtualScreen.Y,
            System.Windows.Forms.SystemInformation.VirtualScreen.Width,
            System.Windows.Forms.SystemInformation.VirtualScreen.Height);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_isRecording)
            StopRecordingAsync().GetAwaiter().GetResult();

        GC.SuppressFinalize(this);
    }
}
