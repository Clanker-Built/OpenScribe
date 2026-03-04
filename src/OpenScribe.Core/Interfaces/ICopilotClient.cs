using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Sends step data to Azure OpenAI / Copilot for AI analysis.
/// </summary>
public interface ICopilotClient
{
    /// <summary>
    /// Ask the AI to research and summarize the expected steps for a named process.
    /// Returns a concise summary to use as context for per-step analysis.
    /// </summary>
    Task<string?> ResearchProcessAsync(string processName, CancellationToken ct = default);

    /// <summary>
    /// Analyze a single step: send screenshot + metadata to AI Vision
    /// and return structured analysis.
    /// </summary>
    Task<AnalyzedStep> AnalyzeStepAsync(
        RawStep step,
        AnalyzedStep? previousStep,
        string? processContext = null,
        CancellationToken ct = default);

    /// <summary>
    /// Run an editorial pass over all analyzed steps to produce a cohesive document plan.
    /// Includes raw steps for voice transcript context.
    /// </summary>
    Task<DocumentPlan> CreateDocumentPlanAsync(
        CaptureSession session,
        IReadOnlyList<AnalyzedStep> steps,
        IReadOnlyList<RawStep> rawSteps,
        string? processContext = null,
        CancellationToken ct = default);

    /// <summary>
    /// Test connectivity to the Azure OpenAI endpoint.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}
