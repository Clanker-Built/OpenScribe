namespace OpenScribe.Core.Models;

/// <summary>
/// The AI analysis result for a single step.
/// </summary>
public class AnalyzedStep
{
    /// <summary>Short, action-oriented title (e.g., "Click the Submit button").</summary>
    public string StepTitle { get; set; } = string.Empty;

    /// <summary>Full instruction paragraph for the end user.</summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>Region to highlight in the screenshot [x, y, width, height].</summary>
    public HighlightRegion? HighlightRegion { get; set; }

    /// <summary>
    /// Crop region for the detail view — the area a reader needs to see to understand this step.
    /// Larger than the highlight; includes context like menus, dialogs, form sections.
    /// </summary>
    public HighlightRegion? CropRegion { get; set; }

    /// <summary>Optional tips or warnings for the end user.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// A rectangular region in a screenshot to highlight.
/// </summary>
public class HighlightRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
