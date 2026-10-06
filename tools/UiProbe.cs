// ============================================================================
//  UiProbe.cs - dev tool (not shipped).
//
//  Drives the launcher's UI from outside: restores the window if it is
//  minimised, reports where it actually is, and clicks a point given in client
//  coordinates.
//
//  Written after a series of failed attempts to script this through the shell.
//  The failures were not random - they had three separate causes, and each one
//  silently produced "the click did nothing":
//
//    * the window was minimised, sitting at (-16000,-16000), so every synthetic
//      click landed on empty desktop
//    * Process.MainWindowHandle caches its value, so a handle captured earlier
//      kept being used after the window had been recreated
//    * a stale handle makes ClientToScreen return nonsense rather than failing
//
//  Hence: find the handle fresh each run, restore the window, and print the real
//  geometry so the caller can see whether a click could possibly have landed.
//
//  Usage:  UiProbe.exe <pid> info
//          UiProbe.exe <pid> click <clientX> <clientY>
//          UiProbe.exe <pid> rect <x> <y> <w> <h>
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal static class UiProbe
{
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;
    private const uint LEFTDOWN = 0x0002;
    private const uint LEFTUP = 0x0004;
    private const int SWP_NOZORDER = 0x0004;
    private const uint GW_OWNER = 4;

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);

    private static int Main(string[] args)
    {
        if (args.Length < 2) { Usage(); return 2; }
        int pid;
        if (!int.TryParse(args[0], out pid)) { Console.Error.WriteLine("bad pid"); return 2; }
        string cmd = args[1].ToLowerInvariant();

        IntPtr h = FindMain(pid);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("no top-level window found for pid " + pid); return 1; }

        if (cmd == "info")
        {
            Dump(pid, h);
            return 0;
        }

        if (cmd == "rect")
        {
            if (args.Length < 6) { Usage(); return 2; }
            // Restore first: MoveWindow on a minimised window does not bring it back.
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            ShowWindow(h, SW_SHOW);
            System.Threading.Thread.Sleep(400);
            MoveWindow(h, int.Parse(args[2]), int.Parse(args[3]),
                          int.Parse(args[4]), int.Parse(args[5]), true);
            System.Threading.Thread.Sleep(600);
            SetForegroundWindow(h);
            System.Threading.Thread.Sleep(400);
            Console.WriteLine("after rect:");
            Dump(pid, h);
            return 0;
        }

        if (cmd == "click")
        {
            if (args.Length < 4) { Usage(); return 2; }
            int cx = int.Parse(args[2]), cy = int.Parse(args[3]);

            // Everything below is why this tool exists: without restoring and
            // verifying, a click can silently go nowhere.
            if (IsIconic(h)) { Console.WriteLine("window was minimised - restoring"); ShowWindow(h, SW_RESTORE); }
            ShowWindow(h, SW_SHOW);
            SetForegroundWindow(h);
            System.Threading.Thread.Sleep(500);

            RECT wr, cr;
            GetWindowRect(h, out wr);
            GetClientRect(h, out cr);
            Console.WriteLine("windowRect = (" + wr.Left + "," + wr.Top + ")-(" + wr.Right + "," + wr.Bottom + ")");
            Console.WriteLine("clientSize = " + cr.Right + "x" + cr.Bottom);

            if (cx < 0 || cy < 0 || cx >= cr.Right || cy >= cr.Bottom)
            {
                Console.Error.WriteLine("client point (" + cx + "," + cy + ") is outside the client area - refusing");
                return 1;
            }

            POINT p = new POINT();
            p.X = cx; p.Y = cy;
            if (!ClientToScreen(h, ref p)) { Console.Error.WriteLine("ClientToScreen failed"); return 1; }

            IntPtr under = WindowFromPoint(p);
            StringBuilder cls = new StringBuilder(256); GetClassNameW(under, cls, 256);
            StringBuilder txt = new StringBuilder(256); GetWindowTextW(under, txt, 256);
            Console.WriteLine("click client(" + cx + "," + cy + ") -> screen(" + p.X + "," + p.Y + ")");
            Console.WriteLine("  window under point = " + under + " [" + cls + "] \"" + txt + "\"");

            SetCursorPos(p.X, p.Y);
            System.Threading.Thread.Sleep(400);
            mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(150);
            mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
            Console.WriteLine("clicked");
            return 0;
        }

        Usage();
        return 2;
    }

    private static void Dump(int pid, IntPtr h)
    {
        RECT wr, cr;
        GetWindowRect(h, out wr);
        GetClientRect(h, out cr);
        StringBuilder txt = new StringBuilder(256); GetWindowTextW(h, txt, 256);
        Console.WriteLine("pid        = " + pid);
        Console.WriteLine("hwnd       = " + h + "  \"" + txt + "\"");
        Console.WriteLine("visible    = " + IsWindowVisible(h) + "   minimised = " + IsIconic(h));
        Console.WriteLine("windowRect = (" + wr.Left + "," + wr.Top + ")-(" + wr.Right + "," + wr.Bottom + ")   "
                          + (wr.Right - wr.Left) + "x" + (wr.Bottom - wr.Top));
        Console.WriteLine("clientSize = " + cr.Right + "x" + cr.Bottom);
        Console.WriteLine("on screen  = " + (wr.Left > -10000 && wr.Top > -10000));
    }

    /// <summary>Top-level, unowned window of the process - the real form.</summary>
    private static IntPtr FindMain(int pid)
    {
        IntPtr best = IntPtr.Zero;
        List<IntPtr> all = new List<IntPtr>();
        EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            int q;
            GetWindowThreadProcessId(h, out q);
            if (q == pid && GetWindow(h, GW_OWNER) == IntPtr.Zero) all.Add(h);
            return true;
        }, IntPtr.Zero);

        // Prefer a WinForms window; the app also owns hidden helper windows.
        foreach (IntPtr h in all)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassNameW(h, sb, 256);
            if (sb.ToString().StartsWith("WindowsForms", StringComparison.Ordinal)) return h;
        }
        foreach (IntPtr h in all)
            if (IsWindowVisible(h)) return h;
        return best;
    }

    private static void Usage()
    {
        Console.Error.WriteLine("usage: UiProbe.exe <pid> info");
        Console.Error.WriteLine("       UiProbe.exe <pid> click <clientX> <clientY>");
        Console.Error.WriteLine("       UiProbe.exe <pid> rect <x> <y> <w> <h>");
    }
}
