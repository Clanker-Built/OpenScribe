using Microsoft.Extensions.Logging;
using OpenScribe.Core.Interfaces;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace OpenScribe.Processing.Services;

/// <summary>
/// On-device OCR using the Windows.Media.Ocr API (built into Windows 10/11).
/// Falls back gracefully if no OCR engine is available for the language.
/// </summary>
public class WindowsOcrProcessor : IOcrProcessor
{
    private readonly ILogger<WindowsOcrProcessor> _logger;
    private readonly OcrEngine? _ocrEngine;

    public WindowsOcrProcessor(ILogger<WindowsOcrProcessor> logger)
    {
        _logger = logger;

        // Try to create an OCR engine for the user's language
        _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();

        if (_ocrEngine is null)
            _logger.LogWarning("No OCR engine available for user's language. OCR will return empty results.");
        else
            _logger.LogInformation("OCR engine initialized for language: {Lang}", _ocrEngine.RecognizerLanguage.DisplayName);
    }

    public async Task<string> ExtractTextAsync(
        string imagePath,
        int? regionX = null,
        int? regionY = null,
        int? regionWidth = null,
        int? regionHeight = null,
        CancellationToken ct = default)
    {
        if (_ocrEngine is null)
            return string.Empty;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();

            var decoder = await BitmapDecoder.CreateAsync(stream);

            // If a region is specified, crop first
            SoftwareBitmap softwareBitmap;
            if (regionX.HasValue && regionY.HasValue && regionWidth.HasValue && regionHeight.HasValue)
            {
                var transform = new BitmapTransform
                {
                    Bounds = new BitmapBounds
                    {
                        X = (uint)Math.Max(0, regionX.Value),
                        Y = (uint)Math.Max(0, regionY.Value),
                        Width = (uint)regionWidth.Value,
                        Height = (uint)regionHeight.Value
                    }
                };
                softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);
            }
            else
            {
                softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied);
            }

            using (softwareBitmap)
            {
                var result = await _ocrEngine.RecognizeAsync(softwareBitmap);
                var text = result.Text?.Trim() ?? string.Empty;

                _logger.LogDebug("OCR extracted {Length} characters from {Path}", text.Length, imagePath);
                return text;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR failed for image: {Path}", imagePath);
            return string.Empty;
        }
    }
}
