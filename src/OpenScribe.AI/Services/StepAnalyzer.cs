using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.AI.Services;

/// <summary>
/// High-level service that orchestrates the AI analysis pipeline:
/// 1. Analyze each step individually
/// 2. Run an editorial pass for document consistency
/// 3. Update the session with AI-generated content
/// </summary>
public class StepAnalyzer
{
    private readonly ICopilotClient _copilotClient;
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<StepAnalyzer> _logger;

    public StepAnalyzer(
        ICopilotClient copilotClient,
        ISessionRepository sessionRepository,
        ILogger<StepAnalyzer> logger)
    {
        _copilotClient = copilotClient;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Fired when progress updates during analysis.
    /// Tuple: (currentStep, totalSteps, statusMessage)
    /// </summary>
    public event EventHandler<(int Current, int Total, string Message)>? ProgressChanged;

    /// <summary>
    /// Run the full AI analysis pipeline for a session.
    /// </summary>
    public async Task<CaptureSession> AnalyzeSessionAsync(
        CaptureSession session,
        IReadOnlyList<RawStep> rawSteps,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting AI analysis for session {Id}: {Count} steps",
            session.Id, rawSteps.Count);

        session.Status = SessionStatus.Analyzing;
        await _sessionRepository.UpdateAsync(session, ct);

        try
        {
            // Phase 0: Research process context for grounding
            ProgressChanged?.Invoke(this, (0, rawSteps.Count,
                "Researching process context..."));

            _logger.LogInformation("Phase 0: Researching process context for '{Name}'", session.Name);
            var processContext = await _copilotClient.ResearchProcessAsync(session.Name, ct);
            session.WebResearchContext = processContext;
            await _sessionRepository.UpdateAsync(session, ct);

            // Phase 1: Analyze each step individually
            var analyzedSteps = new List<AnalyzedStep>();
            AnalyzedStep? previousStep = null;

            for (var i = 0; i < rawSteps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var rawStep = rawSteps[i];
                ProgressChanged?.Invoke(this, (i + 1, rawSteps.Count,
                    $"Analyzing step {i + 1} of {rawSteps.Count}..."));

                _logger.LogDebug("Analyzing step {Seq}", rawStep.SequenceNumber);

                var analyzed = await _copilotClient.AnalyzeStepAsync(rawStep, previousStep, processContext, ct);

                // Three-tier highlight fallback:
                // 1. AI-returned highlight (if valid)
                // 2. UIA element bounds
                // 3. Click-centered box (200px)
                var highlight = ResolveHighlightRegion(analyzed.HighlightRegion, rawStep);
                analyzed.HighlightRegion = highlight;

                // Resolve crop region (separate from highlight — for detail view)
                var cropRegion = ResolveCropRegion(analyzed.CropRegion, highlight, rawStep);
                analyzed.CropRegion = cropRegion;

                analyzedSteps.Add(analyzed);
                previousStep = analyzed;

                // Update the session step with AI results
                var sessionStep = session.Steps.FirstOrDefault(s => s.SequenceNumber == rawStep.SequenceNumber);
                if (sessionStep is not null)
                {
                    sessionStep.AiGeneratedTitle = analyzed.StepTitle;
                    sessionStep.AiGeneratedInstruction = analyzed.Instruction;
                    sessionStep.AiGeneratedNotes = analyzed.Notes;

                    if (highlight is not null)
                    {
                        sessionStep.AiHighlightRegion = JsonSerializer.Serialize(highlight);
                    }

                    if (cropRegion is not null)
                    {
                        sessionStep.AiCropRegion = JsonSerializer.Serialize(cropRegion);
                    }

                    await _sessionRepository.UpdateStepAsync(sessionStep, ct);
                }
            }

            // Phase 2: Editorial pass — create a cohesive document plan
            ProgressChanged?.Invoke(this, (rawSteps.Count, rawSteps.Count,
                "Creating document plan..."));

            var plan = await _copilotClient.CreateDocumentPlanAsync(session, analyzedSteps, rawSteps, processContext, ct);

            session.GeneratedTitle = plan.Title;
            session.GeneratedIntroduction = plan.Introduction;
            session.GeneratedSummary = plan.Summary;

            // Apply revised instructions if available
            var orderedSteps = session.Steps.OrderBy(s => s.SequenceNumber).ToList();
            for (var i = 0; i < Math.Min(plan.RevisedInstructions.Count, orderedSteps.Count); i++)
            {
                var step = orderedSteps[i];
                if (!string.IsNullOrWhiteSpace(plan.RevisedInstructions[i]))
                {
                    step.AiGeneratedInstruction = plan.RevisedInstructions[i];
                }

                // Apply revised titles if available
                if (plan.RevisedTitles.Count > i && !string.IsNullOrWhiteSpace(plan.RevisedTitles[i]))
                {
                    step.AiGeneratedTitle = plan.RevisedTitles[i];
                }

                await _sessionRepository.UpdateStepAsync(step, ct);
            }

            session.Status = SessionStatus.Reviewed;
            await _sessionRepository.UpdateAsync(session, ct);

            _logger.LogInformation("AI analysis complete for session {Id}: '{Title}'",
                session.Id, plan.Title);

            return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AI analysis failed for session {Id}", session.Id);
            session.Status = SessionStatus.Error;
            session.ErrorMessage = $"AI analysis failed: {ex.Message}";
            await _sessionRepository.UpdateAsync(session, ct);
            throw;
        }
    }

    /// <summary>
    /// Resolve the best highlight region using a three-tier fallback:
    /// 1. UIA element bounds (most precise — from the accessibility tree)
    /// 2. AI-returned highlight (vision-estimated — can be inaccurate)
    /// 3. Click-centered box (200px)
    /// </summary>
    private HighlightRegion ResolveHighlightRegion(HighlightRegion? aiHighlight, RawStep step)
    {
        var imgW = step.ScreenshotWidth > 0 ? step.ScreenshotWidth : 1920;
        var imgH = step.ScreenshotHeight > 0 ? step.ScreenshotHeight : 1080;

        // Tier 1: UIA element bounds (preferred — precise coordinates from accessibility tree)
        if (!string.IsNullOrEmpty(step.Click.UiaElementBounds))
        {
            try
            {
                var bounds = JsonSerializer.Deserialize<HighlightRegion>(step.Click.UiaElementBounds);
                if (bounds is not null && bounds.Width > 0 && bounds.Height > 0)
                {
                    const int padding = 6;
                    var uiaHighlight = new HighlightRegion
                    {
                        X = Math.Max(0, bounds.X - padding),
                        Y = Math.Max(0, bounds.Y - padding),
                        Width = bounds.Width + padding * 2,
                        Height = bounds.Height + padding * 2
                    };

                    if (IsValidHighlight(uiaHighlight, step, imgW, imgH))
                    {
                        _logger.LogDebug("Step {Seq}: Using UIA element bounds highlight", step.SequenceNumber);
                        return uiaHighlight;
                    }
                }
            }
            catch
            {
                // Fall through to AI highlight
            }
        }

        // Tier 2: AI-returned highlight (vision-estimated — less reliable but covers
        // cases where UIA bounds are unavailable, e.g., web app overlays)
        if (IsValidHighlight(aiHighlight, step, imgW, imgH))
        {
            _logger.LogDebug("Step {Seq}: Using AI highlight ({X},{Y} {W}x{H})",
                step.SequenceNumber, aiHighlight!.X, aiHighlight.Y, aiHighlight.Width, aiHighlight.Height);
            return aiHighlight;
        }

        // Tier 3: Click-centered box (200px)
        _logger.LogDebug("Step {Seq}: Using click-centered fallback highlight", step.SequenceNumber);
        const int defaultBoxSize = 200;
        var halfBox = defaultBoxSize / 2;
        var x = Math.Max(0, Math.Min(step.Click.X - halfBox, imgW - defaultBoxSize));
        var y = Math.Max(0, Math.Min(step.Click.Y - halfBox, imgH - defaultBoxSize));

        return new HighlightRegion
        {
            X = x,
            Y = y,
            Width = Math.Min(defaultBoxSize, imgW - x),
            Height = Math.Min(defaultBoxSize, imgH - y)
        };
    }

    /// <summary>
    /// Validate a highlight region: must be reasonably sized, within image bounds,
    /// and near the click point.
    /// </summary>
    private static bool IsValidHighlight(HighlightRegion? highlight, RawStep step, int imgW, int imgH)
    {
        if (highlight is null)
            return false;

        // Must have minimum size
        if (highlight.Width <= 10 || highlight.Height <= 10)
            return false;

        // Must not cover more than 80% of the image in either dimension
        if (highlight.Width > imgW * 0.8 || highlight.Height > imgH * 0.8)
            return false;

        // Must be within image bounds
        if (highlight.X < 0 || highlight.Y < 0 ||
            highlight.X + highlight.Width > imgW + 10 || // small tolerance
            highlight.Y + highlight.Height > imgH + 10)
            return false;

        // Click point must be within or near (50px) the region
        const int nearDistance = 50;
        var clickInOrNearX = step.Click.X >= highlight.X - nearDistance &&
                             step.Click.X <= highlight.X + highlight.Width + nearDistance;
        var clickInOrNearY = step.Click.Y >= highlight.Y - nearDistance &&
                             step.Click.Y <= highlight.Y + highlight.Height + nearDistance;

        return clickInOrNearX && clickInOrNearY;
    }

    /// <summary>
    /// Resolve the best crop region using a two-tier fallback:
    /// 1. AI-returned crop region (validated — no click-proximity check)
    /// 2. Expand around the resolved highlight (1.5x margin)
    /// </summary>
    private HighlightRegion ResolveCropRegion(HighlightRegion? aiCropRegion, HighlightRegion? highlight, RawStep step)
    {
        var imgW = step.ScreenshotWidth > 0 ? step.ScreenshotWidth : 1920;
        var imgH = step.ScreenshotHeight > 0 ? step.ScreenshotHeight : 1080;

        // Tier 1: Use AI-returned crop region if valid
        if (IsValidCropRegion(aiCropRegion, imgW, imgH))
        {
            _logger.LogDebug("Step {Seq}: Using AI crop region ({X},{Y} {W}x{H})",
                step.SequenceNumber, aiCropRegion!.X, aiCropRegion.Y, aiCropRegion.Width, aiCropRegion.Height);
            return aiCropRegion;
        }

        // Tier 2: Expand around the resolved highlight with 1.5x margin
        if (highlight is not null)
        {
            var maxDim = Math.Max(highlight.Width, highlight.Height);
            var margin = Math.Max(150, (int)(maxDim * 1.5));

            var cropX = Math.Max(0, highlight.X - margin);
            var cropY = Math.Max(0, highlight.Y - margin);
            var cropRight = Math.Min(imgW, highlight.X + highlight.Width + margin);
            var cropBottom = Math.Min(imgH, highlight.Y + highlight.Height + margin);

            var fallback = new HighlightRegion
            {
                X = cropX,
                Y = cropY,
                Width = cropRight - cropX,
                Height = cropBottom - cropY
            };

            _logger.LogDebug("Step {Seq}: Using highlight-expanded crop region ({X},{Y} {W}x{H})",
                step.SequenceNumber, fallback.X, fallback.Y, fallback.Width, fallback.Height);
            return fallback;
        }

        // Last resort: center on click with 600x400 region
        _logger.LogDebug("Step {Seq}: Using click-centered fallback crop region", step.SequenceNumber);
        const int defaultW = 600;
        const int defaultH = 400;
        var x = Math.Max(0, Math.Min(step.Click.X - defaultW / 2, imgW - defaultW));
        var y = Math.Max(0, Math.Min(step.Click.Y - defaultH / 2, imgH - defaultH));

        return new HighlightRegion
        {
            X = x,
            Y = y,
            Width = Math.Min(defaultW, imgW - x),
            Height = Math.Min(defaultH, imgH - y)
        };
    }

    /// <summary>
    /// Validate a crop region: must be reasonably sized and within image bounds.
    /// No click-proximity check — the crop can legitimately be far from the click.
    /// </summary>
    private static bool IsValidCropRegion(HighlightRegion? region, int imgW, int imgH)
    {
        if (region is null)
            return false;

        // Must have minimum size (crop regions should be substantial)
        if (region.Width < 100 || region.Height < 100)
            return false;

        // Must not cover more than 90% of the image in either dimension
        if (region.Width > imgW * 0.9 || region.Height > imgH * 0.9)
            return false;

        // Must be within image bounds (small tolerance)
        if (region.X < 0 || region.Y < 0 ||
            region.X + region.Width > imgW + 10 ||
            region.Y + region.Height > imgH + 10)
            return false;

        return true;
    }
}
