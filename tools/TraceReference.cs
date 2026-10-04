// ============================================================================
//  TraceReference.cs - dev tool (not shipped).
//
//  Reconstructs the reference icon's mark as a small set of rectangles, by
//  scanning row by row and column by column. Output is printed as both pixel
//  coordinates and fractions of the badge, ready to paste into GenIcon.
//
//  Written after an eyeballed reproduction matched only 25% of the reference's
//  mark pixels. Guessing positions from a rendered image does not work; the
//  spans have to be measured.
//
//  Usage:  TraceReference.exe <frame.png>
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

internal static class TraceReference
{
    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("usage: TraceReference.exe <png>"); return 2; }
        using (Bitmap bmp = new Bitmap(args[0]))
        {
            int w = bmp.Width, h = bmp.Height;

            // ---- badge bounding box, from the shadow-free opaque area --------
            int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (bmp.GetPixel(x, y).A > 200)
                    {
                        if (x < bx0) bx0 = x; if (x > bx1) bx1 = x;
                        if (y < by0) by0 = y; if (y > by1) by1 = y;
                    }
            Console.WriteLine("image " + w + "x" + h);
            Console.WriteLine("badge px  x " + bx0 + ".." + bx1 + "  y " + by0 + ".." + by1
                              + "   size " + (bx1 - bx0 + 1) + "x" + (by1 - by0 + 1));
            Console.WriteLine();

            // ---- horizontal white runs ---------------------------------------
            Console.WriteLine("WHITE horizontal runs (row, x-span, and fraction of badge)");
            foreach (Band b in FindBands(bmp, Kind.White, true))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  y {0,3}..{1,3} (cy {2,6:0.0}) thick {3,2}   x {4,3}..{5,3}   fx {6:0.000}..{7:0.000}   fy {8:0.000}",
                    b.Lo, b.Hi, (b.Lo + b.Hi) / 2f, b.Hi - b.Lo + 1, b.S0, b.S1,
                    Fx(b.S0, bx0, bx1), Fx(b.S1, bx0, bx1), Fy((b.Lo + b.Hi) / 2f, by0, by1)));
            }
            Console.WriteLine();

            Console.WriteLine("BRASS horizontal runs");
            foreach (Band b in FindBands(bmp, Kind.Brass, true))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  y {0,3}..{1,3} (cy {2,6:0.0}) thick {3,2}   x {4,3}..{5,3}   fx {6:0.000}..{7:0.000}   fy {8:0.000}",
                    b.Lo, b.Hi, (b.Lo + b.Hi) / 2f, b.Hi - b.Lo + 1, b.S0, b.S1,
                    Fx(b.S0, bx0, bx1), Fx(b.S1, bx0, bx1), Fy((b.Lo + b.Hi) / 2f, by0, by1)));
            }
            Console.WriteLine();

            Console.WriteLine("WHITE vertical runs");
            foreach (Band b in FindBands(bmp, Kind.White, false))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  x {0,3}..{1,3} (cx {2,6:0.0}) thick {3,2}   y {4,3}..{5,3}   fx {6:0.000}   fy {7:0.000}..{8:0.000}",
                    b.Lo, b.Hi, (b.Lo + b.Hi) / 2f, b.Hi - b.Lo + 1, b.S0, b.S1,
                    Fx((b.Lo + b.Hi) / 2f, bx0, bx1), Fy(b.S0, by0, by1), Fy(b.S1, by0, by1)));
            }
            Console.WriteLine();

            Console.WriteLine("BRASS vertical runs");
            foreach (Band b in FindBands(bmp, Kind.Brass, false))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  x {0,3}..{1,3} (cx {2,6:0.0}) thick {3,2}   y {4,3}..{5,3}   fx {6:0.000}",
                    b.Lo, b.Hi, (b.Lo + b.Hi) / 2f, b.Hi - b.Lo + 1, b.S0, b.S1,
                    Fx((b.Lo + b.Hi) / 2f, bx0, bx1)));
            }
        }
        return 0;
    }

    private static string Fx(float v, int a, int b) { return ((v - a) / (float)(b - a)).ToString("0.000", CultureInfo.InvariantCulture); }
    private static string Fy(float v, int a, int b) { return ((v - a) / (float)(b - a)).ToString("0.000", CultureInfo.InvariantCulture); }

    private enum Kind { White, Brass }

    private class Band
    {
        public int Lo, Hi;      // index range along the scanning axis
        public int S0, S1;      // span along the other axis
    }

    /// <summary>
    /// Group consecutive rows (or columns) whose mark extent is long enough to be
    /// a trace rather than a pad. Pads are separated because their span along the
    /// scanning axis is short.
    /// </summary>
    private static List<Band> FindBands(Bitmap bmp, Kind want, bool horizontal)
    {
        int w = bmp.Width, h = bmp.Height;
        int outer = horizontal ? h : w;
        int inner = horizontal ? w : h;

        int[] count = new int[outer];
        int[] lo = new int[outer];
        int[] hi = new int[outer];
        for (int o = 0; o < outer; o++)
        {
            int c = 0, a = int.MaxValue, b = -1;
            for (int i = 0; i < inner; i++)
            {
                Color px = horizontal ? bmp.GetPixel(i, o) : bmp.GetPixel(o, i);
                if (Match(px, want)) { c++; if (i < a) a = i; if (i > b) b = i; }
            }
            count[o] = c; lo[o] = a; hi[o] = b;
        }

        int longest = 0;
        for (int o = 0; o < outer; o++) if (count[o] > longest) longest = count[o];

        // A "run" row must hold at least half of the longest extent measured,
        // which excludes pad rows and the thin necks of the stems.
        int threshold = Math.Max(6, (int)(longest * 0.45));

        List<Band> bands = new List<Band>();
        int i2 = 0;
        while (i2 < outer)
        {
            if (count[i2] < threshold) { i2++; continue; }
            int start = i2, end = i2;
            while (end + 1 < outer && count[end + 1] >= threshold && end + 1 - start < 40) end++;

            int s0 = int.MaxValue, s1 = -1;
            for (int o = start; o <= end; o++)
            {
                if (lo[o] < s0) s0 = lo[o];
                if (hi[o] > s1) s1 = hi[o];
            }
            bands.Add(new Band { Lo = start, Hi = end, S0 = s0, S1 = s1 });
            i2 = end + 1;
        }
        return bands;
    }

    private static bool Match(Color c, Kind want)
    {
        if (c.A < 80) return false;
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        int sat = max - min;
        if (want == Kind.Brass) return c.R - c.B > 35 && c.R > 120;
        return sat < 34 && max > 205;
    }
}
