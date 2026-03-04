using Microsoft.Extensions.Logging;
using OpenScribe.Core.Interfaces;
using SkiaSharp;

namespace OpenScribe.DocGen.Services;

/// <summary>
/// Annotates screenshots with highlight rectangles, step number badges,
/// and optional arrows to make click targets visually clear in the document.
/// Supports smart cropping for detail views.
/// </summary>
public class ScreenshotAnnotator : IScreenshotAnnotator
{
    private readonly ILogger<ScreenshotAnnotator> _logger;

    // Visual styling constants — scaled relative to image size for consistency
    private const float BaseBadgeRadius = 20f;
    private const float BaseBadgeFontSize = 18f;
    private const float BadgeMargin = 8f;
    private const int ReferenceImageWidth = 1920; // Baseline for scaling

    private static readonly SKColor BadgeColor = new(255, 40, 40, 255);        // Solid red
    private static readonly SKColor BadgeTextColor = SKColors.White;
    private static readonly SKColor ShadowColor = new(0, 0, 0, 80);

    public ScreenshotAnnotator(ILogger<ScreenshotAnnotator> logger)
    {
        _logger = logger;
    }

    public Task AnnotateAsync(
        string inputPath,
        string outputPath,
        int stepNumber,
        int highlightX,
        int highlightY,
        int highlightWidth,
        int highlightHeight,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            using var inputStream = File.OpenRead(inputPath);
            using var original = SKBitmap.Decode(inputStream);

            if (original is null)
                throw new InvalidOperationException($"Failed to decode image: {inputPath}");

            using var surface = SKSurface.Create(new SKImageInfo(original.Width, original.Height));
            var canvas = surface.Canvas;

            // Calculate scale factor relative to reference image size
            var scaleFactor = Math.Max(1f, original.Width / (float)ReferenceImageWidth);
            var badgeRadius = BaseBadgeRadius * scaleFactor;
            var badgeFontSize = BaseBadgeFontSize * scaleFactor;

            // Draw the original screenshot
            canvas.DrawBitmap(original, 0, 0);

            // Draw subtle dim overlay outside the highlight region (23% opacity)
            DrawDimOverlay(canvas, original.Width, original.Height,
                highlightX, highlightY, highlightWidth, highlightHeight);

            // Draw highlight rectangle with improved contrast styling
            DrawHighlightRect(canvas, highlightX, highlightY, highlightWidth, highlightHeight, scaleFactor);

            // Draw step number badge
            DrawStepBadge(canvas, stepNumber, highlightX, highlightY, badgeRadius, badgeFontSize);

            // Save the annotated image
            SaveImage(surface, outputPath);

            _logger.LogDebug("Annotated screenshot saved: {Path} (step {Step})", outputPath, stepNumber);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to annotate screenshot: {Path}", inputPath);
            // Fall back: just copy the original
            File.Copy(inputPath, outputPath, overwrite: true);
        }

        return Task.CompletedTask;
    }

    public Task<CropResult> CropToDetailAsync(
        string inputPath,
        string outputPath,
        int highlightX,
        int highlightY,
        int highlightWidth,
        int highlightHeight,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            using var inputStream = File.OpenRead(inputPath);
            using var original = SKBitmap.Decode(inputStream);

            if (original is null)
                throw new InvalidOperationException($"Failed to decode image: {inputPath}");

            var imgW = original.Width;
            var imgH = original.Height;

            // If the input region is already large (AI-driven crop region, >400px in both
            // dimensions), use a small margin since it's already properly sized.
            // Otherwise expand by 1.5x the largest dimension, clamped 150–400px.
            var isPreSized = highlightWidth > 400 && highlightHeight > 400;
            var maxDim = Math.Max(highlightWidth, highlightHeight);
            var margin = isPreSized
                ? Math.Clamp((int)(maxDim * 0.1), 20, 80)
                : Math.Clamp((int)(maxDim * 1.5), 150, 400);

            var cropX = Math.Max(0, highlightX - margin);
            var cropY = Math.Max(0, highlightY - margin);
            var cropRight = Math.Min(imgW, highlightX + highlightWidth + margin);
            var cropBottom = Math.Min(imgH, highlightY + highlightHeight + margin);
            var cropW = cropRight - cropX;
            var cropH = cropBottom - cropY;

            // Enforce minimum crop size 600x400
            if (cropW < 600)
            {
                var expand = (600 - cropW) / 2;
                cropX = Math.Max(0, cropX - expand);
                cropW = Math.Min(600, imgW - cropX);
            }
            if (cropH < 400)
            {
                var expand = (400 - cropH) / 2;
                cropY = Math.Max(0, cropY - expand);
                cropH = Math.Min(400, imgH - cropY);
            }

            // If crop would be >70% of image in both dimensions, skip cropping
            if (cropW > imgW * 0.7 && cropH > imgH * 0.7)
            {
                _logger.LogDebug("Crop region too large ({W}x{H} vs {IW}x{IH}), skipping crop",
                    cropW, cropH, imgW, imgH);
                return Task.FromResult(new CropResult(inputPath, 0, 0));
            }

            // Enforce maximum 60% of image in each dimension
            if (cropW > imgW * 0.6)
            {
                cropW = (int)(imgW * 0.6);
                cropX = Math.Max(0, highlightX + highlightWidth / 2 - cropW / 2);
                if (cropX + cropW > imgW) cropX = imgW - cropW;
            }
            if (cropH > imgH * 0.6)
            {
                cropH = (int)(imgH * 0.6);
                cropY = Math.Max(0, highlightY + highlightHeight / 2 - cropH / 2);
                if (cropY + cropH > imgH) cropY = imgH - cropH;
            }

            // Perform the crop
            var cropRect = new SKRectI(cropX, cropY, cropX + cropW, cropY + cropH);
            using var cropped = new SKBitmap(cropW, cropH);
            using var croppedCanvas = new SKCanvas(cropped);
            croppedCanvas.DrawBitmap(original, cropRect, new SKRect(0, 0, cropW, cropH));

            // Save cropped image
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var image = SKImage.FromBitmap(cropped);
            using var data = image.Encode(SKEncodedImageFormat.Png, 95);
            using var outputStream = File.Create(outputPath);
            data.SaveTo(outputStream);

            _logger.LogDebug("Cropped detail saved: {Path} (offset {X},{Y} size {W}x{H})",
                outputPath, cropX, cropY, cropW, cropH);

            return Task.FromResult(new CropResult(outputPath, cropX, cropY));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to crop screenshot: {Path}", inputPath);
            return Task.FromResult(new CropResult(inputPath, 0, 0));
        }
    }

    public Task AnnotateDetailAsync(
        string inputPath,
        string outputPath,
        int stepNumber,
        int highlightX,
        int highlightY,
        int highlightWidth,
        int highlightHeight,
        int cropOffsetX,
        int cropOffsetY,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            using var inputStream = File.OpenRead(inputPath);
            using var original = SKBitmap.Decode(inputStream);

            if (original is null)
                throw new InvalidOperationException($"Failed to decode image: {inputPath}");

            using var surface = SKSurface.Create(new SKImageInfo(original.Width, original.Height));
            var canvas = surface.Canvas;

            var scaleFactor = Math.Max(1f, original.Width / (float)ReferenceImageWidth);
            var badgeRadius = BaseBadgeRadius * scaleFactor;
            var badgeFontSize = BaseBadgeFontSize * scaleFactor;

            // Draw the cropped image
            canvas.DrawBitmap(original, 0, 0);

            // Translate highlight coords by subtracting crop offset
            var localX = highlightX - cropOffsetX;
            var localY = highlightY - cropOffsetY;

            // NO dim overlay on detail images — the crop itself is the focus

            // Draw highlight rectangle
            DrawHighlightRect(canvas, localX, localY, highlightWidth, highlightHeight, scaleFactor);

            // Draw step number badge
            DrawStepBadge(canvas, stepNumber, localX, localY, badgeRadius, badgeFontSize);

            // Save the annotated detail image
            SaveImage(surface, outputPath);

            _logger.LogDebug("Annotated detail saved: {Path} (step {Step})", outputPath, stepNumber);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to annotate detail screenshot: {Path}", inputPath);
            File.Copy(inputPath, outputPath, overwrite: true);
        }

        return Task.CompletedTask;
    }

    // ── Drawing Helpers ─────────────────────────────────────────────

    private static void DrawDimOverlay(SKCanvas canvas, int width, int height,
        int hx, int hy, int hw, int hh)
    {
        using var dimPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 60), // Subtle 23% opacity dim
            Style = SKPaintStyle.Fill
        };

        // Draw four rectangles around the highlight region
        canvas.DrawRect(0, 0, width, hy, dimPaint);                          // Top
        canvas.DrawRect(0, hy + hh, width, height - hy - hh, dimPaint);     // Bottom
        canvas.DrawRect(0, hy, hx, hh, dimPaint);                            // Left
        canvas.DrawRect(hx + hw, hy, width - hx - hw, hh, dimPaint);        // Right
    }

    private static void DrawHighlightRect(SKCanvas canvas, int x, int y, int w, int h, float scaleFactor)
    {
        // Scale corner radius with element size
        var cornerRadius = Math.Min(8f, Math.Min(w, h) / 8f);

        // Shadow
        using var shadowPaint = new SKPaint
        {
            Color = ShadowColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (6f + 3f + 4f) * scaleFactor, // covers both strokes + offset
            IsAntialias = true
        };
        canvas.DrawRoundRect(x - 2, y - 2, w + 4, h + 4, cornerRadius + 2, cornerRadius + 2, shadowPaint);

        // White outer stroke (6px) for contrast on any background
        var outerStrokeWidth = 6f * scaleFactor;
        using var outerPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = outerStrokeWidth,
            IsAntialias = true
        };
        var outerOffset = outerStrokeWidth / 2;
        canvas.DrawRoundRect(
            x - outerOffset, y - outerOffset,
            w + outerStrokeWidth, h + outerStrokeWidth,
            cornerRadius + 2, cornerRadius + 2, outerPaint);

        // Red inner stroke (3px)
        var innerStrokeWidth = 3f * scaleFactor;
        using var innerPaint = new SKPaint
        {
            Color = new SKColor(255, 40, 40, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = innerStrokeWidth,
            IsAntialias = true
        };
        canvas.DrawRoundRect(x, y, w, h, cornerRadius, cornerRadius, innerPaint);
    }

    private static void DrawStepBadge(SKCanvas canvas, int stepNumber, int x, int y,
        float badgeRadius, float badgeFontSize)
    {
        // Position badge above-left of the highlight
        var badgeCenterX = x - BadgeMargin;
        var badgeCenterY = y - BadgeMargin;

        // Ensure badge stays in frame
        badgeCenterX = Math.Max(badgeRadius + 2, badgeCenterX);
        badgeCenterY = Math.Max(badgeRadius + 2, badgeCenterY);

        // Shadow
        using var shadowPaint = new SKPaint
        {
            Color = ShadowColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(badgeCenterX + 2, badgeCenterY + 2, badgeRadius + 1, shadowPaint);

        // Badge circle
        using var badgePaint = new SKPaint
        {
            Color = BadgeColor,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawCircle(badgeCenterX, badgeCenterY, badgeRadius, badgePaint);

        // White border
        using var borderPaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawCircle(badgeCenterX, badgeCenterY, badgeRadius, borderPaint);

        // Step number text
        using var textPaint = new SKPaint
        {
            Color = BadgeTextColor,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
            TextSize = badgeFontSize,
            FakeBoldText = true
        };

        // Center text vertically
        var textBounds = new SKRect();
        textPaint.MeasureText(stepNumber.ToString(), ref textBounds);
        var textY = badgeCenterY + textBounds.Height / 2;

        canvas.DrawText(stepNumber.ToString(), badgeCenterX, textY, textPaint);
    }

    private static void SaveImage(SKSurface surface, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var outputStream = File.Create(outputPath);
        data.SaveTo(outputStream);
    }
}
