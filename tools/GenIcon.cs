// ============================================================================
//  GenIcon.cs - generates assets/app.ico, the application icon.
//
//  Why a separate generator instead of a checked-in .ico:
//    * the icon can be re-tuned (colour, stroke weight) by editing this file
//    * several sizes are rendered independently rather than downscaled from
//      one bitmap, so 16x16 stays crisp instead of turning to mush
//    * no binary asset is edited by hand, so the design is reviewable in a diff
//
//  The .ico writer is hand-rolled: BMP (DIB) frames stored bottom-up with a
//  1-bit AND mask, which is the format Windows expects for small icon sizes.
//
//  Design: a rounded-square badge, a circuit node in the middle and three
//  traces running to vias on the edges - readable at 16px, still interesting
//  at 256px.
//
//  Usage:  GenIcon.exe <output.ico>
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class GenIcon
{
    // Palette: deep navy badge, cool white/cyan traces, muted brass terminals -
    // the same construction as the NI Multisim icon, but restrained rather than
    // purple, so it reads as calm and technical.
    private static readonly Color BadgeTop = Color.FromArgb(255, 30, 51, 84);
    private static readonly Color BadgeBottom = Color.FromArgb(255, 14, 26, 47);
    private static readonly Color Trace = Color.FromArgb(255, 232, 242, 252);
    private static readonly Color TraceDim = Color.FromArgb(255, 150, 190, 226);
    private static readonly Color Terminal = Color.FromArgb(255, 214, 166, 82);
    private static readonly Color TerminalDeep = Color.FromArgb(255, 168, 122, 52);

    // Sizes Windows actually asks for: the small shell sizes, the 48px default,
    // the 64px extra-large shell icon and Explorer's 256px jumbo view. The
    // deliberately odd sizes Inno Setup used to offer (20, 40) are dropped -
    // each frame costs tens of KB and none of them is requested by the shell.
    private static readonly int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "app.ico";

        List<byte[]> frames = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using (Bitmap bmp = Render(size))
                frames.Add(size >= 256 ? ToPngFrame(bmp) : ToDib(bmp));
        }

        WriteIco(outPath, Sizes, frames);
        Console.WriteLine("wrote " + outPath + "  frames: " + string.Join(",", Array.ConvertAll(Sizes, delegate(int s) { return s.ToString(); })));
        return 0;
    }

    // ------------------------------------------------------------------------
    // drawing
    // ------------------------------------------------------------------------
    private static Bitmap Render(int size)
    {
        Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Below roughly 24px the fine runs merge into noise, so small frames
            // use fewer, thicker runs instead of a scaled-down copy.
            bool detailed = size >= 32;

            float pad = size * 0.055f;
            RectangleF badge = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
            float radius = size * 0.185f;

            // ---- badge -----------------------------------------------------
            using (GraphicsPath path = Rounded(badge, radius))
            using (LinearGradientBrush b = new LinearGradientBrush(
                badge, BadgeTop, BadgeBottom, 100f))
            {
                g.FillPath(b, path);
            }
            // inner highlight: a soft top-edge sheen, the thing that makes a flat
            // badge look like a physical tile
            if (detailed)
            {
                using (GraphicsPath path = Rounded(badge, radius))
                using (Pen p = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, size * 0.010f)))
                {
                    g.DrawPath(p, path);
                }
            }
            using (GraphicsPath path = Rounded(badge, radius))
            using (Pen p = new Pen(Color.FromArgb(70, 90, 130, 180), Math.Max(1f, size * 0.008f)))
            {
                g.DrawPath(p, path);
            }

            // ---- circuit runs ------------------------------------------------
            // A five-line bundle fed from a vertical trunk on the left: every run
            // starts at the trunk and extends right by a different amount, with
            // brass terminals on the three longest. The trunk is the spine, so
            // the whole thing reads as one connected circuit.
            //
            // Line count is a size trade-off. Below roughly 32px five lines plus
            // a trunk collapse into a grey smear, so small frames draw a reduced
            // set - fewer, longer runs read far better than a shrunken copy.
            float stroke = Math.Max(0.9f, size * 0.029f);
            float dotR = Math.Max(1.1f, size * 0.040f);
            float busW = Math.Max(0.9f, stroke * 1.45f);

            float xBus = size * 0.335f;         // the trunk, left of centre
            float xEnd = size * 0.795f;         // where the terminals sit
            // Per-run right ends, varied on purpose so the bundle tapers instead
            // of forming a solid block.
            float e0 = xEnd;
            float e1 = size * 0.620f;
            float e2 = size * 0.735f;
            float e3 = size * 0.575f;
            float e4 = xEnd;

            // five evenly spaced rows
            float y0 = size * 0.280f;
            float yStep = size * 0.110f;
            float y1 = y0 + yStep;
            float y2 = y0 + yStep * 2f;
            float y3 = y0 + yStep * 3f;
            float y4 = y0 + yStep * 4f;

            using (Pen pen = new Pen(Trace, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                if (detailed)
                {
                    g.DrawLine(pen, xBus, y0, e0 - dotR * 0.55f, y0);
                    g.DrawLine(pen, xBus, y1, e1, y1);
                    g.DrawLine(pen, xBus, y2, e2 - dotR * 0.55f, y2);
                    g.DrawLine(pen, xBus, y3, e3, y3);
                    g.DrawLine(pen, xBus, y4, e4 - dotR * 0.55f, y4);
                }
                else
                {
                    // Reduced set for the small frames: three runs and the trunk.
                    g.DrawLine(pen, xBus, y0, e0 - dotR * 0.55f, y0);
                    g.DrawLine(pen, xBus, y2, e2 - dotR * 0.55f, y2);
                    g.DrawLine(pen, xBus, y4, e4 - dotR * 0.55f, y4);
                }

                // the trunk, drawn last so it sits cleanly over the run origins
                using (Pen bp = new Pen(Trace, busW))
                {
                    bp.StartCap = LineCap.Round;
                    bp.EndCap = LineCap.Round;
                    g.DrawLine(bp, xBus, y0, xBus, y4);
                }
            }

            // ---- round terminals on the three longest runs ---------------------
            DrawPad(g, e0 - dotR * 0.55f, y0, dotR);
            DrawPad(g, e4 - dotR * 0.55f, y4, dotR);
            if (detailed) DrawPad(g, e2 - dotR * 0.55f, y2, dotR);

            // ---- square pad at the trunk's head --------------------------------
            DrawSquarePad(g, xBus, y0, stroke * 1.7f);
        }
        return bmp;
    }

    /// <summary>Round terminal: brass ring with a darker centre.</summary>
    private static void DrawPad(Graphics g, float x, float y, float r)
    {
        using (SolidBrush b = new SolidBrush(Terminal))
            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        using (SolidBrush b = new SolidBrush(TerminalDeep))
            g.FillEllipse(b, x - r * 0.48f, y - r * 0.48f, r * 0.96f, r * 0.96f);
    }

    /// <summary>Square pad, the small solid block seen at a run's origin.</summary>
    private static void DrawSquarePad(Graphics g, float x, float y, float s)
    {
        using (SolidBrush b = new SolidBrush(Trace))
            g.FillRectangle(b, x - s / 2f, y - s / 2f, s, s);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2f;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ------------------------------------------------------------------------
    // .ico writing
    // ------------------------------------------------------------------------
    /// <summary>
    /// Convert to the DIB layout an icon frame uses: a BITMAPINFOHEADER whose
    /// height is doubled (colour data plus the 1-bit AND mask), 32bpp BGRA
    /// rows stored bottom-up, then the mask rows padded to 4 bytes.
    /// </summary>
    private static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;          // 1bpp, 4-byte aligned
        int colorBytes = w * h * 4;
        int maskBytes = maskStride * h;

        byte[] outBuf = new byte[40 + colorBytes + maskBytes];
        int o = 0;

        // BITMAPINFOHEADER
        WriteInt(outBuf, ref o, 40);
        WriteInt(outBuf, ref o, w);
        WriteInt(outBuf, ref o, h * 2);                // doubled height: XOR + AND
        WriteShort(outBuf, ref o, 1);                  // planes
        WriteShort(outBuf, ref o, 32);                 // bits per pixel
        WriteInt(outBuf, ref o, 0);                    // BI_RGB
        WriteInt(outBuf, ref o, colorBytes + maskBytes);
        WriteInt(outBuf, ref o, 0);
        WriteInt(outBuf, ref o, 0);
        WriteInt(outBuf, ref o, 0);
        WriteInt(outBuf, ref o, 0);

        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[w * 4];
            for (int y = h - 1; y >= 0; y--)          // bottom-up
            {
                IntPtr src = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(src, row, 0, row.Length);
                Array.Copy(row, 0, outBuf, o, row.Length);
                o += row.Length;
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        // AND mask: 0 means "use the colour data", which is what we want since
        // the alpha channel already carries the shape.
        o += maskBytes;
        return outBuf;
    }


    /// <summary>
    /// Encode a frame as PNG. The ICO format allows PNG-compressed frames, and
    /// Explorer has supported them since Vista. A 256x256 BMP frame costs about
    /// 270 KB while the same image as PNG is roughly 20 KB, so the large frames
    /// use this and only the small ones stay as DIB.
    /// </summary>
    private static byte[] ToPngFrame(Bitmap bmp)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
    private static void WriteIco(string path, int[] sizes, List<byte[]> frames)
    {
        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);            // reserved
            bw.Write((ushort)1);            // type: icon
            bw.Write((ushort)frames.Count);

            int offset = 6 + frames.Count * 16;
            for (int i = 0; i < frames.Count; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));   // 0 means 256
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);                    // palette colours
                bw.Write((byte)0);                    // reserved
                bw.Write((ushort)1);                  // planes
                bw.Write((ushort)32);                 // bits per pixel
                bw.Write(frames[i].Length);
                bw.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] f in frames) bw.Write(f);
        }
    }

    private static void WriteInt(byte[] b, ref int o, int v)
    {
        b[o++] = (byte)(v & 0xFF);
        b[o++] = (byte)((v >> 8) & 0xFF);
        b[o++] = (byte)((v >> 16) & 0xFF);
        b[o++] = (byte)((v >> 24) & 0xFF);
    }

    private static void WriteShort(byte[] b, ref int o, short v)
    {
        b[o++] = (byte)(v & 0xFF);
        b[o++] = (byte)((v >> 8) & 0xFF);
    }
}
