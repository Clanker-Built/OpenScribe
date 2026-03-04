using System.Diagnostics;
using OpenScribe.Capture.Interop;
using OpenScribe.Core.Interfaces;
using OpenScribe.Core.Models;

namespace OpenScribe.Capture.Services;

/// <summary>
/// Enumerates monitors and visible top-level windows for the capture scope picker.
/// </summary>
public class CaptureTargetEnumerator : ICaptureTargetEnumerator
{
    public IReadOnlyList<MonitorInfo> EnumerateMonitors()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var result = new List<MonitorInfo>(screens.Length);

        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var bounds = screen.Bounds;
            var primary = screen.Primary ? " \u2014 Primary" : "";
            var displayName = $"Monitor {i + 1} ({bounds.Width}x{bounds.Height}){primary}";

            result.Add(new MonitorInfo(
                DeviceName: screen.DeviceName,
                DisplayName: displayName,
                Left: bounds.X,
                Top: bounds.Y,
                Width: bounds.Width,
                Height: bounds.Height,
                IsPrimary: screen.Primary));
        }

        return result;
    }

    public IReadOnlyList<CaptureWindowInfo> EnumerateWindows()
    {
        var result = new List<CaptureWindowInfo>();
        var ourPid = Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            // Skip tool windows
            var exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            // Get window title
            var titleBuffer = new char[256];
            var titleLength = NativeMethods.GetWindowTextW(hwnd, titleBuffer, titleBuffer.Length);
            if (titleLength == 0)
                return true;

            var title = new string(titleBuffer, 0, titleLength);

            // Get process info
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if ((int)pid == ourPid)
                return true;

            string? processName = null;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch
            {
                // Process may have exited
            }

            result.Add(new CaptureWindowInfo(
                Handle: hwnd,
                ProcessId: (int)pid,
                Title: title,
                ProcessName: processName));

            return true;
        }, IntPtr.Zero);

        return result;
    }
}
