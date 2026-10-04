// ============================================================================
//  Shot.cs - tiny screenshot helper used by tools/screenshot.ps1 to produce
//  assets/screenshot.png for the README. Not shipped to users.
//
//  Usage:  Shot.exe <processId> <outputPath> [settleMs]
//
//  Written because capturing a window from a host PowerShell is unreliable:
//  the host is usually not DPI aware, so GetWindowRect returns virtualised
//  coordinates and the capture lands on the wrong part of the screen.
//  This program declares DPI awareness for itself.
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

internal static class Shot
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Shot.exe <pid> <out.png> [settleMs]");
            return 2;
        }

        int pid = int.Parse(args[0]);
        string outPath = args[1];
        int settle = args.Length > 2 ? int.Parse(args[2]) : 800;

        // Must happen before any window query, otherwise the coordinates we get
        // back are scaled and the capture misses the window.
        SetProcessDPIAware();

        Process p = Process.GetProcessById(pid);
        IntPtr h = IntPtr.Zero;
        for (int i = 0; i < 40; i++)
        {
            p.Refresh();
            h = p.MainWindowHandle;
            if (h != IntPtr.Zero && IsWindowVisible(h)) break;
            Thread.Sleep(250);
        }
        if (h == IntPtr.Zero)
        {
            Console.Error.WriteLine("no visible main window for pid " + pid);
            return 1;
        }

        ShowWindow(h, 9);            // SW_RESTORE
        SetForegroundWindow(h);
        Thread.Sleep(settle);

        // Capture the client area only - no title bar, no borders, which is what
        // a README screenshot should show.
        RECT cr;
        if (!GetClientRect(h, out cr)) { Console.Error.WriteLine("GetClientRect failed"); return 1; }
        POINT origin = new POINT();
        origin.X = 0; origin.Y = 0;
        if (!ClientToScreen(h, ref origin)) { Console.Error.WriteLine("ClientToScreen failed"); return 1; }

        int w = cr.Right - cr.Left;
        int ht = cr.Bottom - cr.Top;
        if (w <= 0 || ht <= 0) { Console.Error.WriteLine("bad client size " + w + "x" + ht); return 1; }
        if (w > 4000 || ht > 4000) { Console.Error.WriteLine("client size out of range " + w + "x" + ht); return 1; }

        using (Bitmap bmp = new Bitmap(w, ht))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(w, ht), CopyPixelOperation.SourceCopy);
            }
            bmp.Save(outPath, ImageFormat.Png);
        }

        Console.WriteLine("captured " + w + "x" + ht + " -> " + outPath);
        return 0;
    }
}
