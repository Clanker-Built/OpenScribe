using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Builds a timeline of raw steps by merging click events, screenshots, OCR, and transcripts.
/// </summary>
public interface ITimelineBuilder
{
    /// <summary>
    /// Merge all captured data sources into an ordered list of raw steps.
    /// </summary>
    Task<IReadOnlyList<RawStep>> BuildTimelineAsync(
        CaptureSession session,
        CancellationToken ct = default);
}
