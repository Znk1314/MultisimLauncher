// ============================================================================
//  CompareIcons.cs - dev tool (not shipped).
//
//  Renders a side-by-side sheet: a reference icon frame next to the matching
//  frame from our own .ico, at the same scale, with each frame placed at its
//  true position.
//
//  This exists because System.Drawing.Icon silently falls back to a smaller
//  frame when the requested size is stored as PNG inside the ICO, which made an
//  earlier comparison misleading. Here the frames are decoded directly.
//
//  Usage:  CompareIcons.exe <referenceFrame.png> <our.ico> <out.png>
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class CompareIcons
{
    private static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: CompareIcons.exe <ref.png> <our.ico> <out.png>"); return 2; }
        string refPath = args[0], icoPath = args[1], outPath = args[2];

        int canvas = 256;
        using (Image reference = Image.FromFile(refPath))
        using (Bitmap ours = DecodeLargestFrame(icoPath))
        {
            if (ours == null) { Console.Error.WriteLine("could not decode a frame from " + icoPath); return 1; }

            int label = 34;
            int pad = 22;
            Bitmap sheet = new Bitmap(pad * 3 + canvas * 2, label + pad * 2 + canvas);
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.FromArgb(245, 247, 250));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                DrawContained(g, reference, new Rectangle(pad, label, canvas, canvas));
                DrawContained(g, ours, new Rectangle(pad * 2 + canvas, label, canvas, canvas));

                using (Font f = new Font("Segoe UI", 11f))
                using (Brush b = new SolidBrush(Color.FromArgb(38, 48, 62)))
                {
                    g.DrawString("NI Multisim  (reference, 256px frame)", f, b, pad, 8);
                    g.DrawString("Multisim Launcher  (ours, 256px frame)", f, b, pad * 2 + canvas, 8);
                }

                // a grid to make offset differences easy to spot
                using (Pen gp = new Pen(Color.FromArgb(28, 0, 0, 0), 1f))
                {
                    for (int i = 1; i < 8; i++)
                    {
                        int x = pad + canvas / 8 * i;
                        g.DrawLine(gp, x, label, x, label + canvas);
                        int x2 = pad * 2 + canvas + canvas / 8 * i;
                        g.DrawLine(gp, x2, label, x2, label + canvas);
                    }
                    for (int i = 1; i < 8; i++)
                    {
                        int y = label + canvas / 8 * i;
                        g.DrawLine(gp, pad, y, pad + canvas, y);
                        g.DrawLine(gp, pad * 2 + canvas, y, pad * 2 + canvas * 2, y);
                    }
                }
            }
            sheet.Save(outPath, ImageFormat.Png);
            Console.WriteLine("wrote " + outPath + "  (" + sheet.Width + "x" + sheet.Height + ")");
            Console.WriteLine("reference frame: " + reference.Width + "x" + reference.Height);
            Console.WriteLine("our frame      : " + ours.Width + "x" + ours.Height);
            sheet.Dispose();
        }
        return 0;
    }

    private static void DrawContained(Graphics g, Image img, Rectangle box)
    {
        // preserve aspect, centre inside the box
        float s = Math.Min((float)box.Width / img.Width, (float)box.Height / img.Height);
        int w = (int)(img.Width * s), h = (int)(img.Height * s);
        int x = box.X + (box.Width - w) / 2, y = box.Y + (box.Height - h) / 2;
        g.DrawImage(img, new Rectangle(x, y, w, h));
    }

    /// <summary>
    /// Decode the largest frame from an .ico by hand. Handles frames stored as
    /// PNG (which is how we store 256px) as well as classic DIB frames.
    /// </summary>
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

        // PNG signature?
        bool png = frame.Length > 8 && frame[0] == 0x89 && frame[1] == 0x50 && frame[2] == 0x4E && frame[3] == 0x47;
        if (png)
        {
            using (MemoryStream ms = new MemoryStream(frame))
            using (Image img = Image.FromStream(ms))
                return new Bitmap(img);
        }

        // DIB frame: the BITMAPINFOHEADER stores doubled height and the pixels
        // are 32bpp bottom-up with no alpha premultiplication.
        int headerSize = BitConverter.ToInt32(frame, 0);
        int wpx = BitConverter.ToInt32(frame, 4);
        int hpx = BitConverter.ToInt32(frame, 8) / 2;
        int bpp = BitConverter.ToUInt16(frame, 14);
        if (bpp != 32) return null;

        Bitmap bmp = new Bitmap(wpx, hpx, PixelFormat.Format32bppArgb);
        int src = headerSize;
        for (int y = 0; y < hpx; y++)
        {
            for (int x = 0; x < wpx; x++)
            {
                int p = src + ((hpx - 1 - y) * wpx + x) * 4;
                if (p + 3 >= frame.Length) break;
                bmp.SetPixel(x, y, Color.FromArgb(frame[p + 3], frame[p + 2], frame[p + 1], frame[p]));
            }
        }
        return bmp;
    }
}
