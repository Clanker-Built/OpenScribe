namespace OpenScribe.Core.Models;

/// <summary>
/// Describes a display monitor for the capture scope picker.
/// </summary>
public record MonitorInfo(
    string DeviceName,
    string DisplayName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary)
{
    public override string ToString() => DisplayName;
}
