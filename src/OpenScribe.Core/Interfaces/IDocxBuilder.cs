using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Generates Word (.docx) documents from analyzed capture sessions.
/// </summary>
public interface IDocxBuilder
{
    /// <summary>
    /// Build a complete .docx document from a capture session.
    /// </summary>
    /// <param name="session">The capture session with analyzed steps.</param>
    /// <param name="settings">Export settings (template, format, etc.).</param>
    /// <returns>Path to the generated .docx file.</returns>
    Task<string> BuildDocumentAsync(
        CaptureSession session,
        ExportSettings settings,
        CancellationToken ct = default);
}
