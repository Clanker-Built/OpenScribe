namespace OpenScribe.Core.Models;

/// <summary>
/// Describes a window for the capture scope picker.
/// </summary>
public record CaptureWindowInfo(
    IntPtr Handle,
    int ProcessId,
    string Title,
    string? ProcessName)
{
    public override string ToString() => string.IsNullOrEmpty(ProcessName)
        ? Title
        : $"{Title} ({ProcessName})";
}
