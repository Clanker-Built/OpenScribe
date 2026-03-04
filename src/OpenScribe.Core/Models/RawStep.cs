namespace OpenScribe.Core.Models;

/// <summary>
/// Intermediate representation of a step after capture but before AI analysis.
/// Combines screenshot, click data, OCR, and transcript.
/// </summary>
public class RawStep
{
    public int SequenceNumber { get; set; }
    public TimeSpan Timestamp { get; set; }

    /// <summary>The click event that triggered this step.</summary>
    public required ClickEvent Click { get; set; }

    /// <summary>Path to the full screenshot PNG.</summary>
    public required string ScreenshotPath { get; set; }

    /// <summary>Path to the cropped screenshot around the click region.</summary>
    public string? CroppedScreenshotPath { get; set; }

    /// <summary>Full screenshot width in pixels.</summary>
    public int ScreenshotWidth { get; set; }

    /// <summary>Full screenshot height in pixels.</summary>
    public int ScreenshotHeight { get; set; }

    /// <summary>OCR text extracted from the click region.</summary>
    public string? OcrText { get; set; }

    /// <summary>Voice transcript for this step's time window.</summary>
    public string? VoiceTranscript { get; set; }

    /// <summary>Accumulated keyboard text typed during or before this step.</summary>
    public string? TypedText { get; set; }
}
