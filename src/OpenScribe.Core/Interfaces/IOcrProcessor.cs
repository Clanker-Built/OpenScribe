namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Extracts text from screenshot images via OCR.
/// </summary>
public interface IOcrProcessor
{
    /// <summary>
    /// Run OCR on the specified image and return extracted text.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <param name="regionX">Optional X coordinate of the region of interest.</param>
    /// <param name="regionY">Optional Y coordinate of the region of interest.</param>
    /// <param name="regionWidth">Optional width of the region of interest.</param>
    /// <param name="regionHeight">Optional height of the region of interest.</param>
    /// <returns>Extracted text from the image/region.</returns>
    Task<string> ExtractTextAsync(
        string imagePath,
        int? regionX = null,
        int? regionY = null,
        int? regionWidth = null,
        int? regionHeight = null,
        CancellationToken ct = default);
}
