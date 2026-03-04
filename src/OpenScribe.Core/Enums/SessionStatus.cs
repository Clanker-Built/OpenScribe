namespace OpenScribe.Core.Enums;

/// <summary>
/// Represents the current state of a capture session.
/// </summary>
public enum SessionStatus
{
    /// <summary>Session created but capture not yet started.</summary>
    Created = 0,

    /// <summary>Actively recording screen, clicks, and audio.</summary>
    Recording = 1,

    /// <summary>Recording paused by user.</summary>
    Paused = 2,

    /// <summary>Recording stopped; raw data captured.</summary>
    Captured = 3,

    /// <summary>Preprocessing pipeline running (OCR, transcription, timeline).</summary>
    Processing = 4,

    /// <summary>AI analysis in progress.</summary>
    Analyzing = 5,

    /// <summary>AI analysis complete; ready for user review.</summary>
    Reviewed = 6,

    /// <summary>Document has been generated and exported.</summary>
    Exported = 7,

    /// <summary>An error occurred during processing.</summary>
    Error = 99
}
