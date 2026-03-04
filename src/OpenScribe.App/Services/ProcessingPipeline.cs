using Microsoft.Extensions.Logging;
using OpenScribe.AI.Services;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.App.Services;

/// <summary>
/// Orchestrates the full processing pipeline for a captured session:
/// 1. Build timeline (crop, OCR, transcribe)
/// 2. AI analysis (per-step + editorial pass)
/// 3. Generate document
/// </summary>
public class ProcessingPipeline
{
    private readonly ITimelineBuilder _timelineBuilder;
    private readonly StepAnalyzer _stepAnalyzer;
    private readonly IDocxBuilder _docxBuilder;
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<ProcessingPipeline> _logger;

    public ProcessingPipeline(
        ITimelineBuilder timelineBuilder,
        StepAnalyzer stepAnalyzer,
        IDocxBuilder docxBuilder,
        ISessionRepository sessionRepository,
        ILogger<ProcessingPipeline> logger)
    {
        _timelineBuilder = timelineBuilder;
        _stepAnalyzer = stepAnalyzer;
        _docxBuilder = docxBuilder;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    /// <summary>
    /// Fired during processing with (current, total, message).
    /// </summary>
    public event EventHandler<(int Current, int Total, string Message)>? ProgressChanged;

    /// <summary>
    /// Run the full pipeline: preprocessing → AI analysis → document generation.
    /// </summary>
    public async Task<string> ProcessSessionAsync(
        Guid sessionId,
        ExportSettings exportSettings,
        CancellationToken ct = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        _logger.LogInformation("Starting processing pipeline for session: {Name}", session.Name);

        try
        {
            // Phase 1: Preprocessing
            ProgressChanged?.Invoke(this, (0, 3, "Building timeline..."));
            session.Status = SessionStatus.Processing;
            await _sessionRepository.UpdateAsync(session, ct);

            var rawSteps = await _timelineBuilder.BuildTimelineAsync(session, ct);

            // Phase 2: AI Analysis
            ProgressChanged?.Invoke(this, (1, 3, "Running AI analysis..."));

            void OnAnalyzerProgress(object? s, (int Current, int Total, string Message) e) =>
                ProgressChanged?.Invoke(this, (1, 3, e.Message));

            _stepAnalyzer.ProgressChanged += OnAnalyzerProgress;
            try
            {
                session = await _stepAnalyzer.AnalyzeSessionAsync(session, rawSteps, ct);
            }
            finally
            {
                _stepAnalyzer.ProgressChanged -= OnAnalyzerProgress;
            }

            // Phase 3: Document Generation
            ProgressChanged?.Invoke(this, (2, 3, "Generating document..."));

            var docPath = await _docxBuilder.BuildDocumentAsync(session, exportSettings, ct);

            session.ExportedDocumentPath = docPath;
            session.Status = SessionStatus.Exported;
            await _sessionRepository.UpdateAsync(session, ct);

            ProgressChanged?.Invoke(this, (3, 3, "Complete!"));

            _logger.LogInformation("Pipeline complete. Document: {Path}", docPath);
            return docPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Status = SessionStatus.Error;
            session.ErrorMessage = ex.Message;
            // Use CancellationToken.None so error state is always persisted
            await _sessionRepository.UpdateAsync(session, CancellationToken.None);
            throw;
        }
    }
}
