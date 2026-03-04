using Microsoft.EntityFrameworkCore;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.Data;

/// <summary>
/// SQLite-backed implementation of <see cref="ISessionRepository"/>.
/// </summary>
public class SessionRepository : ISessionRepository
{
    private readonly OpenScribeDbContext _db;

    public SessionRepository(OpenScribeDbContext db)
    {
        _db = db;
    }

    public async Task<CaptureSession> CreateAsync(CaptureSession session, CancellationToken ct = default)
    {
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    public async Task<CaptureSession?> GetByIdAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _db.Sessions
            .Include(s => s.Steps.OrderBy(st => st.SequenceNumber))
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    public async Task<IReadOnlyList<CaptureSession>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Sessions
            .Include(s => s.Steps)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(CaptureSession session, CancellationToken ct = default)
    {
        _db.Sessions.Update(session);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.Sessions.FindAsync([sessionId], ct);
        if (session is not null)
        {
            _db.Sessions.Remove(session);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ProcessStep> AddStepAsync(ProcessStep step, CancellationToken ct = default)
    {
        _db.Steps.Add(step);
        await _db.SaveChangesAsync(ct);
        return step;
    }

    public async Task UpdateStepAsync(ProcessStep step, CancellationToken ct = default)
    {
        _db.Steps.Update(step);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteStepAsync(Guid stepId, CancellationToken ct = default)
    {
        var step = await _db.Steps.FindAsync([stepId], ct);
        if (step is not null)
        {
            _db.Steps.Remove(step);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<ProcessStep>> GetStepsAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _db.Steps
            .Where(s => s.SessionId == sessionId)
            .OrderBy(s => s.SequenceNumber)
            .ToListAsync(ct);
    }
}
