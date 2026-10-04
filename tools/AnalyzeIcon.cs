// ============================================================================
//  AnalyzeIcon.cs - dev tool (not shipped).
//
//  Reads the NI Multisim icon and reports, in pixel coordinates, where every
//  line and pad sits. Guessing these positions by eye kept producing an icon
//  that was recognisably similar but not aligned, so they are measured instead.
//
//  Classification is by hue/lightness, which is enough to separate the three
//  groups that matter: the white traces, the brass traces, and the badge.
//
//  Usage:  AnalyzeIcon.exe <icon.png> [gridDivisions]
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

internal static class AnalyzeIcon
{
    private enum Kind { Bg, White, Brass }

    private static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("usage: AnalyzeIcon.exe <png> [div]"); return 2; }
        int div = args.Length > 1 ? int.Parse(args[1]) : 32;

        using (Bitmap bmp = new Bitmap(args[0]))
        {
            int w = bmp.Width, h = bmp.Height;
            Console.WriteLine("image " + w + "x" + h + "   (reporting in /" + div + " units)");
            Console.WriteLine();

            Kind[,] k = new Kind[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    k[x, y] = Classify(bmp.GetPixel(x, y));

            // ---- badge extent, from anything that is not fully transparent ----
            int bx0 = w, by0 = h, bx1 = -1, by1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (bmp.GetPixel(x, y).A > 40)
                    {
                        if (x < bx0) bx0 = x; if (x > bx1) bx1 = x;
                        if (y < by0) by0 = y; if (y > by1) by1 = y;
                    }
            Console.WriteLine("badge bbox px   : x " + bx0 + ".." + bx1 + "   y " + by0 + ".." + by1);
            Console.WriteLine("badge bbox frac : x " + F(bx0, w) + ".." + F(bx1, w) + "   y " + F(by0, h) + ".." + F(by1, h));
            Console.WriteLine();

            // ---- horizontal runs: rows with a long horizontal extent -----------
            Console.WriteLine("HORIZONTAL RUNS (white)");
            ReportRunsDetailed(k, w, h, Kind.White, div);
            Console.WriteLine();
            Console.WriteLine("HORIZONTAL RUNS (brass)");
            ReportRunsDetailed(k, w, h, Kind.Brass, div);
            Console.WriteLine();

            // ---- vertical stems ------------------------------------------------
            Console.WriteLine("VERTICAL STEMS");
            ReportStems(k, w, h, Kind.White, div);
            Console.WriteLine();
            Console.WriteLine("VERTICAL STEMS (brass)");
            ReportStems(k, w, h, Kind.Brass, div);
        }
        return 0;
    }

    private static Kind Classify(Color c)
    {
        if (c.A < 60) return Kind.Bg;
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        int sat = max - min;

        // Brass: warm, red above blue by a clear margin.
        if (c.R - c.B > 35 && c.R > 120) return Kind.Brass;
        // White trace: near-neutral AND genuinely bright. The threshold has to
        // be high because the badge itself is a light lilac in places; a low
        // threshold classified half the badge as a trace.
        if (sat < 34 && max > 205) return Kind.White;
        return Kind.Bg;
    }

    /// <summary>Report only the runs, with pad positions, at one size.</summary>
    private static void ReportRunsDetailed(Kind[,] k, int w, int h, Kind want, int div)
    {
        // For every row, note how many pixels of this kind it holds.
        int[] count = new int[h];
        int[] rowX0 = new int[h];
        int[] rowX1 = new int[h];
        for (int y = 0; y < h; y++)
        {
            int c = 0, x0 = int.MaxValue, x1 = -1;
            for (int x = 0; x < w; x++)
                if (k[x, y] == want) { c++; if (x < x0) x0 = x; if (x > x1) x1 = x; }
            count[y] = c; rowX0[y] = x0; rowX1[y] = x1;
        }

        // Group rows where the horizontal extent is long: those are the runs.
        // A pad row has a much shorter extent, which is how pads get separated.
        List<int> longRows = new List<int>();
        int longest = 0;
        for (int y = 0; y < h; y++) if (count[y] > longest) longest = count[y];
        int runThreshold = (int)(longest * 0.55);
        for (int y = 0; y < h; y++) if (count[y] >= runThreshold) longRows.Add(y);

        int i = 0;
        while (i < longRows.Count)
        {
            int start = longRows[i], end = longRows[i];
            while (i + 1 < longRows.Count && longRows[i + 1] <= end + 3) { i++; end = longRows[i]; }

            int x0 = int.MaxValue, x1 = -1;
            for (int y = start; y <= end; y++)
            {
                if (rowX0[y] < x0) x0 = rowX0[y];
                if (rowX1[y] > x1) x1 = rowX1[y];
            }
            float cy = (start + end) / 2f;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  RUN   y {0,3}..{1,3} cy={2,6:0.0}  thick={3,2}   x {4,3}..{5,3}",
                start, end, cy, end - start + 1, x0, x1));
            i++;
        }
        if (longRows.Count == 0) Console.WriteLine("  (no runs)");
    }

    private static void ReportRuns(Kind[,] k, int w, int h, Kind want, int div)
    {
        List<int> rows = new List<int>();
        int[] extent = new int[h];
        for (int y = 0; y < h; y++)
        {
            int count = 0, x0 = int.MaxValue, x1 = -1;
            for (int x = 0; x < w; x++)
                if (k[x, y] == want) { count++; if (x < x0) x0 = x; if (x > x1) x1 = x; }
            extent[y] = count;
            if (count > w * 0.25) rows.Add(y);          // a long run, not a pad
        }

        // group adjacent rows into bands
        int i2 = 0;
        while (i2 < rows.Count)
        {
            int start = rows[i2], end = rows[i2];
            while (i2 + 1 < rows.Count && rows[i2 + 1] == end + 1) { i2++; end = rows[i2]; }

            int x0 = int.MaxValue, x1 = -1;
            for (int y = start; y <= end; y++)
                for (int x = 0; x < w; x++)
                    if (k[x, y] == want) { if (x < x0) x0 = x; if (x > x1) x1 = x; }

            float cy = (start + end) / 2f;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  y px {0,3}..{1,3} (cy {2,5}) thick {3,2}   x px {4,3}..{5,3}   |  cy/{6} = {7}   x/{6} = {8}..{9}",
                start, end, cy, end - start + 1, x0, x1, div, F2(cy, div, h), F2(x0, div, w), F2(x1, div, w)));
            i2++;
        }
        if (rows.Count == 0) Console.WriteLine("  (none)");
    }

    private static void ReportStems(Kind[,] k, int w, int h, Kind want, int div)
    {
        List<int> cols = new List<int>();
        for (int x = 0; x < w; x++)
        {
            int count = 0;
            for (int y = 0; y < h; y++) if (k[x, y] == want) count++;
            if (count > h * 0.22) cols.Add(x);          // tall enough to be a stem
        }
        int i2 = 0;
        while (i2 < cols.Count)
        {
            int start = cols[i2], end = cols[i2];
            while (i2 + 1 < cols.Count && cols[i2 + 1] == end + 1) { i2++; end = cols[i2]; }

            int y0 = int.MaxValue, y1 = -1;
            for (int x = start; x <= end; x++)
                for (int y = 0; y < h; y++)
                    if (k[x, y] == want) { if (y < y0) y0 = y; if (y > y1) y1 = y; }

            float cx = (start + end) / 2f;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  x px {0,3}..{1,3} (cx {2,5}) thick {3,2}   y px {4,3}..{5,3}   |  cx/{6} = {7}",
                start, end, cx, end - start + 1, y0, y1, div, F2(cx, div, w)));
            i2++;
        }
        if (cols.Count == 0) Console.WriteLine("  (none)");
    }

    private static string F(int v, int total)
    {
        return ((double)v / total).ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static string F2(float v, int div, int total)
    {
        return (v / total * div).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
