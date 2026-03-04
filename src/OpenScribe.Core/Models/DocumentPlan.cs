namespace OpenScribe.Core.Models;

/// <summary>
/// AI-generated overview of the full document (intro, summary, title).
/// </summary>
public class DocumentPlan
{
    /// <summary>Suggested document title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Introduction paragraph for the document.</summary>
    public string Introduction { get; set; } = string.Empty;

    /// <summary>Summary/conclusion paragraph.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Revised step instructions after editorial consistency pass.</summary>
    public List<string> RevisedInstructions { get; set; } = [];

    /// <summary>Revised step titles for consistency (e.g., "Click the Submit button").</summary>
    public List<string> RevisedTitles { get; set; } = [];
}
