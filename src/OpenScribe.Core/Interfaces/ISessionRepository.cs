using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Repository for persisting and retrieving capture sessions and steps.
/// </summary>
public interface ISessionRepository
{
    Task<CaptureSession> CreateAsync(CaptureSession session, CancellationToken ct = default);
    Task<CaptureSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<CaptureSession>> GetAllAsync(CancellationToken ct = default);
    Task UpdateAsync(CaptureSession session, CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);

    Task<ProcessStep> AddStepAsync(ProcessStep step, CancellationToken ct = default);
    Task UpdateStepAsync(ProcessStep step, CancellationToken ct = default);
    Task DeleteStepAsync(Guid stepId, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessStep>> GetStepsAsync(Guid sessionId, CancellationToken ct = default);
}
