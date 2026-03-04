using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace OpenScribe.DocGen.Services;

/// <summary>
/// Builds a professional Word (.docx) document from an analyzed capture session
/// using the Open XML SDK. Includes a title page, table of contents placeholder,
/// numbered steps with annotated screenshots, and a summary.
/// </summary>
public class DocxBuilder : IDocxBuilder
{
    private readonly ILogger<DocxBuilder> _logger;
    private readonly IScreenshotAnnotator _annotator;

    private const long EmuPerInch = 914400L;
    private const long MaxImageWidthEmu = (long)(6.0 * EmuPerInch);          // 6 inches max width
    private const long MaxImageHeightEmu = (long)(4.0 * EmuPerInch);         // 4 inches max height (single-image fallback)
    private const long OverviewMaxHeightEmu = (long)(2.5 * EmuPerInch);      // 2.5 inches for overview
    private const long DetailMaxHeightEmu = (long)(3.5 * EmuPerInch);        // 3.5 inches for detail
    private uint _nextImageId;

    public DocxBuilder(ILogger<DocxBuilder> logger, IScreenshotAnnotator annotator)
    {
        _logger = logger;
        _annotator = annotator;
    }

    public async Task<string> BuildDocumentAsync(
        CaptureSession session,
        ExportSettings settings,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Building .docx for session {Id}: {Name}", session.Id, session.Name);
        _nextImageId = 0;

        var outputPath = settings.OutputPath;
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Build in memory first, then write bytes to disk.
        // This avoids Windows Defender blocking direct .docx file creation
        // from untrusted/unsigned processes (anti-ransomware heuristic).
        using var memoryStream = new MemoryStream();
        using var doc = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document);

        // Add main document part
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        // Add styles
        AddStyles(mainPart);

        // Title
        AddTitle(body, session.GeneratedTitle ?? session.Name);

        // Metadata line
        if (!string.IsNullOrEmpty(settings.OrganizationName) || !string.IsNullOrEmpty(settings.AuthorName))
        {
            var meta = string.Join(" | ",
                new[] { settings.OrganizationName, settings.AuthorName, DateTime.Now.ToString("MMMM d, yyyy") }
                    .Where(s => !string.IsNullOrEmpty(s)));
            AddParagraph(body, meta, "Subtitle");
        }

        // Horizontal line
        AddHorizontalRule(body);

        // Introduction
        if (!string.IsNullOrEmpty(session.GeneratedIntroduction))
        {
            AddHeading(body, "Overview", 1);
            AddParagraph(body, session.GeneratedIntroduction);
        }

        // Steps
        AddHeading(body, "Procedure", 1);

        var orderedSteps = session.Steps
            .Where(s => !s.IsExcluded)
            .OrderBy(s => s.SequenceNumber)
            .ToList();

        for (var i = 0; i < orderedSteps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var step = orderedSteps[i];
            var displayNumber = i + 1;

            // Step heading
            var title = step.AiGeneratedTitle ?? $"Step {displayNumber}";
            AddHeading(body, $"Step {displayNumber}: {title}", 2);

            // Instruction text
            var instruction = step.EffectiveInstruction;
            if (!string.IsNullOrWhiteSpace(instruction))
            {
                AddParagraph(body, instruction);
            }

            // Screenshot
            if (settings.IncludeScreenshots && !string.IsNullOrEmpty(step.ScreenshotPath))
            {
                await AddStepScreenshots(mainPart, body, step, displayNumber, settings, ct);
            }

            // Notes
            if (settings.IncludeNotes && !string.IsNullOrWhiteSpace(step.AiGeneratedNotes))
            {
                AddNote(body, step.AiGeneratedNotes);
            }

            // User notes
            if (!string.IsNullOrWhiteSpace(step.UserNotes))
            {
                AddNote(body, step.UserNotes);
            }

            // Spacing between steps
            AddSpacing(body);
        }

        // Summary
        if (!string.IsNullOrEmpty(session.GeneratedSummary))
        {
            AddHorizontalRule(body);
            AddHeading(body, "Summary", 1);
            AddParagraph(body, session.GeneratedSummary);
        }

        mainPart.Document.Append(body);
        mainPart.Document.Save();
        doc.Dispose();

        // Write from memory to disk as raw bytes.
        // Write directly to the final path — File.Move (rename to .docx) gets blocked
        // by Defender's anti-ransomware heuristic, but raw byte writes are allowed.
        await File.WriteAllBytesAsync(outputPath, memoryStream.ToArray(), ct);

        _logger.LogInformation("Document saved: {Path}", outputPath);
        return outputPath;
    }

    // ── Screenshot Handling ─────────────────────────────────────────

    /// <summary>
    /// Add screenshot(s) for a step. When UseDetailCrop is enabled and highlight data exists,
    /// produces a two-image layout: small overview + large detail crop. Otherwise falls back
    /// to a single annotated image.
    /// </summary>
    private async Task AddStepScreenshots(
        MainDocumentPart mainPart,
        Body body,
        ProcessStep step,
        int displayNumber,
        ExportSettings settings,
        CancellationToken ct)
    {
        var hasHighlight = settings.UseAnnotatedScreenshots &&
                           !string.IsNullOrEmpty(step.AiHighlightRegion);

        HighlightRegion? highlight = null;
        if (hasHighlight)
        {
            try
            {
                highlight = System.Text.Json.JsonSerializer.Deserialize<HighlightRegion>(step.AiHighlightRegion!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse highlight region for step {Seq}", step.SequenceNumber);
            }
        }

        // Parse separate crop region (used for detail view cropping)
        HighlightRegion? cropRegion = null;
        if (!string.IsNullOrEmpty(step.AiCropRegion))
        {
            try
            {
                cropRegion = System.Text.Json.JsonSerializer.Deserialize<HighlightRegion>(step.AiCropRegion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse crop region for step {Seq}", step.SequenceNumber);
            }
        }

        var useTwoImageLayout = settings.UseDetailCrop && highlight is not null;

        if (useTwoImageLayout && highlight is not null)
        {
            await AddTwoImageLayout(mainPart, body, step, displayNumber, highlight, cropRegion, ct);
        }
        else
        {
            await AddSingleImageLayout(mainPart, body, step, displayNumber, highlight, ct);
        }
    }

    /// <summary>
    /// Two-image layout: small overview screenshot + large cropped detail screenshot.
    /// Uses cropRegion (if available) for cropping, and highlight for the red-box annotation.
    /// </summary>
    private async Task AddTwoImageLayout(
        MainDocumentPart mainPart,
        Body body,
        ProcessStep step,
        int displayNumber,
        HighlightRegion highlight,
        HighlightRegion? cropRegion,
        CancellationToken ct)
    {
        var baseDir = Path.GetDirectoryName(step.ScreenshotPath)!;
        var annotatedDir = Path.Combine(baseDir, "annotated");
        var detailDir = Path.Combine(baseDir, "detail");

        // 1. Annotate full screenshot → overview image (always uses highlight for red box)
        var overviewPath = Path.Combine(annotatedDir, $"annotated_{step.SequenceNumber:D4}.png");
        try
        {
            await _annotator.AnnotateAsync(
                step.ScreenshotPath, overviewPath, displayNumber,
                highlight.X, highlight.Y, highlight.Width, highlight.Height, ct);
            step.AnnotatedScreenshotPath = overviewPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to annotate overview for step {Seq}", step.SequenceNumber);
            overviewPath = step.ScreenshotPath;
        }

        // 2. Smart-crop: use AI crop region if available, otherwise fall back to highlight
        var cropTarget = cropRegion ?? highlight;
        var croppedPath = Path.Combine(detailDir, $"cropped_{step.SequenceNumber:D4}.png");
        CropResult cropResult;
        try
        {
            cropResult = await _annotator.CropToDetailAsync(
                step.ScreenshotPath, croppedPath,
                cropTarget.X, cropTarget.Y, cropTarget.Width, cropTarget.Height, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to crop detail for step {Seq}", step.SequenceNumber);
            // Fall back to single-image layout
            if (File.Exists(overviewPath))
            {
                AddImage(mainPart, body, overviewPath, $"Step {displayNumber} screenshot",
                    MaxImageWidthEmu, MaxImageHeightEmu);
            }
            return;
        }

        // If crop returned the original (region too large), fall back to single overview
        if (cropResult.OutputPath == step.ScreenshotPath)
        {
            if (File.Exists(overviewPath))
            {
                AddImage(mainPart, body, overviewPath, $"Step {displayNumber} screenshot",
                    MaxImageWidthEmu, MaxImageHeightEmu);
            }
            return;
        }

        // 3. Annotate cropped image → detail image
        var detailAnnotatedPath = Path.Combine(detailDir, $"detail_{step.SequenceNumber:D4}.png");
        try
        {
            await _annotator.AnnotateDetailAsync(
                cropResult.OutputPath, detailAnnotatedPath, displayNumber,
                highlight.X, highlight.Y, highlight.Width, highlight.Height,
                cropResult.OffsetX, cropResult.OffsetY, ct);
            step.DetailScreenshotPath = detailAnnotatedPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to annotate detail for step {Seq}", step.SequenceNumber);
            detailAnnotatedPath = cropResult.OutputPath;
            step.DetailScreenshotPath = detailAnnotatedPath;
        }

        // 4. Embed both images: overview (small) then detail (large)
        if (File.Exists(overviewPath))
        {
            AddImage(mainPart, body, overviewPath, $"Step {displayNumber} overview",
                MaxImageWidthEmu, OverviewMaxHeightEmu);
            AddImageCaption(body, $"Figure {displayNumber}a: Full screen overview");
        }

        if (File.Exists(detailAnnotatedPath))
        {
            AddImage(mainPart, body, detailAnnotatedPath, $"Step {displayNumber} detail",
                MaxImageWidthEmu, DetailMaxHeightEmu);
            AddImageCaption(body, $"Figure {displayNumber}b: Detail view");
        }
    }

    /// <summary>
    /// Single-image layout: one annotated screenshot at full size.
    /// </summary>
    private async Task AddSingleImageLayout(
        MainDocumentPart mainPart,
        Body body,
        ProcessStep step,
        int displayNumber,
        HighlightRegion? highlight,
        CancellationToken ct)
    {
        var imagePath = step.ScreenshotPath;

        if (highlight is not null)
        {
            try
            {
                var annotatedDir = Path.Combine(
                    Path.GetDirectoryName(step.ScreenshotPath)!,
                    "annotated");
                var annotatedPath = Path.Combine(annotatedDir,
                    $"annotated_{step.SequenceNumber:D4}.png");

                await _annotator.AnnotateAsync(
                    step.ScreenshotPath, annotatedPath, displayNumber,
                    highlight.X, highlight.Y, highlight.Width, highlight.Height, ct);

                imagePath = annotatedPath;
                step.AnnotatedScreenshotPath = annotatedPath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to annotate screenshot for step {Seq}", step.SequenceNumber);
            }
        }

        if (File.Exists(imagePath))
        {
            AddImage(mainPart, body, imagePath, $"Step {displayNumber} screenshot",
                MaxImageWidthEmu, MaxImageHeightEmu);
        }
    }

    // ── Helper Methods ──────────────────────────────────────────────

    private static void AddStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        // Heading 1 style
        styles.Append(CreateHeadingStyle("Heading1", "Heading 1", "28", true));
        // Heading 2 style
        styles.Append(CreateHeadingStyle("Heading2", "Heading 2", "24", true));
        // Subtitle style
        styles.Append(CreateStyle("Subtitle", "Subtitle", "20", false, "666666"));

        stylesPart.Styles = styles;
    }

    private static Style CreateHeadingStyle(string styleId, string styleName, string fontSize, bool bold)
    {
        return CreateStyle(styleId, styleName, fontSize, bold, "1F3864");
    }

    private static Style CreateStyle(string styleId, string styleName, string fontSize, bool bold, string color)
    {
        var style = new Style
        {
            Type = StyleValues.Paragraph,
            StyleId = styleId,
            CustomStyle = true
        };
        style.Append(new StyleName { Val = styleName });

        var rPr = new StyleRunProperties();
        rPr.Append(new FontSize { Val = fontSize });
        rPr.Append(new Color { Val = color });
        if (bold) rPr.Append(new Bold());

        style.Append(rPr);
        return style;
    }

    private static void AddTitle(Body body, string text)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties(
            new ParagraphStyleId { Val = "Heading1" },
            new Justification { Val = JustificationValues.Center });
        para.Append(pPr);

        var run = new Run(new Text(text));
        var rPr = new RunProperties();
        rPr.Append(new FontSize { Val = "36" });
        rPr.Append(new Bold());
        run.PrependChild(rPr);

        para.Append(run);
        body.Append(para);
    }

    private static void AddHeading(Body body, string text, int level)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties(
            new ParagraphStyleId { Val = $"Heading{level}" });
        para.Append(pPr);
        para.Append(new Run(new Text(text)));
        body.Append(para);
    }

    private static void AddParagraph(Body body, string text, string? styleId = null)
    {
        var para = new Paragraph();
        if (styleId is not null)
        {
            para.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }
        para.Append(new Run(new Text(text)));
        body.Append(para);
    }

    private static void AddNote(Body body, string text)
    {
        var para = new Paragraph();

        // Indent and style as a note
        var pPr = new ParagraphProperties();
        pPr.Append(new Indentation { Left = "720" }); // 0.5 inch indent
        pPr.Append(new ParagraphBorders(
            new LeftBorder
            {
                Val = BorderValues.Single,
                Size = 6,
                Color = "4472C4",
                Space = 4
            }));
        para.Append(pPr);

        // "Note:" prefix in bold
        var noteRun = new Run();
        noteRun.Append(new RunProperties(new Bold(), new Color { Val = "4472C4" }));
        noteRun.Append(new Text("Note: ") { Space = SpaceProcessingModeValues.Preserve });
        para.Append(noteRun);

        // Note text
        var textRun = new Run();
        textRun.Append(new RunProperties(new Italic(), new Color { Val = "404040" }));
        textRun.Append(new Text(text));
        para.Append(textRun);

        body.Append(para);
    }

    private static void AddHorizontalRule(Body body)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphBorders(
            new BottomBorder
            {
                Val = BorderValues.Single,
                Size = 6,
                Color = "CCCCCC",
                Space = 1
            }));
        para.Append(pPr);
        body.Append(para);
    }

    private static void AddSpacing(Body body)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new SpacingBetweenLines { After = "200" });
        para.Append(pPr);
        body.Append(para);
    }

    /// <summary>
    /// Add a centered, italic, gray caption below an image.
    /// </summary>
    private static void AddImageCaption(Body body, string text)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new SpacingBetweenLines { Before = "40", After = "120" });
        para.Append(pPr);

        var run = new Run();
        var rPr = new RunProperties();
        rPr.Append(new Italic());
        rPr.Append(new FontSize { Val = "18" }); // 9pt
        rPr.Append(new Color { Val = "808080" });
        run.Append(rPr);
        run.Append(new Text(text));
        para.Append(run);

        body.Append(para);
    }

    private void AddImage(MainDocumentPart mainPart, Body body, string imagePath, string altText,
        long maxWidthEmu = MaxImageWidthEmu, long maxHeightEmu = MaxImageHeightEmu)
    {
        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var stream = File.OpenRead(imagePath))
        {
            imagePart.FeedData(stream);
        }

        var relationshipId = mainPart.GetIdOfPart(imagePart);

        // Get image dimensions and calculate scaled EMUs
        var (widthEmu, heightEmu) = GetScaledImageDimensions(imagePath, maxWidthEmu, maxHeightEmu);

        var element = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)(++_nextImageId), Name = altText, Description = altText },
                new DW.NonVisualGraphicFrameDrawingProperties(
                    new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = Path.GetFileName(imagePath) },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            )
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        var para = new Paragraph();
        para.Append(new ParagraphProperties(new Justification { Val = JustificationValues.Center }));
        para.Append(new Run(element));
        body.Append(para);
    }

    private static (long WidthEmu, long HeightEmu) GetScaledImageDimensions(
        string imagePath, long maxWidthEmu = MaxImageWidthEmu, long maxHeightEmu = MaxImageHeightEmu)
    {
        try
        {
            using var stream = File.OpenRead(imagePath);
            using var bitmap = SkiaSharp.SKBitmap.Decode(stream);

            if (bitmap is null)
                return (maxWidthEmu, maxHeightEmu / 2);

            // Screenshots are captured at native screen resolution. Windows reports
            // coordinates in 96-DPI logical units, so we use 96 as the baseline for EMU
            // conversion. On HiDPI displays the pixel dimensions are larger, which is
            // handled by the max-width/max-height scaling below.
            const double baseDpi = 96.0;
            var widthEmu = (long)(bitmap.Width * EmuPerInch / baseDpi);
            var heightEmu = (long)(bitmap.Height * EmuPerInch / baseDpi);

            // Scale down if too large, maintaining aspect ratio
            if (widthEmu > maxWidthEmu)
            {
                var scale = (double)maxWidthEmu / widthEmu;
                widthEmu = maxWidthEmu;
                heightEmu = (long)(heightEmu * scale);
            }

            if (heightEmu > maxHeightEmu)
            {
                var scale = (double)maxHeightEmu / heightEmu;
                heightEmu = maxHeightEmu;
                widthEmu = (long)(widthEmu * scale);
            }

            return (widthEmu, heightEmu);
        }
        catch
        {
            // Default size if we can't read the image
            return (maxWidthEmu, maxHeightEmu / 2);
        }
    }
}
