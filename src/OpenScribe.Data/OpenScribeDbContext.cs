using Microsoft.EntityFrameworkCore;
using OpenScribe.Core.Models;

namespace OpenScribe.Data;

/// <summary>
/// Entity Framework Core database context for OpenScribe.
/// Uses SQLite for local storage of sessions and steps.
/// </summary>
public class OpenScribeDbContext : DbContext
{
    public DbSet<CaptureSession> Sessions => Set<CaptureSession>();
    public DbSet<ProcessStep> Steps => Set<ProcessStep>();

    public OpenScribeDbContext(DbContextOptions<OpenScribeDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── CaptureSession ──────────────────────────────────────
        modelBuilder.Entity<CaptureSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.ArtifactPath).HasMaxLength(1000);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.WebResearchContext).HasMaxLength(5000);

            entity.HasMany(e => e.Steps)
                  .WithOne()
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
        });

        // ── ProcessStep ─────────────────────────────────────────
        modelBuilder.Entity<ProcessStep>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WindowTitle).HasMaxLength(500);
            entity.Property(e => e.ControlName).HasMaxLength(500);
            entity.Property(e => e.ApplicationName).HasMaxLength(500);
            entity.Property(e => e.UiaControlType).HasMaxLength(200);
            entity.Property(e => e.UiaElementName).HasMaxLength(500);
            entity.Property(e => e.UiaAutomationId).HasMaxLength(500);
            entity.Property(e => e.UiaClassName).HasMaxLength(500);
            entity.Property(e => e.UiaElementBounds).HasMaxLength(200);
            entity.Property(e => e.ScreenshotPath).HasMaxLength(1000);
            entity.Property(e => e.CroppedScreenshotPath).HasMaxLength(1000);
            entity.Property(e => e.AnnotatedScreenshotPath).HasMaxLength(1000);
            entity.Property(e => e.DetailScreenshotPath).HasMaxLength(1000);
            entity.Property(e => e.TypedText).HasMaxLength(5000);
            entity.Property(e => e.OcrText).HasMaxLength(5000);
            entity.Property(e => e.VoiceTranscript).HasMaxLength(5000);
            entity.Property(e => e.AiGeneratedTitle).HasMaxLength(500);
            entity.Property(e => e.AiGeneratedInstruction).HasMaxLength(5000);
            entity.Property(e => e.AiGeneratedNotes).HasMaxLength(2000);
            entity.Property(e => e.UserEditedInstruction).HasMaxLength(5000);
            entity.Property(e => e.UserNotes).HasMaxLength(2000);
            entity.Property(e => e.AiCropRegion).HasMaxLength(200);
            entity.Property(e => e.ClickType).HasConversion<int>();

            entity.HasIndex(e => new { e.SessionId, e.SequenceNumber });

            // Ignore computed property
            entity.Ignore(e => e.EffectiveInstruction);
        });
    }
}
