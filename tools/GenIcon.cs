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
            bool detailed = size >= 24;

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
            // Layout mirrors the Multisim icon: two long horizontal runs with
            // round brass terminals on the right, a square pad on the left, and
            // a vertical run joining them.
            //
            // The vertical run spans from the upper long run all the way down,
            // and the middle run sits ON the long run's y, so every trace
            // visibly meets another. An earlier version left the vertical run
            // floating between the two, which read as a broken bracket.
            // Thin strokes on purpose: heavy runs made the badge look clumsy at
            // every size. Fine lines also survive downscaling better.
            float stroke = Math.Max(1.0f, size * 0.036f);
            float dotR = Math.Max(1.2f, size * 0.044f);

            float xL = size * 0.195f;           // left edge of the runs
            float xR = size * 0.800f;           // right edge
            float xVert = size * 0.395f;        // vertical connector
            float yTop = size * 0.315f;         // upper long run
            float yMid = size * 0.500f;         // short dim run (feed)
            float yBot = size * 0.685f;         // lower long run

            using (Pen pen = new Pen(Trace, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                // upper long run -> brass terminal
                g.DrawLine(pen, xL, yTop, xR - dotR, yTop);
                // lower long run -> brass terminal
                g.DrawLine(pen, xVert, yBot, xR - dotR, yBot);
                // vertical run joining the two long runs
                g.DrawLine(pen, xVert, yTop, xVert, yBot);

                if (detailed)
                {
                    // short dim feed that tees into the vertical run
                    using (Pen dim = new Pen(TraceDim, Math.Max(1f, stroke * 0.70f)))
                    {
                        dim.StartCap = LineCap.Round;
                        dim.EndCap = LineCap.Round;
                        g.DrawLine(dim, xL, yMid, xVert, yMid);
                    }
                }
            }

            // ---- round terminals on the right --------------------------------
            DrawPad(g, xR - dotR * 0.55f, yTop, dotR);
            DrawPad(g, xR - dotR * 0.55f, yBot, dotR);

            // ---- square pad on the left --------------------------------------
            DrawSquarePad(g, xL, yTop, stroke * 1.85f);
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
