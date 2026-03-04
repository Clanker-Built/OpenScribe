namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Result of a smart-crop operation, including the output path and the offset
/// of the crop region relative to the original image.
/// </summary>
public record CropResult(string OutputPath, int OffsetX, int OffsetY);

/// <summary>
/// Annotates screenshot images with highlights, arrows, and step number badges.
/// </summary>
public interface IScreenshotAnnotator
{
    /// <summary>
    /// Annotate a screenshot with a highlight rectangle and step number badge.
    /// Applies a dim overlay outside the highlight region.
    /// </summary>
    Task AnnotateAsync(
        string inputPath,
        string outputPath,
        int stepNumber,
        int highlightX,
        int highlightY,
        int highlightWidth,
        int highlightHeight,
        CancellationToken ct = default);

    /// <summary>
    /// Smart-crop a screenshot around a highlight region, producing a zoomed-in detail image.
    /// Returns the crop offset so highlight coordinates can be translated.
    /// </summary>
    Task<CropResult> CropToDetailAsync(
        string inputPath,
        string outputPath,
        int highlightX,
        int highlightY,
        int highlightWidth,
        int highlightHeight,
        CancellationToken ct = default);

    /// <summary>
    /// Annotate a cropped detail image with a highlight rectangle and step badge.
    /// No dim overlay is applied — the crop itself provides focus.
    /// </summary>
    Task AnnotateDetailAsync(
        string inputPath,
        string outputPath,
        int stepNumber,
        int highlightX,
        int highlightY,
        int highlightWidth,
        int highlightHeight,
        int cropOffsetX,
        int cropOffsetY,
        CancellationToken ct = default);
}
