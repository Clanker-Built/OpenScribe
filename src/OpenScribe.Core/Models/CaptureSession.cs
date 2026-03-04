using OpenScribe.Core.Enums;

namespace OpenScribe.Core.Models;

/// <summary>
/// Represents a complete capture session — one recorded process from start to finish.
/// </summary>
public class CaptureSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-provided name for this session (e.g., "How to submit a PO").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the process being documented.</summary>
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Created;

    /// <summary>Error message if Status == Error.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Root folder where all session artifacts (PNGs, WAV, JSON) are stored.</summary>
    public string ArtifactPath { get; set; } = string.Empty;

    /// <summary>Path to the screen recording MP4, if captured.</summary>
    public string? VideoPath { get; set; }

    /// <summary>Path to the raw audio recording WAV.</summary>
    public string? AudioPath { get; set; }

    /// <summary>Ordered list of process steps captured in this session.</summary>
    public List<ProcessStep> Steps { get; set; } = [];

    /// <summary>AI-generated document title.</summary>
    public string? GeneratedTitle { get; set; }

    /// <summary>AI-generated introduction paragraph.</summary>
    public string? GeneratedIntroduction { get; set; }

    /// <summary>AI-generated summary paragraph.</summary>
    public string? GeneratedSummary { get; set; }

    /// <summary>Path to the exported .docx file.</summary>
    public string? ExportedDocumentPath { get; set; }

    /// <summary>AI-generated process research context used during analysis.</summary>
    public string? WebResearchContext { get; set; }

    // ── Display helpers (for XAML x:Bind that requires string types) ──
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("g");
    public string StatusDisplay => Status.ToString();
    public int StepCount => Steps.Count;
}
