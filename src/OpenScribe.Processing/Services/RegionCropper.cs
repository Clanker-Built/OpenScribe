using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Core.Configuration;

namespace OpenScribe.Processing.Services;

/// <summary>
/// Crops a region around a click point from a full screenshot.
/// Used to isolate the area of interest for OCR and AI analysis.
/// </summary>
public class RegionCropper
{
    private readonly ILogger<RegionCropper> _logger;
    private readonly int _regionSize;

    public RegionCropper(ILogger<RegionCropper> logger, IOptions<OpenScribeSettings> settings)
    {
        _logger = logger;
        _regionSize = settings.Value.CropRegionSize;
    }

    /// <summary>
    /// Crop a region around the specified click coordinates from the screenshot.
    /// </summary>
    /// <param name="screenshotPath">Path to the full screenshot.</param>
    /// <param name="clickX">X coordinate of the click.</param>
    /// <param name="clickY">Y coordinate of the click.</param>
    /// <param name="outputPath">Path to save the cropped image.</param>
    /// <returns>The actual crop rectangle used.</returns>
    public Rectangle CropAroundClick(string screenshotPath, int clickX, int clickY, string outputPath)
    {
        using var source = new Bitmap(screenshotPath);

        // Calculate crop bounds, clamped to image dimensions
        var halfSize = _regionSize / 2;
        var x = Math.Max(0, clickX - halfSize);
        var y = Math.Max(0, clickY - halfSize);
        var width = Math.Min(_regionSize, source.Width - x);
        var height = Math.Min(_regionSize, source.Height - y);

        var cropRect = new Rectangle(x, y, width, height);

        using var cropped = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(cropped);
        g.DrawImage(source, new Rectangle(0, 0, width, height), cropRect, GraphicsUnit.Pixel);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        cropped.Save(outputPath, ImageFormat.Png);

        _logger.LogDebug("Cropped region ({X},{Y} {W}x{H}) saved to {Path}",
            x, y, width, height, outputPath);

        return cropRect;
    }
}
