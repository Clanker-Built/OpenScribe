using OpenScribe.Core.Models;

namespace OpenScribe.Core.Interfaces;

/// <summary>
/// Monitors global mouse/keyboard events and emits click and text-input events.
/// </summary>
public interface IInputHookManager : IDisposable
{
    /// <summary>Fired when a mouse click is detected.</summary>
    event EventHandler<ClickEvent>? ClickDetected;

    /// <summary>Fired when accumulated keyboard text is flushed (idle timeout or explicit flush).</summary>
    event EventHandler<KeyboardInputEvent>? TextInputDetected;

    /// <summary>Start listening for global input events.</summary>
    void Start();

    /// <summary>Stop listening for global input events.</summary>
    void Stop();

    /// <summary>Whether the hook is currently active.</summary>
    bool IsActive { get; }

    /// <summary>Optional capture scope for click filtering.</summary>
    CaptureScope? CaptureScope { get; set; }

    /// <summary>Flush the keyboard text buffer immediately and raise TextInputDetected if non-empty.</summary>
    void FlushTextBuffer();
}
