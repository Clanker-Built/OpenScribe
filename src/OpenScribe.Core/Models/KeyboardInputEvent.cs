namespace OpenScribe.Core.Models;

/// <summary>
/// Represents accumulated keyboard input detected by the input hook system.
/// </summary>
public record KeyboardInputEvent(
    /// <summary>Time relative to session start.</summary>
    TimeSpan Timestamp,
    /// <summary>Accumulated text including action-key tokens like [Enter], [Tab].</summary>
    string AccumulatedText,
    /// <summary>Window title at the time the text was flushed.</summary>
    string WindowTitle,
    /// <summary>Process name of the foreground application.</summary>
    string? ApplicationName,
    /// <summary>Number of individual keystrokes that produced this text.</summary>
    int KeystrokeCount
);
