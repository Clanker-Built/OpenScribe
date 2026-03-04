using OpenScribe.Core.Enums;

namespace OpenScribe.Core.Models;

/// <summary>
/// Represents a click event captured by the input hook system.
/// </summary>
public record ClickEvent
{
    /// <summary>Time relative to session start.</summary>
    public required TimeSpan Timestamp { get; init; }

    /// <summary>Screen X coordinate of the click.</summary>
    public required int X { get; init; }

    /// <summary>Screen Y coordinate of the click.</summary>
    public required int Y { get; init; }

    public required ClickType ClickType { get; init; }

    /// <summary>Window title at the time of click.</summary>
    public required string WindowTitle { get; init; }

    /// <summary>Control name/AutomationId if available.</summary>
    public string? ControlName { get; init; }

    /// <summary>Process name of the foreground application.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>The monitor/screen index where the click occurred.</summary>
    public int ScreenIndex { get; init; }

    // ── UI Automation metadata ────────────────────────────────────
    /// <summary>UIA control type (e.g., "Button", "TextBox", "MenuItem").</summary>
    public string? UiaControlType { get; init; }

    /// <summary>UIA element name (the visible label or content).</summary>
    public string? UiaElementName { get; init; }

    /// <summary>UIA automation ID (developer-assigned identifier).</summary>
    public string? UiaAutomationId { get; init; }

    /// <summary>UIA class name (e.g., "Button", "TextBlock").</summary>
    public string? UiaClassName { get; init; }

    /// <summary>UIA element bounding rectangle as JSON: {"x":0,"y":0,"width":100,"height":30}.</summary>
    public string? UiaElementBounds { get; init; }

    /// <summary>DPI scale factor for the monitor where the click occurred.</summary>
    public double DpiScale { get; init; } = 1.0;
}
