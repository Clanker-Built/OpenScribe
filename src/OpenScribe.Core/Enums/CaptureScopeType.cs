namespace OpenScribe.Core.Enums;

/// <summary>
/// Determines what area of the screen is captured during a session.
/// </summary>
public enum CaptureScopeType
{
    /// <summary>Capture the entire virtual screen (all monitors).</summary>
    EntireScreen = 0,

    /// <summary>Capture a single monitor.</summary>
    SingleMonitor = 1,

    /// <summary>Capture a single window.</summary>
    SingleWindow = 2
}
