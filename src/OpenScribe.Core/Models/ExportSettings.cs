using OpenScribe.Core.Enums;

namespace OpenScribe.Core.Models;

/// <summary>
/// Settings for exporting a document.
/// </summary>
public class ExportSettings
{
    public ExportFormat Format { get; set; } = ExportFormat.Docx;

    /// <summary>Output file path for the generated document.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Path to a template .docx for corporate branding.</summary>
    public string? TemplatePath { get; set; }

    /// <summary>Whether to include screenshot images in the document.</summary>
    public bool IncludeScreenshots { get; set; } = true;

    /// <summary>Whether to include annotated (highlighted) versions of screenshots.</summary>
    public bool UseAnnotatedScreenshots { get; set; } = true;

    /// <summary>Whether to include a cropped detail view alongside the full overview screenshot.</summary>
    public bool UseDetailCrop { get; set; } = true;

    /// <summary>Whether to include the AI-generated notes/tips in the document.</summary>
    public bool IncludeNotes { get; set; } = true;

    /// <summary>Whether to generate a table of contents.</summary>
    public bool IncludeTableOfContents { get; set; } = true;

    /// <summary>Company/team name for the document header.</summary>
    public string? OrganizationName { get; set; }

    /// <summary>Author name for the document metadata.</summary>
    public string? AuthorName { get; set; }
}
