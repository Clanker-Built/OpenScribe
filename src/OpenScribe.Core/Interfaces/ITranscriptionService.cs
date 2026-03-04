namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Transcribes audio speech to text.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribe an audio file to text with timestamps.
    /// </summary>
    /// <param name="audioFilePath">Path to the WAV/FLAC audio file.</param>
    /// <returns>List of transcript segments with timestamps.</returns>
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        string audioFilePath,
        CancellationToken ct = default);

    /// <summary>
    /// Get transcript text for a specific time range.
    /// </summary>
    Task<string> GetTranscriptForRangeAsync(
        string audioFilePath,
        TimeSpan start,
        TimeSpan end,
        CancellationToken ct = default);
}

/// <summary>
/// A timed segment of transcribed speech.
/// </summary>
public record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    float Confidence);
