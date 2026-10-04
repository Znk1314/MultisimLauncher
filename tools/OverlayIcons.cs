// ============================================================================
//  OverlayIcons.cs - dev tool (not shipped).
//
//  Produces two diagnostic images:
//    1. a 50/50 overlay of the reference and our icon, so any misalignment shows
//       as a doubled edge
//    2. a difference map - where the two disagree, painted on the reference
//
//  Checking geometry by eye across two separate pictures is unreliable; an
//  overlay makes an offset of even a couple of pixels obvious.
//
//  Usage:  OverlayIcons.exe <ref.png> <our.ico> <outOverlay.png> <outDiff.png>
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class OverlayIcons
{
    private static int Main(string[] args)
    {
        if (args.Length < 4) { Console.Error.WriteLine("usage: OverlayIcons.exe <ref.png> <our.ico> <overlay.png> <diff.png>"); return 2; }
        using (Bitmap reference = new Bitmap(Image.FromFile(args[0])))
        using (Bitmap ours = DecodeLargestFrame(args[1]))
        {
            if (ours == null) { Console.Error.WriteLine("no frame decoded"); return 1; }
            int w = Math.Min(reference.Width, ours.Width);
            int h = Math.Min(reference.Height, ours.Height);

            // ---- 1. overlay: reference tinted blue, ours tinted red -----------
            using (Bitmap overlay = new Bitmap(w, h))
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color a = reference.GetPixel(x, y);
                        Color b = ours.GetPixel(x, y);
                        // Where the two agree the result is neutral; disagreement
                        // shows as blue (only reference) or red (only ours).
                        int r = (int)(b.R * 0.55f + 40);
                        int g = (int)((a.G + b.G) * 0.30f);
                        int bl = (int)(a.B * 0.55f + 40);
                        overlay.SetPixel(x, y, Color.FromArgb(255,
                            Math.Min(255, r), Math.Min(255, g), Math.Min(255, bl)));
                    }
                overlay.Save(args[2], ImageFormat.Png);
                Console.WriteLine("wrote " + args[2]);
            }

            // ---- 2. difference map -------------------------------------------
            // Report agreement only on the mark itself (bright pixels), because
            // the badge backgrounds differ by design and would swamp the count.
            using (Bitmap diff = new Bitmap(w, h))
            {
                int refMark = 0, ourMark = 0, both = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        bool a = IsMark(reference.GetPixel(x, y));
                        bool b = IsMark(ours.GetPixel(x, y));
                        if (a) refMark++;
                        if (b) ourMark++;
                        if (a && b) both++;

                        Color c;
                        if (a && b) c = Color.FromArgb(255, 70, 170, 90);       // agree
                        else if (a) c = Color.FromArgb(255, 60, 110, 235);      // reference only
                        else if (b) c = Color.FromArgb(255, 220, 70, 60);       // ours only
                        else c = Color.FromArgb(255, 236, 238, 242);
                        diff.SetPixel(x, y, c);
                    }
                diff.Save(args[3], ImageFormat.Png);
                Console.WriteLine("wrote " + args[3]);
                Console.WriteLine("mark pixels  reference=" + refMark + "  ours=" + ourMark
                                  + "  overlap=" + both);
                if (refMark > 0)
                    Console.WriteLine("coverage of reference = " + (100.0 * both / refMark).ToString("0.0") + "%");
                if (ourMark > 0)
                    Console.WriteLine("precision vs reference = " + (100.0 * both / ourMark).ToString("0.0") + "%");
            }
        }
        return 0;
    }

    /// <summary>Is this pixel part of the drawn mark (a trace or pad)?</summary>
    private static bool IsMark(Color c)
    {
        if (c.A < 80) return false;
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        int sat = max - min;
        // brass, or bright neutral
        if (c.R - c.B > 35 && c.R > 120) return true;
        return sat < 34 && max > 205;
    }

    /// <summary>Decode the largest frame, handling PNG-stored frames.</summary>
    private static Bitmap DecodeLargestFrame(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 6) return null;
        int count = BitConverter.ToUInt16(data, 4);
        int bestSize = -1, bestOffset = 0, bestLength = 0;
        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            int w = data[e] == 0 ? 256 : data[e];
            int len = (int)BitConverter.ToUInt32(data, e + 8);
            int off = (int)BitConverter.ToUInt32(data, e + 12);
            if (w > bestSize) { bestSize = w; bestOffset = off; bestLength = len; }
        }
        if (bestSize < 0) return null;
        byte[] frame = new byte[bestLength];
        Array.Copy(data, bestOffset, frame, 0, bestLength);

        if (frame.Length > 8 && frame[0] == 0x89 && frame[1] == 0x50 && frame[2] == 0x4E && frame[3] == 0x47)
        {
            using (MemoryStream ms = new MemoryStream(frame))
            using (Image img = Image.FromStream(ms))
                return new Bitmap(img);
        }

        int headerSize = BitConverter.ToInt32(frame, 0);
        int wpx = BitConverter.ToInt32(frame, 4);
        int hpx = BitConverter.ToInt32(frame, 8) / 2;
        if (BitConverter.ToUInt16(frame, 14) != 32) return null;

        Bitmap bmp = new Bitmap(wpx, hpx, PixelFormat.Format32bppArgb);
        for (int y = 0; y < hpx; y++)
            for (int x = 0; x < wpx; x++)
            {
                int p = headerSize + ((hpx - 1 - y) * wpx + x) * 4;
                if (p + 3 >= frame.Length) break;
                bmp.SetPixel(x, y, Color.FromArgb(frame[p + 3], frame[p + 2], frame[p + 1], frame[p]));
            }
        return bmp;
    }
}
