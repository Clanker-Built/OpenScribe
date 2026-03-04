using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using OpenScribe.Capture.Interop;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.Capture.Services;

/// <summary>
/// Captures screenshots using System.Drawing (GDI+).
/// Takes full-screen screenshots on demand — typically triggered by click events.
/// </summary>
public class ScreenRecorder : IScreenRecorder
{
    private readonly ILogger<ScreenRecorder> _logger;
    private bool _isRecording;
    private int _screenshotCounter;
    private bool _disposed;

    public bool IsRecording => _isRecording;

    public ScreenRecorder(ILogger<ScreenRecorder> logger)
    {
        _logger = logger;
    }

    public Task StartRecordingAsync(string outputPath, CancellationToken ct = default)
    {
        _isRecording = true;
        _screenshotCounter = 0;
        _logger.LogInformation("Screen recording started (screenshot mode). Output: {Path}", outputPath);
        return Task.CompletedTask;
    }

    public Task StopRecordingAsync(CancellationToken ct = default)
    {
        _isRecording = false;
        _logger.LogInformation("Screen recording stopped. {Count} screenshots captured", _screenshotCounter);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Capture a full-screen screenshot and save it as a PNG.
    /// </summary>
    public Task<string> CaptureScreenshotAsync(string outputDirectory, CancellationToken ct = default)
    {
        return CaptureScreenshotAsync(outputDirectory, CaptureScope.EntireScreen(), ct);
    }

    /// <summary>
    /// Capture a scoped screenshot and save it as a PNG.
    /// </summary>
    public Task<string> CaptureScreenshotAsync(string outputDirectory, CaptureScope scope, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);

        _screenshotCounter++;
        var fileName = $"step_{_screenshotCounter:D4}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png";
        var filePath = Path.Combine(outputDirectory, fileName);

        try
        {
            switch (scope.ScopeType)
            {
                case CaptureScopeType.SingleMonitor:
                    CaptureMonitor(scope, filePath);
                    break;
                case CaptureScopeType.SingleWindow:
                    CaptureWindow(scope, filePath);
                    break;
                default:
                    CaptureEntireScreen(filePath);
                    break;
            }

            _logger.LogDebug("Scoped screenshot saved: {Path} (scope={Scope})", filePath, scope.ScopeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture scoped screenshot");
            throw;
        }

        return Task.FromResult(filePath);
    }

    private void CaptureEntireScreen(string filePath)
    {
        var bounds = GetVirtualScreenBounds();

        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        bitmap.Save(filePath, ImageFormat.Png);
    }

    private void CaptureMonitor(CaptureScope scope, string filePath)
    {
        using var bitmap = new Bitmap(scope.MonitorWidth, scope.MonitorHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.CopyFromScreen(scope.MonitorLeft, scope.MonitorTop, 0, 0,
            new Size(scope.MonitorWidth, scope.MonitorHeight), CopyPixelOperation.SourceCopy);
        bitmap.Save(filePath, ImageFormat.Png);
    }

    private void CaptureWindow(CaptureScope scope, string filePath)
    {
        var hwnd = scope.WindowHandle;

        // Verify window still exists
        if (!NativeMethods.IsWindow(hwnd))
        {
            _logger.LogWarning("Target window closed — falling back to entire screen");
            CaptureEntireScreen(filePath);
            return;
        }

        // Restore if minimized
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            Thread.Sleep(200);
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            _logger.LogWarning("GetWindowRect failed — falling back to entire screen");
            CaptureEntireScreen(filePath);
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
        {
            _logger.LogWarning("Window has zero size — falling back to entire screen");
            CaptureEntireScreen(filePath);
            return;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        try
        {
            if (!NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT))
            {
                _logger.LogWarning("PrintWindow failed — falling back to entire screen");
                graphics.ReleaseHdc(hdc);
                CaptureEntireScreen(filePath);
                return;
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        bitmap.Save(filePath, ImageFormat.Png);
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
        _isRecording = false;
        GC.SuppressFinalize(this);
    }
}
