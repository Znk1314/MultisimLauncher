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

            // ---- circuit runs ------------------------------------------------
            // Structure traced from the NI Multisim icon:
            //   * three white horizontal runs, right ends capped with square pads
            //   * two brass runs sitting just below the middle and bottom white
            //     runs, their right ends capped with round brass pads at the SAME
            //     x as the white pads, so the right edge lines up
            //   * a short brass link joining the two brass runs
            //   * three white stems along the bottom, each topped by a round pad
            //
            // Faithful to the original geometry, recoloured to the deeper navy
            // palette. Below about 32px the fine runs merge, so small frames drop
            // the stems - fewer, longer runs read far better than a shrunken copy.
            float run = Math.Max(0.9f, size * 0.030f);       // horizontal stroke
            float whiteR = Math.Max(1.0f, size * 0.036f);

            // vertical placement, scaled from the reference
            float y1 = size * 0.210f;        // white run 1
            float y2 = size * 0.370f;        // white run 2
            float y3 = size * 0.500f;        // white run 3
            float yG1 = y2 + size * 0.095f;  // brass run 1, below white run 2
            float yG2 = y3 + size * 0.095f;  // brass run 2, below white run 3

            float xLeft = size * 0.085f;
            float xRight = size * 0.880f;    // shared right edge for all pads
            float xPadMid = size * 0.700f;   // right end of white runs 2 and 3
            float xGLeft = size * 0.265f;    // left end of the brass runs
            float xLink = size * 0.300f;     // the brass link

            using (Pen pen = new Pen(Trace, run))
            {
                pen.StartCap = LineCap.Square;
                pen.EndCap = LineCap.Square;
                g.DrawLine(pen, xLeft, y1, xRight, y1);
                g.DrawLine(pen, xLeft, y2, xPadMid, y2);
                g.DrawLine(pen, xLeft, y3, xPadMid, y3);
            }

            // white square pads capping the white runs
            DrawSquarePad(g, xRight, y1, run * 2.4f);
            DrawSquarePad(g, xPadMid, y2, run * 2.2f);
            DrawSquarePad(g, xPadMid, y3, run * 2.2f);

            // brass runs, drawn after the white ones so they sit on top
            using (Pen pen = new Pen(Terminal, run * 1.2f))
            {
                pen.StartCap = LineCap.Square;
                pen.EndCap = LineCap.Square;
                g.DrawLine(pen, xGLeft, yG1, xRight, yG1);
                g.DrawLine(pen, xGLeft, yG2, xRight, yG2);
                // the short link joining them
                g.DrawLine(pen, xLink, yG1, xLink, yG2);
            }

            // round brass pads, right-aligned with the white pads
            DrawBrassPad(g, xRight, yG1, whiteR);
            DrawBrassPad(g, xRight, yG2, whiteR);

            if (detailed)
            {
                // ---- three white stems along the bottom ------------------------
                float[] stemX = new float[] { size * 0.470f, size * 0.620f, size * 0.770f };
                float[] stemPadY = new float[] { size * 0.615f, size * 0.640f, size * 0.605f };
                float[] stemR = new float[] { size * 0.040f, size * 0.036f, size * 0.040f };
                float stemBottom = size * 0.900f;

                for (int i = 0; i < 3; i++)
                {
                    using (Pen sp = new Pen(Trace, run))
                    {
                        sp.StartCap = LineCap.Square;
                        sp.EndCap = LineCap.Square;
                        g.DrawLine(sp, stemX[i], stemPadY[i], stemX[i], stemBottom);
                    }
                    using (SolidBrush b = new SolidBrush(Trace))
                    {
                        float r = stemR[i];
                        g.FillEllipse(b, stemX[i] - r, stemPadY[i] - r, r * 2, r * 2);
                    }
                }
            }
        }
        return bmp;
    }

    /// <summary>Round brass pad: a brass disc with a darker centre.</summary>
    private static void DrawBrassPad(Graphics g, float x, float y, float r)
    {
        using (SolidBrush b = new SolidBrush(Terminal))
            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        using (SolidBrush b = new SolidBrush(TerminalDeep))
            g.FillEllipse(b, x - r * 0.46f, y - r * 0.46f, r * 0.92f, r * 0.92f);
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
