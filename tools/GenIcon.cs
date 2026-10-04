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
            // Rounded tile with a diagonal gradient. The Multisim icon also has a
            // glossy sheen across the top; here it is a very soft radial wash
            // rather than a linear band, because a linear sheen produced a
            // visible horizontal seam across the middle of the tile.
            using (GraphicsPath path = Rounded(badge, radius))
            using (LinearGradientBrush b = new LinearGradientBrush(
                badge, BadgeTop, BadgeBottom, 115f))
            {
                g.FillPath(b, path);
            }
            using (GraphicsPath path = Rounded(badge, radius))
            {
                g.SetClip(path);
                float gw = badge.Width * 2.1f;
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(badge.X + badge.Width * 0.5f - gw / 2f,
                                  badge.Y - badge.Height * 0.72f, gw, gw * 0.92f);
                    using (PathGradientBrush pg = new PathGradientBrush(gp))
                    {
                        pg.CenterColor = Color.FromArgb(64, 255, 255, 255);
                        pg.SurroundColors = new Color[] { Color.FromArgb(0, 255, 255, 255) };
                        g.FillPath(pg, gp);
                    }
                }
                g.ResetClip();
            }
            using (GraphicsPath path = Rounded(badge, radius))
            using (Pen p = new Pen(Color.FromArgb(96, 214, 226, 255), Math.Max(1f, size * 0.008f)))
            {
                g.DrawPath(p, path);
            }

            // Everything below is clipped to the badge. This is not cosmetic: the
            // mark runs to the tile edge in the reference, where the rounded
            // corners cut it off. Without the clip, runs and pads spill into the
            // transparent corners and show up outside the icon in Explorer.
            //
            // An earlier attempt set this clip and was then reverted because the
            // overlap score dropped. That was the wrong call - the score only
            // measures agreement with the reference inside the badge, and it was
            // traded against a visible defect.
            using (GraphicsPath badgeClip = Rounded(badge, radius))
            {
                g.SetClip(badgeClip, CombineMode.Replace);
            }

            // ---- circuit runs ------------------------------------------------
            // Geometry measured from the 256x256 frame inside multisim.exe; the
            // numbers passed to U() are that frame's pixel coordinates.
            //
            // What the reference contains:
            //   * four white horizontal runs at y = 13.5, 46, 84.5, 123, all
            //     starting at the left edge of the badge
            //   * a white vertical segment at x = 26 joining runs 3 and 4, which
            //     forms the bracket shape the icon is known for
            //   * round white pads closing runs 2, 3 and 4
            //   * brass runs: a short one at y = 68 on the right, a full width
            //     one at y = 161, and two short ones at y = 197 and 204 on the
            //     left. Each ends in a brass round pad.
            //   * three white stems at x = 30, 134 and 203 rising from the bottom,
            //     each topped with a round white pad
            //
            // Below about 32px the detail merges, so small frames drop the stems
            // and the brass link.
            float run = Math.Max(0.9f, size * 0.032f);       // white trace stroke
            float brassRun = Math.Max(1.2f, size * 0.046f);  // brass traces are heavier
            float padR = Math.Max(1.0f, size * 0.042f);      // round pad radius

            // ---- white runs ---------------------------------------------------
            float wY1 = U(13.5f, size), wY2 = U(46f, size), wY3 = U(84.5f, size), wY4 = U(123f, size);
            float wXLeft = U(0f, size);          // reaches the tile edge; the clip trims it
            float wX1 = U(232f, size);     // trimmed by the clip
            float wX2 = U(161f, size);     // runs 2 and 4
            float wX3 = U(139f, size);     // run 3 is the shortest
            float bracketX = U(26f, size); // vertical join between runs 3 and 4

            using (Pen pen = new Pen(Trace, run))
            {
                pen.StartCap = LineCap.Square;
                pen.EndCap = LineCap.Square;
                // Run 1 carries no end pad: in the reference it simply stops.
                g.DrawLine(pen, wXLeft, wY1, wX1, wY1);
                g.DrawLine(pen, wXLeft, wY2, wX2 - padR, wY2);
                g.DrawLine(pen, wXLeft, wY3, wX3 - padR, wY3);
                g.DrawLine(pen, wXLeft, wY4, wX2 - padR, wY4);
                // the bracket - this is what makes the mark recognisable
                g.DrawLine(pen, bracketX, wY3, bracketX, wY4);
            }

            // Round white pads: runs 2, 3 and 4 at their right ends, plus the
            // entry pad that sits on run 1 near the middle of the badge.
            DrawDisc(g, wX2 - padR, wY2, padR, Trace, null);
            DrawDisc(g, wX3 - padR, wY3, padR, Trace, null);
            DrawDisc(g, wX2 - padR, wY4, padR, Trace, null);
            DrawDisc(g, U(140f, size), U(24f, size), padR * 1.4f, Trace, null);

            // ---- brass runs ---------------------------------------------------
            // Measured spans: a short one on the right, a full width one, and a
            // lower pair on the left. Round caps so the ends tuck into their pads
            // instead of sticking out as square corners.
            float bXRight = U(238f, size);   // pad sits near the edge, trimmed by the clip
            float bPadR = padR * 0.82f;

            float bYTop = U(68f, size);       // short run, right side
            float bYFull = U(161f, size);     // full width run
            float bYLow1 = U(197f, size);     // lower left pair
            float bYLow2 = U(204f, size);
            float bXTopLeft = U(172f, size);
            float bXFullLeft = U(0f, size);     // reaches the tile edge; the clip trims it
            float bXLowLeft = U(0f, size);
            float bXLowRight = U(75f, size);

            using (Pen pen = new Pen(Terminal, brassRun))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, bXTopLeft, bYTop, bXRight - padR, bYTop);
                g.DrawLine(pen, bXFullLeft, bYFull, bXRight - padR, bYFull);
                g.DrawLine(pen, bXLowLeft, bYLow1, bXLowRight - bPadR, bYLow1);
                g.DrawLine(pen, bXLowLeft, bYLow2, bXLowRight - bPadR, bYLow2);
            }

            // brass round pads: one at each right end
            DrawDisc(g, bXRight - padR, bYTop, bPadR, Terminal, null);
            DrawDisc(g, bXRight - padR, bYFull, bPadR, Terminal, null);
            DrawDisc(g, bXLowRight - bPadR, bYLow1, bPadR, Terminal, null);
            DrawDisc(g, bXLowRight - bPadR, bYLow2, bPadR, Terminal, null);

            if (detailed)
            {
                // ---- three white stems along the bottom ------------------------
                float[] stemX = new float[] { U(30f, size), U(134f, size), U(203f, size) };
                float[] stemTop = new float[] { U(166f, size), U(150f, size), U(160f, size) };
                float stemBottom = U(260f, size);   // overshoots; the clip trims it flush

                for (int i = 0; i < 3; i++)
                {
                    using (Pen sp = new Pen(Trace, run))
                    {
                        sp.StartCap = LineCap.Square;
                        sp.EndCap = LineCap.Square;
                        g.DrawLine(sp, stemX[i], stemTop[i], stemX[i], stemBottom);
                    }
                    DrawDisc(g, stemX[i], stemTop[i], padR * 0.95f, Trace, null);
                }
            }
        }
        return bmp;
    }

    /// <summary>
    /// Map a coordinate from the 256px reference frame onto the current render
    /// size, relative to the badge edge. Writing the measured numbers directly
    /// keeps the copy aligned with the original at every size.
    /// </summary>
    private static float U(float referencePixel, int size)
    {
        // The reference coordinates are absolute pixels in a 256x256 frame, so
        // the mapping is a plain uniform scale. An earlier version offset and
        // rescaled them against an assumed badge size, which pushed every run
        // about 7px too low.
        return referencePixel * size / 256f;
    }

    /// <summary>Filled disc, optionally with a darker core.</summary>
    private static void DrawDisc(Graphics g, float x, float y, float r, Color fill, Color? core)
    {
        using (SolidBrush b = new SolidBrush(fill))
            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        if (core.HasValue)
        {
            using (SolidBrush b = new SolidBrush(core.Value))
                g.FillEllipse(b, x - r * 0.46f, y - r * 0.46f, r * 0.92f, r * 0.92f);
        }
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
