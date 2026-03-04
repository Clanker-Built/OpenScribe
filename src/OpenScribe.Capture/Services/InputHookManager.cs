using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenScribe.Capture.Interop;
using OpenScribe.Core.Configuration;
using OpenScribe.Core.Enums;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.Capture.Services;

/// <summary>
/// Installs low-level Windows mouse and keyboard hooks to detect global click events
/// and accumulate typed text. Text is flushed on mouse click or idle timeout.
/// </summary>
public class InputHookManager : IInputHookManager
{
    private readonly ILogger<InputHookManager> _logger;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;
    private NativeMethods.LowLevelKeyboardProc? _keyboardHookProc;
    private DateTime _sessionStart;
    private DateTime _lastClickTime = DateTime.MinValue;
    private readonly int _debounceMs;
    private readonly int _keyboardIdleTimeoutMs;
    private bool _disposed;

    // ── Keyboard text accumulation ──────────────────────────────
    private readonly StringBuilder _textBuffer = new();
    private int _keystrokeCount;
    private readonly object _textBufferLock = new();
    private System.Threading.Timer? _idleTimer;
    private volatile bool _stopping; // guard against timer race during Stop()
    private readonly byte[] _keyboardStateBuffer = new byte[256]; // reuse to avoid GC in hook
    private readonly char[] _charBuffer = new char[4];

    public event EventHandler<ClickEvent>? ClickDetected;
    public event EventHandler<KeyboardInputEvent>? TextInputDetected;
    public bool IsActive => _mouseHookId != IntPtr.Zero;
    public CaptureScope? CaptureScope { get; set; }

    public InputHookManager(ILogger<InputHookManager> logger, IOptions<OpenScribeSettings> settings)
    {
        _logger = logger;
        _debounceMs = settings.Value.ClickDebounceMs;
        _keyboardIdleTimeoutMs = settings.Value.KeyboardIdleTimeoutMs;
    }

    public void Start()
    {
        if (IsActive)
            return;

        _sessionStart = DateTime.UtcNow;
        _stopping = false;
        _mouseHookProc = MouseHookCallback;
        _keyboardHookProc = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = NativeMethods.GetModuleHandleW(curModule.ModuleName);

        // Install mouse hook
        _mouseHookId = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_MOUSE_LL,
            _mouseHookProc,
            moduleHandle,
            0);

        if (_mouseHookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to install mouse hook. Win32 error: {Error}", error);
            throw new InvalidOperationException($"Failed to install mouse hook. Error code: {error}");
        }

        // Install keyboard hook
        _keyboardHookId = NativeMethods.SetWindowsHookExW(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardHookProc,
            moduleHandle,
            0);

        if (_keyboardHookId == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogWarning("Failed to install keyboard hook. Win32 error: {Error}. Continuing without keyboard capture.", error);
        }
        else
        {
            _logger.LogInformation("Global keyboard hook installed successfully");
        }

        // Initialize idle timer (does not start until first keystroke)
        _idleTimer = new System.Threading.Timer(OnIdleTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation("Global mouse hook installed successfully");
    }

    public void Stop()
    {
        if (!IsActive)
            return;

        // Signal the hook callback to stop touching the timer before we dispose it
        _stopping = true;

        // Unhook keyboard FIRST so no more callbacks can fire
        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        _keyboardHookProc = null;

        // Now safe to dispose the timer — no more callbacks will call Change()
        _idleTimer?.Dispose();
        _idleTimer = null;

        // Flush any remaining text after hooks are removed
        FlushTextBuffer();

        NativeMethods.UnhookWindowsHookEx(_mouseHookId);
        _mouseHookId = IntPtr.Zero;
        _mouseHookProc = null;

        _logger.LogInformation("Global mouse and keyboard hooks removed");
    }

    public void FlushTextBuffer()
    {
        string text;
        int count;
        lock (_textBufferLock)
        {
            if (_textBuffer.Length == 0)
                return;

            text = _textBuffer.ToString();
            count = _keystrokeCount;
            _textBuffer.Clear();
            _keystrokeCount = 0;
        }

        // Stop the idle timer
        _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        var fgHwnd = NativeMethods.GetForegroundWindow();
        var windowTitle = GetWindowTitle(fgHwnd);
        var appName = GetApplicationName(fgHwnd);

        var evt = new KeyboardInputEvent(
            Timestamp: DateTime.UtcNow - _sessionStart,
            AccumulatedText: text,
            WindowTitle: windowTitle,
            ApplicationName: appName,
            KeystrokeCount: count
        );

        TextInputDetected?.Invoke(this, evt);
    }

    private void OnIdleTimerElapsed(object? state)
    {
        try
        {
            FlushTextBuffer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing text buffer on idle timeout");
        }
    }

    // ── Keyboard hook callback ────────────────────────────────────
    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && !_stopping)
            {
                var msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    var vk = (int)hookStruct.vkCode;

                    // Skip modifier-only keys
                    if (!IsModifierKey(vk))
                    {
                        ProcessKeystroke(hookStruct, vk);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Never let exceptions escape the hook callback — Windows will kill the process
            _logger.LogError(ex, "Error in keyboard hook callback");
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private void ProcessKeystroke(NativeMethods.KBDLLHOOKSTRUCT hookStruct, int vk)
    {
        // Skip if our own process is focused
        var fgHwnd = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(fgHwnd, out var fgPid);
        if (fgPid == (uint)Environment.ProcessId)
            return;

        // Scope filtering for keyboard
        var scope = CaptureScope;
        if (scope is not null && scope.ScopeType == CaptureScopeType.SingleWindow)
        {
            if (fgPid != (uint)scope.ProcessId)
                return;
        }

        // Skip Ctrl+ and Alt+ combos (shortcuts, not text input)
        // Allow Shift (for uppercase/symbols)
        NativeMethods.GetKeyboardState(_keyboardStateBuffer);

        var ctrlHeld = (NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
        var altHeld = (NativeMethods.GetKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;

        if (ctrlHeld || altHeld)
            return;

        // Handle action keys with readable tokens
        lock (_textBufferLock)
        {
            switch (vk)
            {
                case NativeMethods.VK_RETURN:
                    _textBuffer.Append("[Enter]");
                    break;
                case NativeMethods.VK_TAB:
                    _textBuffer.Append("[Tab]");
                    break;
                case NativeMethods.VK_ESCAPE:
                    _textBuffer.Append("[Escape]");
                    break;
                case NativeMethods.VK_BACK:
                    _textBuffer.Append("[Backspace]");
                    break;
                default:
                    // Translate to actual character using ToUnicode
                    var result = NativeMethods.ToUnicode(
                        hookStruct.vkCode,
                        hookStruct.scanCode,
                        _keyboardStateBuffer,
                        _charBuffer,
                        _charBuffer.Length,
                        0);

                    if (result > 0)
                    {
                        _textBuffer.Append(_charBuffer, 0, result);
                    }
                    break;
            }

            _keystrokeCount++;
        }

        // Reset idle timer on each keystroke (guard against disposed timer)
        if (!_stopping)
        {
            try { _idleTimer?.Change(_keyboardIdleTimeoutMs, Timeout.Infinite); }
            catch (ObjectDisposedException) { /* timer disposed during shutdown — safe to ignore */ }
        }
    }

    private static bool IsModifierKey(int vk) => vk is
        NativeMethods.VK_SHIFT or NativeMethods.VK_CONTROL or NativeMethods.VK_MENU or
        NativeMethods.VK_LWIN or NativeMethods.VK_RWIN or
        NativeMethods.VK_LSHIFT or NativeMethods.VK_RSHIFT or
        NativeMethods.VK_LCONTROL or NativeMethods.VK_RCONTROL or
        NativeMethods.VK_LMENU or NativeMethods.VK_RMENU;

    // ── Mouse hook callback ───────────────────────────────────────
    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var clickType = GetClickType(wParam);
            if (clickType.HasValue)
            {
                // Skip clicks on our own process (OpenScribe UI, overlay, etc.)
                // Check both the foreground window AND the window under the cursor.
                // Always-on-top windows (like our overlay) receive clicks without
                // being the foreground window, so GetForegroundWindow alone misses them.
                var hookStruct0 = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                var fgHwnd = NativeMethods.GetForegroundWindow();
                var clickedHwnd = NativeMethods.WindowFromPoint(hookStruct0.pt);

                NativeMethods.GetWindowThreadProcessId(fgHwnd, out var fgPid);
                NativeMethods.GetWindowThreadProcessId(clickedHwnd, out var clickedPid);

                var ourPid = (uint)Environment.ProcessId;
                if (fgPid == ourPid || clickedPid == ourPid)
                {
                    return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }

                // Scope-based click filtering
                var scope = CaptureScope;
                if (scope is not null)
                {
                    switch (scope.ScopeType)
                    {
                        case CaptureScopeType.SingleMonitor:
                            if (hookStruct0.pt.x < scope.MonitorLeft ||
                                hookStruct0.pt.x >= scope.MonitorLeft + scope.MonitorWidth ||
                                hookStruct0.pt.y < scope.MonitorTop ||
                                hookStruct0.pt.y >= scope.MonitorTop + scope.MonitorHeight)
                            {
                                return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                            }
                            break;
                        case CaptureScopeType.SingleWindow:
                            if (fgPid != (uint)scope.ProcessId)
                            {
                                return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                            }
                            break;
                    }
                }

                // Debounce rapid clicks
                var now = DateTime.UtcNow;
                if ((now - _lastClickTime).TotalMilliseconds >= _debounceMs)
                {
                    _lastClickTime = now;

                    // BuildClickEvent (which includes slow UIA queries) runs on a background
                    // thread to avoid blocking the hook and triggering Windows' hook timeout.
                    // hookStruct0 and fgHwnd were already captured above.
                    Task.Run(() =>
                    {
                        try
                        {
                            var clickEvent = BuildClickEvent(hookStruct0, clickType.Value, fgHwnd);
                            ClickDetected?.Invoke(this, clickEvent);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in ClickDetected handler");
                        }
                    });
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private ClickEvent BuildClickEvent(NativeMethods.MSLLHOOKSTRUCT hookStruct, ClickType clickType, IntPtr hwnd)
    {
        var windowTitle = GetWindowTitle(hwnd);
        var appName = GetApplicationName(hwnd);

        // Capture DPI scale for the monitor
        var dpiScale = 1.0;
        try
        {
            var dpi = NativeMethods.GetDpiForWindow(hwnd);
            if (dpi > 0)
                dpiScale = dpi / 96.0;
        }
        catch
        {
            // GetDpiForWindow may not be available on older builds
        }

        // Query UI Automation for the element at the click point
        string? uiaControlType = null;
        string? uiaElementName = null;
        string? uiaAutomationId = null;
        string? uiaClassName = null;
        string? uiaElementBounds = null;

        try
        {
            var point = new System.Windows.Point(hookStruct.pt.x, hookStruct.pt.y);
            var element = AutomationElement.FromPoint(point);
            if (element is not null)
            {
                uiaControlType = element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "");
                uiaElementName = string.IsNullOrWhiteSpace(element.Current.Name) ? null : element.Current.Name;
                uiaAutomationId = string.IsNullOrWhiteSpace(element.Current.AutomationId) ? null : element.Current.AutomationId;
                uiaClassName = string.IsNullOrWhiteSpace(element.Current.ClassName) ? null : element.Current.ClassName;

                var rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                {
                    uiaElementBounds = JsonSerializer.Serialize(new
                    {
                        x = (int)rect.X,
                        y = (int)rect.Y,
                        width = (int)rect.Width,
                        height = (int)rect.Height
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UIA query failed for click at ({X},{Y})", hookStruct.pt.x, hookStruct.pt.y);
        }

        return new ClickEvent
        {
            Timestamp = DateTime.UtcNow - _sessionStart,
            X = hookStruct.pt.x,
            Y = hookStruct.pt.y,
            ClickType = clickType,
            WindowTitle = windowTitle,
            ApplicationName = appName,
            UiaControlType = uiaControlType,
            UiaElementName = uiaElementName,
            UiaAutomationId = uiaAutomationId,
            UiaClassName = uiaClassName,
            UiaElementBounds = uiaElementBounds,
            DpiScale = dpiScale
        };
    }

    private static ClickType? GetClickType(IntPtr wParam)
    {
        var msg = wParam.ToInt32();
        // Note: WH_MOUSE_LL does not receive WM_LBUTTONDBLCLK;
        // double-clicks are detected via debounce timing at a higher level.
        return msg switch
        {
            NativeMethods.WM_LBUTTONDOWN => ClickType.LeftClick,
            NativeMethods.WM_RBUTTONDOWN => ClickType.RightClick,
            NativeMethods.WM_MBUTTONDOWN => ClickType.MiddleClick,
            _ => null
        };
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetWindowTextW(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string? GetApplicationName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
