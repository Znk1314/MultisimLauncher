// ============================================================================
//  WindowInfo.cs - diagnostic helper (dev only, not shipped).
//
//  Reports, from a DPI-aware process, what a window's real pixel geometry is
//  and which DPI awareness context the process declared. Written because a
//  non-DPI-aware host PowerShell sees virtualised coordinates and therefore
//  cannot tell whether a window is laid out correctly.
//
//  Usage:  WindowInfo.exe <processName>
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
    [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

    private static int Main(string[] args)
    {
        SetProcessDPIAware();   // must come first

        string name = args.Length > 0 ? args[0] : "MultisimLauncher";

        Process[] procs;
        try { procs = Process.GetProcessesByName(name); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }

        if (procs.Length == 0) { Console.WriteLine("no process named " + name); return 1; }

        int awareness = -1;
        try { awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()); } catch { }
        string[] awarenessNames = { "DPI_AWARENESS_INVALID", "UNAWARE", "SYSTEM_AWARE", "PER_MONITOR_AWARE" };
        string aw = (awareness >= 0 && awareness < awarenessNames.Length) ? awarenessNames[awareness] : "?";

        Console.WriteLine("probe awareness = " + aw + "  (this probe process)");
        Console.WriteLine();

        foreach (Process p in procs)
        {
            Console.WriteLine("process " + p.ProcessName + " pid=" + p.Id);
            List<IntPtr> wins = new List<IntPtr>();
            uint want = (uint)p.Id;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint w; GetWindowThreadProcessId(h, out w);
                if (w == want) wins.Add(h);
                return true;
            }, IntPtr.Zero);

            bool any = false;
            foreach (IntPtr h in wins)
            {
                if (!IsWindowVisible(h)) continue;
                StringBuilder t = new StringBuilder(512); GetWindowTextW(h, t, 512);
                string title = t.ToString();
                if (title.Length == 0) continue;         // skip hidden helpers
                StringBuilder c = new StringBuilder(256); GetClassNameW(h, c, 256);

                RECT wr, cr;
                GetWindowRect(h, out wr);
                GetClientRect(h, out cr);
                uint dpi = 0;
                try { dpi = GetDpiForWindow(h); } catch { }

                any = true;
                Console.WriteLine("  hwnd=0x" + h.ToInt64().ToString("X"));
                Console.WriteLine("    class      : " + c);
                Console.WriteLine("    title      : " + title);
                Console.WriteLine("    window rect: " + (wr.Right - wr.Left) + " x " + (wr.Bottom - wr.Top)
                                  + "   at (" + wr.Left + "," + wr.Top + ")");
                Console.WriteLine("    client rect: " + (cr.Right - cr.Left) + " x " + (cr.Bottom - cr.Top));
                Console.WriteLine("    window dpi : " + dpi + "  (" + (dpi == 0 ? "?" : (100 * (int)dpi / 96) + "%") + ")");
            }
            if (!any) Console.WriteLine("  (no visible titled windows)");
        }
        return 0;
    }
}
