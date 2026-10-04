// ============================================================================
//  ExtractIconFrames.cs - dev tool (not shipped).
//
//  Pulls every icon frame out of an executable by size, so the geometry of a
//  reference icon can be measured at a resolution high enough to be meaningful.
//  Icon.ExtractAssociatedIcon only ever returns one small frame, which is not
//  enough to line up an accurate copy.
//
//  Usage:  ExtractIconFrames.exe <exe-or-dll> <outputDir>
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class ExtractIconFrames
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIcons(string file, int index, int cx, int cy,
                                                  IntPtr[] handles, int[] ids, int count, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);

    private static readonly int[] Sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    private static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: ExtractIconFrames.exe <file> <outDir>"); return 2; }
        string file = args[0];
        string dir = args[1];
        Directory.CreateDirectory(dir);

        int found = 0;
        foreach (int s in Sizes)
        {
            IntPtr[] h = new IntPtr[1];
            int[] id = new int[1];
            int n = PrivateExtractIcons(file, 0, s, s, h, id, 1, 0);
            if (n <= 0 || h[0] == IntPtr.Zero)
            {
                Console.WriteLine("  " + s + "px  : none");
                continue;
            }
            using (Icon ic = Icon.FromHandle(h[0]))
            using (Bitmap b = ic.ToBitmap())
            {
                string p = Path.Combine(dir, "frame_" + b.Width + "x" + b.Height + ".png");
                b.Save(p, ImageFormat.Png);
                Console.WriteLine("  asked " + s + "px -> got " + b.Width + "x" + b.Height + "  " + p);
                found++;
            }
            DestroyIcon(h[0]);
        }
        Console.WriteLine("frames written: " + found);
        return 0;
    }
}
