using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Enumerates available capture targets (monitors and windows).
/// </summary>
public interface ICaptureTargetEnumerator
{
    /// <summary>Returns information about all connected monitors.</summary>
    IReadOnlyList<MonitorInfo> EnumerateMonitors();

    /// <summary>Returns information about visible top-level windows.</summary>
    IReadOnlyList<CaptureWindowInfo> EnumerateWindows();
}
