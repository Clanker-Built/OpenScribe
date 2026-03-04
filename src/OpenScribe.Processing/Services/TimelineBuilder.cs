using System.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.Processing.Services;

/// <summary>
/// Builds a timeline of <see cref="RawStep"/> objects by merging click events,
/// screenshots, OCR results, and transcription segments.
/// </summary>
public class TimelineBuilder : ITimelineBuilder
{
    private readonly IOcrProcessor _ocrProcessor;
    private readonly ITranscriptionService _transcriptionService;
    private readonly RegionCropper _regionCropper;
    private readonly ILogger<TimelineBuilder> _logger;
    private readonly OpenScribeSettings _settings;

    public TimelineBuilder(
        IOcrProcessor ocrProcessor,
        ITranscriptionService transcriptionService,
        RegionCropper regionCropper,
        ILogger<TimelineBuilder> logger,
        IOptions<OpenScribeSettings> settings)
    {
        _ocrProcessor = ocrProcessor;
        _transcriptionService = transcriptionService;
        _regionCropper = regionCropper;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<RawStep>> BuildTimelineAsync(
        CaptureSession session,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Building timeline for session {Id} with {Count} steps",
            session.Id, session.Steps.Count);

        // Transcribe audio if available
        IReadOnlyList<TranscriptSegment> transcriptSegments = [];
        if (!string.IsNullOrEmpty(session.AudioPath) && File.Exists(session.AudioPath))
        {
            _logger.LogInformation("Transcribing audio: {Path}", session.AudioPath);
            transcriptSegments = await _transcriptionService.TranscribeAsync(session.AudioPath, ct);
            _logger.LogInformation("Got {Count} transcript segments", transcriptSegments.Count);
        }

        var rawSteps = new List<RawStep>();
        var sortedSteps = session.Steps.OrderBy(s => s.SequenceNumber).ToList();

        for (var i = 0; i < sortedSteps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var step = sortedSteps[i];
            _logger.LogDebug("Processing step {Seq}/{Total}", step.SequenceNumber, sortedSteps.Count);

            // Create the click event from the step data
            var click = new ClickEvent
            {
                Timestamp = step.Timestamp,
                X = step.ClickX,
                Y = step.ClickY,
                ClickType = step.ClickType,
                WindowTitle = step.WindowTitle,
                ControlName = step.ControlName,
                ApplicationName = step.ApplicationName,
                UiaControlType = step.UiaControlType,
                UiaElementName = step.UiaElementName,
                UiaAutomationId = step.UiaAutomationId,
                UiaClassName = step.UiaClassName,
                UiaElementBounds = step.UiaElementBounds,
                DpiScale = step.DpiScale
            };

            var rawStep = new RawStep
            {
                SequenceNumber = step.SequenceNumber,
                Timestamp = step.Timestamp,
                Click = click,
                ScreenshotPath = step.ScreenshotPath,
                TypedText = step.TypedText
            };

            // Read full screenshot dimensions
            if (File.Exists(step.ScreenshotPath))
            {
                try
                {
                    using var bmp = new Bitmap(step.ScreenshotPath);
                    rawStep.ScreenshotWidth = bmp.Width;
                    rawStep.ScreenshotHeight = bmp.Height;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read image dimensions for step {Seq}", step.SequenceNumber);
                }
            }

            // Crop region around click
            if (File.Exists(step.ScreenshotPath) && step.ClickX > 0 && step.ClickY > 0)
            {
                var croppedPath = Path.Combine(
                    Path.GetDirectoryName(step.ScreenshotPath)!,
                    "cropped",
                    $"crop_{step.SequenceNumber:D4}.png");

                try
                {
                    _regionCropper.CropAroundClick(step.ScreenshotPath, step.ClickX, step.ClickY, croppedPath);
                    rawStep.CroppedScreenshotPath = croppedPath;
                    step.CroppedScreenshotPath = croppedPath;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to crop region for step {Seq}", step.SequenceNumber);
                }
            }

            // Run OCR on the cropped region (or full screenshot)
            var ocrImagePath = rawStep.CroppedScreenshotPath ?? step.ScreenshotPath;
            if (File.Exists(ocrImagePath))
            {
                try
                {
                    rawStep.OcrText = await _ocrProcessor.ExtractTextAsync(ocrImagePath, ct: ct);
                    step.OcrText = rawStep.OcrText;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OCR failed for step {Seq}", step.SequenceNumber);
                }
            }

            // Find matching transcript for this step's time window
            if (transcriptSegments.Count > 0)
            {
                var stepStart = step.Timestamp;
                var stepEnd = i + 1 < sortedSteps.Count
                    ? sortedSteps[i + 1].Timestamp
                    : step.Timestamp + TimeSpan.FromSeconds(10);

                var matchingTranscript = transcriptSegments
                    .Where(ts => ts.Start >= stepStart && ts.Start < stepEnd)
                    .OrderBy(ts => ts.Start)
                    .Select(ts => ts.Text);

                var transcript = string.Join(" ", matchingTranscript).Trim();
                if (!string.IsNullOrEmpty(transcript))
                {
                    rawStep.VoiceTranscript = transcript;
                    step.VoiceTranscript = transcript;
                }
            }

            rawSteps.Add(rawStep);
        }

        _logger.LogInformation("Timeline built: {Count} raw steps", rawSteps.Count);
        return rawSteps;
    }
}
