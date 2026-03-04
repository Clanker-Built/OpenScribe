using OpenScribe.Core.Enums;

namespace OpenScribe.Core.Models;

/// <summary>
/// Describes the capture scope — what portion of the screen to capture.
/// </summary>
public class CaptureScope
{
    public CaptureScopeType ScopeType { get; init; }

    // Monitor scope fields
    public int MonitorLeft { get; init; }
    public int MonitorTop { get; init; }
    public int MonitorWidth { get; init; }
    public int MonitorHeight { get; init; }
    public string? MonitorDeviceName { get; init; }

    // Window scope fields
    public IntPtr WindowHandle { get; init; }
    public int ProcessId { get; init; }
    public string? WindowTitle { get; init; }

    /// <summary>Creates a scope that captures the entire virtual screen.</summary>
    public static CaptureScope EntireScreen() => new() { ScopeType = CaptureScopeType.EntireScreen };
}
