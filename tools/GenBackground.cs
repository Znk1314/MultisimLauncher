// ============================================================================
//  GenBackground.cs - generates assets/background.png, the circuit-board hero
//  image used behind the launcher UI.
//
//  Generated rather than downloaded on purpose:
//    * no third-party licence to worry about in an MIT project
//    * reproducible: re-running this file regenerates the identical asset
//    * it can be re-tuned (density, hue, glow) without hunting for artwork
//
//  Usage:  GenBackground.exe <output.png> [width] [height]
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

internal static class GenBackground
{
    // Deep navy-to-slate base, so white UI text stays readable on top.
    private static readonly Color Top = Color.FromArgb(255, 10, 22, 40);
    private static readonly Color Bottom = Color.FromArgb(255, 16, 36, 62);

    // Trace colours: a cyan and a teal, both fairly desaturated so the result
    // reads as "technical" rather than "neon".
    private static readonly Color TraceA = Color.FromArgb(120, 56, 170, 220);
    private static readonly Color TraceB = Color.FromArgb(95, 60, 200, 190);
    private static readonly Color ViaColor = Color.FromArgb(190, 110, 200, 235);

    private static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "background.png";
        int w = args.Length > 1 ? int.Parse(args[1]) : 1920;
        int h = args.Length > 2 ? int.Parse(args[2]) : 1200;

        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // ---- 1. base gradient -------------------------------------
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, w, h), Top, Bottom, 115f))
                {
                    g.FillRectangle(bg, 0, 0, w, h);
                }

                // ---- 2. soft radial highlights ----------------------------
                DrawGlow(g, w * 0.78f, h * 0.22f, w * 0.42f, Color.FromArgb(38, 70, 150, 235));
                DrawGlow(g, w * 0.18f, h * 0.80f, w * 0.38f, Color.FromArgb(30, 60, 210, 190));

                // ---- 3. circuit grid + traces ----------------------------
                // Deterministic seed keeps the asset identical between runs.
                Random rnd = new Random(20261004);
                int cell = Math.Max(48, w / 26);          // grid pitch
                int cols = w / cell + 2;
                int rows = h / cell + 2;

                // Manhattan-routed traces between grid nodes: the classic
                // orthogonal look of a PCB.
                int traces = (w * h) / 9000;
                for (int i = 0; i < traces; i++)
                {
                    int x0 = rnd.Next(cols) * cell - cell / 2;
                    int y0 = rnd.Next(rows) * cell - cell / 2;
                    int len = (1 + rnd.Next(4)) * cell;

                    bool horizontalFirst = rnd.Next(2) == 0;
                    int x1 = x0 + (rnd.Next(2) == 0 ? len : -len);
                    int y1 = y0 + (rnd.Next(2) == 0 ? len : -len);

                    int alpha = 40 + rnd.Next(90);
                    bool teal = rnd.Next(3) == 0;
                    Color baseCol = teal ? TraceB : TraceA;
                    Color col = Color.FromArgb(Math.Min(255, alpha), baseCol.R, baseCol.G, baseCol.B);

                    using (Pen p = new Pen(col, 1.4f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        if (horizontalFirst)
                            g.DrawLines(p, new Point[] {
                                new Point(x0, y0), new Point(x1, y0), new Point(x1, y1) });
                        else
                            g.DrawLines(p, new Point[] {
                                new Point(x0, y0), new Point(x0, y1), new Point(x1, y1) });
                    }

                    // a via at each corner
                    if (rnd.Next(3) == 0)
                        DrawVia(g, x1, y1, rnd.Next(2) == 0 ? 3f : 4.5f);
                    if (rnd.Next(4) == 0)
                        DrawVia(g, x0, y0, 3f);
                }

                // ---- 4. component pads --------------------------------
                int pads = (w * h) / 26000;
                for (int i = 0; i < pads; i++)
                {
                    float px = rnd.Next(w);
                    float py = rnd.Next(h);
                    float pw = 18 + rnd.Next(90);
                    float ph = 10 + rnd.Next(46);

                    using (SolidBrush b = new SolidBrush(Color.FromArgb(26 + rnd.Next(26), 120, 190, 235)))
                    {
                        g.FillRectangle(b, px, py, pw, ph);
                    }
                    using (Pen p = new Pen(Color.FromArgb(70, 150, 215, 245), 1f))
                    {
                        g.DrawRectangle(p, px, py, pw, ph);
                    }
                }

                // ---- 5. vignette so edges stay quiet --------------------
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(-w * 0.25f, -h * 0.25f, w * 1.5f, h * 1.5f);
                    using (PathGradientBrush vg = new PathGradientBrush(path))
                    {
                        vg.CenterColor = Color.FromArgb(0, 0, 0, 0);
                        vg.SurroundColors = new Color[] { Color.FromArgb(155, 4, 10, 20) };
                        g.FillRectangle(vg, 0, 0, w, h);
                    }
                }

                // ---- 6. a slightly darker band on the left --------------
                // The sidebar sits there, so the artwork should not fight the
                // navigation labels drawn on top of it.
                using (LinearGradientBrush sb = new LinearGradientBrush(
                    new Rectangle(0, 0, (int)(w * 0.30f), h),
                    Color.FromArgb(205, 8, 16, 30), Color.FromArgb(0, 8, 16, 30), 0f))
                {
                    g.FillRectangle(sb, 0, 0, w * 0.30f, h);
                }
            }

            bmp.Save(outPath, ImageFormat.Png);
        }

        Console.WriteLine("wrote " + outPath + "  " + w + "x" + h);
        return 0;
    }

    private static void DrawGlow(Graphics g, float cx, float cy, float radius, Color colour)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
            using (PathGradientBrush b = new PathGradientBrush(path))
            {
                b.CenterColor = colour;
                b.SurroundColors = new Color[] { Color.FromArgb(0, colour.R, colour.G, colour.B) };
                g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);
            }
        }
    }

    private static void DrawVia(Graphics g, float x, float y, float r)
    {
        using (SolidBrush b = new SolidBrush(Color.FromArgb(70, ViaColor.R, ViaColor.G, ViaColor.B)))
        {
            g.FillEllipse(b, x - r * 1.9f, y - r * 1.9f, r * 3.8f, r * 3.8f);
        }
        using (SolidBrush b = new SolidBrush(ViaColor))
        {
            g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
        }
        using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 12, 26, 44)))
        {
            g.FillEllipse(b, x - r * 0.45f, y - r * 0.45f, r * 0.9f, r * 0.9f);
        }
    }
}
