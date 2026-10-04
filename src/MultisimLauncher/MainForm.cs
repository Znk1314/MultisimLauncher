// ============================================================================
//  MainForm - the GUI. Dark, flat, hand-painted so it does not look like a
//  stock WinForms dialog. All launching happens on a worker thread; the UI
//  thread only ever updates text and colours.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MultisimLauncher
{
    internal sealed class MainForm : Form
    {
        // ---- palette -------------------------------------------------------
        private static readonly Color BgTop = Color.FromArgb(24, 27, 33);
        private static readonly Color BgBottom = Color.FromArgb(17, 19, 24);
        private static readonly Color Card = Color.FromArgb(33, 37, 45);
        private static readonly Color CardEdge = Color.FromArgb(52, 58, 70);
        private static readonly Color TextMain = Color.FromArgb(233, 237, 243);
        private static readonly Color TextDim = Color.FromArgb(138, 147, 163);
        private static readonly Color Accent = Color.FromArgb(58, 150, 255);     // idle / start
        private static readonly Color AccentHover = Color.FromArgb(84, 168, 255);
        private static readonly Color Working = Color.FromArgb(245, 166, 35);    // busy
        private static readonly Color Danger = Color.FromArgb(226, 78, 78);      // stop
        private static readonly Color DangerHover = Color.FromArgb(240, 100, 100);
        private static readonly Color Good = Color.FromArgb(63, 196, 128);

        // ---- controls ------------------------------------------------------
        private readonly FlatButton _button;
        private readonly Label _status;
        private readonly Label _substatus;
        private readonly Label _detail;
        private readonly Label _attemptLabel;
        private readonly Panel _card;
        private readonly CheckBox _verbose;
        private readonly Label _pathLabel;

        // ---- state ---------------------------------------------------------
        private MultisimInstall _install;
        private volatile bool _cancelled;
        private volatile bool _busy;
        private Process _process;
        private int _attempt;
        private int _successCount;
        private float _scale = 1f;

        /// <summary>Scale a design-time pixel value for the current DPI.</summary>
        private int S(int designPixels)
        {
            return (int)Math.Round(designPixels * _scale, MidpointRounding.AwayFromZero);
        }

        private float SF(float designPixels)
        {
            return designPixels * _scale;
        }

        /// <summary>
        /// Build a font sized in PIXELS, already scaled for this display.
        ///
        /// Everything in this form uses pixel units on purpose. A point-sized
        /// font is resolved against the real DPI (on a 200% display 1pt is about
        /// 2.67px), so combining point sizes with the manual scaling above would
        /// scale text twice and overflow the window - which is exactly what the
        /// first two builds did.
        /// </summary>
        private Font F(string family, float designPixels, FontStyle style)
        {
            try
            {
                return new Font(family, SF(designPixels), style, GraphicsUnit.Pixel);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, SF(designPixels), style, GraphicsUnit.Pixel);
            }
        }

        private Font F(string family, float designPixels)
        {
            return F(family, designPixels, FontStyle.Regular);
        }

        private readonly List<string> _log = new List<string>();
        private readonly object _logLock = new object();

        // Tunables. The retry loop is the whole point of this program: on the
        // reference machine ~25-50% of cold starts come up with a broken
        // component library, so a handful of attempts gets to a usable state.
        private const int SettleMs = 2500;        // must survive this long to count as good
        private const int StartTimeoutMs = 45000; // give up on one attempt after this
        private const int BetweenAttemptsMs = 1200;
        private const int MaxAttempts = 40;       // safety net; 0 would mean forever

        public MainForm(string[] args)
        {
            bool verbose = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)) verbose = true;
            }

            // With a working DPI declaration the app renders at real pixels, so
            // the layout must be scaled by hand. Doing it here (instead of via
            // AutoScaleMode) keeps every position predictable and avoids double
            // scaling, which is what made the first build overflow on a 200%
            // display.
            const int BaseW = 560, BaseH = 420;
            _scale = 1f;
            try
            {
                using (Graphics g = CreateGraphics())
                {
                    _scale = g.DpiX / 96f;
                }
            }
            catch { }
            if (_scale < 1f) _scale = 1f;
            if (_scale > 4f) _scale = 4f;

            Text = Program.AppName + " " + Program.AppVersion;
            ClientSize = new Size(S(BaseW), S(BaseH));
            MinimumSize = new Size(S(BaseW), S(BaseH));
            MaximumSize = new Size(S(BaseW), S(BaseH));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgTop;
            Font = F("Segoe UI", 12f);
            DoubleBuffered = true;
            Icon = AppIcon.Create();

            // ---------- card ----------
            int cardW = S(504);
            _card = new Panel();
            _card.Location = new Point(S(28), S(84));
            _card.Size = new Size(cardW, S(196));
            _card.BackColor = Card;
            _card.Paint += CardPaint;
            Controls.Add(_card);

            // ---------- the one button ----------
            _button = new FlatButton();
            _button.Size = new Size(S(200), S(52));
            _button.Location = new Point((cardW - S(200)) / 2, S(36));
            _button.Click += ButtonClick;
            _card.Controls.Add(_button);

            // ---------- status ----------
            _status = new Label();
            _status.AutoSize = false;
            _status.Size = new Size(cardW - S(40), S(26));
            _status.Location = new Point(S(20), S(104));
            _status.TextAlign = ContentAlignment.MiddleCenter;
            _status.ForeColor = TextMain;
            _status.Font = F("Segoe UI", 15f);
            _status.BackColor = Color.Transparent;
            _status.Text = "Ready";
            _card.Controls.Add(_status);

            _substatus = new Label();
            _substatus.AutoSize = false;
            _substatus.Size = new Size(cardW - S(40), S(20));
            _substatus.Location = new Point(S(20), S(130));
            _substatus.TextAlign = ContentAlignment.MiddleCenter;
            _substatus.ForeColor = TextDim;
            _substatus.Font = F("Segoe UI", 11.5f);
            _substatus.BackColor = Color.Transparent;
            _substatus.Text = "";
            _card.Controls.Add(_substatus);

            _attemptLabel = new Label();
            _attemptLabel.AutoSize = false;
            _attemptLabel.Size = new Size(cardW - S(40), S(18));
            _attemptLabel.Location = new Point(S(20), S(158));
            _attemptLabel.TextAlign = ContentAlignment.MiddleCenter;
            _attemptLabel.ForeColor = TextDim;
            _attemptLabel.Font = F("Consolas", 11f);
            _attemptLabel.BackColor = Color.Transparent;
            _attemptLabel.Text = "";
            _card.Controls.Add(_attemptLabel);

            // ---------- detected path ----------
            _pathLabel = new Label();
            _pathLabel.AutoSize = false;
            _pathLabel.Size = new Size(S(504), S(76));
            _pathLabel.Location = new Point(S(28), S(292));
            _pathLabel.ForeColor = TextDim;
            _pathLabel.Font = F("Consolas", 9.5f);
            _pathLabel.TextAlign = ContentAlignment.TopLeft;
            Controls.Add(_pathLabel);

            // ---------- verbose toggle ----------
            _verbose = new CheckBox();
            _verbose.Text = "Show details";
            _verbose.ForeColor = TextDim;
            _verbose.FlatStyle = FlatStyle.Flat;
            _verbose.BackColor = BgTop;              // match the form, not the default white box
            _verbose.UseVisualStyleBackColor = false;
            _verbose.Font = F("Segoe UI", 11f);
            _verbose.AutoSize = true;
            _verbose.Location = new Point(S(28), S(374));
            _verbose.Checked = verbose;
            _verbose.CheckedChanged += delegate { UpdatePathLabel(); };
            Controls.Add(_verbose);

            _detail = new Label();
            _detail.AutoSize = false;
            _detail.Size = new Size(S(230), S(18));
            _detail.Location = new Point(S(302), S(376));
            _detail.TextAlign = ContentAlignment.MiddleRight;
            _detail.ForeColor = TextDim;
            _detail.Font = F("Consolas", 9.5f);
            _detail.Text = "";
            Controls.Add(_detail);

            Load += OnLoad;
            FormClosing += OnFormClosing;
        }

        // --------------------------------------------------------------------
        // startup: find Multisim, or tell the user plainly
        // --------------------------------------------------------------------
        private void OnLoad(object sender, EventArgs e)
        {
            _install = MultisimLocator.Find();
            if (_install == null || !_install.IsUsable)
            {
                SetStatus("Multisim not found", Danger);
                SetSub("Install NI Multisim first, then restart this launcher.");
                _button.SetState("Unavailable", Accent, false);
                _button.Enabled = false;
                UpdatePathLabel();
                return;
            }
            _button.SetState("Start", Accent, true);
            SetStatus("Ready", TextMain);
            SetSub("Multisim " + _install.Version + " detected");
            UpdatePathLabel();
            Log("found: " + _install.ExePath + " (" + _install.Source + ")");
        }

        private void UpdatePathLabel()
        {
            if (_install == null)
            {
                _pathLabel.Text = "Multisim: not found";
                _detail.Text = "";
                return;
            }
            string s = _install.ExePath;
            if (_verbose.Checked)
            {
                s += Environment.NewLine + "database: " + (_install.DatabaseDir ?? "?");
                s += Environment.NewLine + "user db : " + (_install.UserDatabaseDir ?? "?");
                s += Environment.NewLine + "source  : " + _install.Source;
            }
            _pathLabel.Text = s;
            _detail.Text = "attempts " + _attempt + "   ok " + _successCount;
        }

        // --------------------------------------------------------------------
        // the single button: Start  <->  Stop
        // --------------------------------------------------------------------
        private void ButtonClick(object sender, EventArgs e)
        {
            if (_busy) { _cancelled = true; SetStatus("Stopping...", Working); return; }
            if (_install == null || !_install.IsUsable) return;

            _cancelled = false;
            _busy = true;
            _attempt = 0;
            _button.SetState("Stop", Danger, true);
            SetStatus("Starting...", Working);
            SetSub("");
            Log("--- session start ---");

            Thread t = new Thread(new ThreadStart(Worker));
            t.IsBackground = true;
            t.Start();
        }

        // --------------------------------------------------------------------
        // worker: keep trying until an instance comes up healthy
        // --------------------------------------------------------------------
        private void Worker()
        {
            for (int i = 0; i < MaxAttempts; i++)
            {
                if (_cancelled) break;
                _attempt = i + 1;
                Ui(delegate
                {
                    SetStatus("Starting...", Working);
                    SetSub("attempt " + _attempt);
                    UpdatePathLabel();
                });

                bool ok = TryOneLaunch();
                if (_cancelled) break;

                if (ok)
                {
                    _successCount++;
                    int shown = _attempt;
                    Ui(delegate
                    {
                        SetStatus("Running", Good);
                        SetSub("ready after " + shown + (shown == 1 ? " attempt" : " attempts"));
                        _button.SetState("Stop", Danger, true);
                        UpdatePathLabel();
                    });
                    Log("successful launch on attempt " + _attempt);
                    WatchUntilExit();
                    // Multisim was closed (by the user or by us) - reset to idle.
                    _busy = false;
                    _attempt = 0;
                    Ui(delegate
                    {
                        _button.SetState("Start", Accent, true);
                        SetStatus("Ready", TextMain);
                        SetSub("");
                        UpdatePathLabel();
                    });
                    Log("--- session end ---");
                    return;
                }

                Log("attempt " + _attempt + " came up broken, discarding");
                if (i < MaxAttempts - 1) Sleep(BetweenAttemptsMs);
            }

            bool cancelled = _cancelled;
            _busy = false;
            _attempt = 0;
            Ui(delegate
            {
                _button.SetState("Start", Accent, true);
                if (cancelled) { SetStatus("Ready", TextMain); SetSub(""); }
                else { SetStatus("Gave up after " + MaxAttempts + " attempts", Danger); SetSub("Try again, or restart Windows."); }
                UpdatePathLabel();
            });
            Log("--- session end (no success) ---");
        }

        /// <summary>
        /// One cold start. Returns true only when the instance is genuinely
        /// usable. Anything else is cleaned up silently.
        /// </summary>
        private bool TryOneLaunch()
        {
            // clean slate: no leftover process, no leftover lock files
            KillResident();
            SessionHealth.RemoveLockFiles(_install);

            Process p;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(_install.ExePath);
                psi.WorkingDirectory = _install.InstallDir;
                psi.UseShellExecute = true;      // run exactly as a desktop double-click would
                p = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log("start failed: " + ex.Message);
                return false;
            }
            if (p == null) return false;
            _process = p;

            Stopwatch sw = Stopwatch.StartNew();
            bool healthy = false;
            bool sawDialog = false;

            while (sw.ElapsedMilliseconds < StartTimeoutMs)
            {
                if (_cancelled) break;
                Sleep(400);
                try { if (p.HasExited) break; } catch { break; }

                if (SessionHealth.HasDatabaseErrorDialog((uint)p.Id)) { sawDialog = true; break; }
                if (SessionHealth.IsHealthy((uint)p.Id, _install)) { healthy = true; break; }
            }

            if (!healthy) { Discard(p); return false; }

            // Databases are open - now make sure it stays that way, and that no
            // error box appeared late. This is the window in which the broken
            // instances used to slip through in earlier attempts.
            Stopwatch settle = Stopwatch.StartNew();
            while (settle.ElapsedMilliseconds < SettleMs)
            {
                if (_cancelled) break;
                Sleep(300);
                try { if (p.HasExited) { Discard(p); return false; } } catch { Discard(p); return false; }
                if (SessionHealth.HasDatabaseErrorDialog((uint)p.Id)) { Discard(p); return false; }
                if (SessionHealth.CountLockFiles(_install) < SessionHealth.RequiredLocks) { Discard(p); return false; }
            }

            if (_cancelled) { Discard(p); return false; }
            if (sawDialog) { Discard(p); return false; }
            return true;
        }

        /// <summary>Close a launch attempt that did not make it, without noise.</summary>
        private void Discard(Process p)
        {
            try
            {
                if (p != null && !p.HasExited)
                {
                    Win32.PostCloseToAll((uint)p.Id);
                    // The broken instance sits on a modal dialog, so WM_CLOSE often
                    // does not take. Give it a moment, then force it down: leaving
                    // it alive would leave stale .ldb files behind.
                    if (!p.WaitForExit(2500))
                    {
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(3000); } catch { }
                    }
                }
            }
            catch { }
            SessionHealth.RemoveLockFiles(_install);
        }

        private void KillResident()
        {
            Process[] all;
            try { all = Process.GetProcessesByName("multisim"); }
            catch { return; }
            foreach (Process p in all)
            {
                try
                {
                    if (p.HasExited) continue;
                    Win32.PostCloseToAll((uint)p.Id);
                }
                catch { }
            }
            foreach (Process p in all)
            {
                try
                {
                    if (!p.HasExited && !p.WaitForExit(1500))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
                catch { }
                try { p.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// After a good launch, sit quietly until Multisim closes. The Stop
        /// button sets _cancelled, which is also how the user closes it.
        /// </summary>
        private void WatchUntilExit()
        {
            while (!_cancelled)
            {
                Sleep(500);
                Process[] all;
                try { all = Process.GetProcessesByName("multisim"); }
                catch { return; }
                if (all.Length == 0) return;
            }
            KillResident();
            SessionHealth.RemoveLockFiles(_install);
            _cancelled = false;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _cancelled = true;
            Thread.Sleep(150);
        }

        // --------------------------------------------------------------------
        // helpers
        // --------------------------------------------------------------------
        private void Log(string line)
        {
            lock (_logLock)
            {
                _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
                if (_log.Count > 300) _log.RemoveAt(0);
            }
            // Persist next to the executable so users can send it in with a bug report.
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MultisimLauncher");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "launcher.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        private void Sleep(int ms)
        {
            int left = ms;
            while (left > 0 && !_cancelled)
            {
                int step = left > 100 ? 100 : left;
                Thread.Sleep(step);
                left -= step;
            }
        }

        private void Ui(MethodInvoker action)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private void SetSub(string text) { _substatus.Text = text; }

        // --------------------------------------------------------------------
        // painting
        // --------------------------------------------------------------------
        private void CardPaint(object sender, PaintEventArgs e)
        {
            Smooth(e.Graphics);
            using (Pen pen = new Pen(CardEdge, 1f))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, _card.Width - 1, _card.Height - 1);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height), BgTop, BgBottom, 90f))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Smooth(e.Graphics);
            // title
            using (Font f = F("Segoe UI Semibold", 20f, FontStyle.Bold))
            using (Brush b = new SolidBrush(TextMain))
            {
                e.Graphics.DrawString("Multisim Launcher", f, b, SF(28f), SF(24f));
            }
            using (Font f = F("Segoe UI", 11.5f))
            using (Brush b = new SolidBrush(TextDim))
            {
                e.Graphics.DrawString("It keeps starting Multisim until the component library loads",
                    f, b, SF(30f), SF(56f));
            }
        }

        internal static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }
    }

    // ------------------------------------------------------------------------
    // FlatButton - rounded, flat, hover-aware. Hand painted so it does not look
    // like a default WinForms button.
    // ------------------------------------------------------------------------
    internal sealed class FlatButton : Control
    {
        private string _text = "Start";
        private Color _base = Color.FromArgb(58, 150, 255);
        private bool _hover;
        private bool _down;
        private bool _enabledVisual = true;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
            BackColor = Color.Transparent;
        }

        public void SetState(string text, Color baseColor, bool enabledVisual)
        {
            _text = text;
            _base = baseColor;
            _enabledVisual = enabledVisual;
            Cursor = enabledVisual ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            MainForm.Smooth(e.Graphics);
            Color fill = _base;
            if (!_enabledVisual) fill = Color.FromArgb(70, 76, 88);
            else if (_down) fill = Darken(_base, 0.80f);
            else if (_hover) fill = Lighten(_base, 0.12f);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Rounded(r, 10))
            using (SolidBrush b = new SolidBrush(fill))
            {
                e.Graphics.FillPath(b, path);
            }
            using (Font f = new Font("Segoe UI Semibold", Height * 0.30f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush tb = new SolidBrush(_enabledVisual ? Color.White : Color.FromArgb(150, 156, 168)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(_text, f, tb, new RectangleF(0, 0, Width, Height), sf);
            }
        }

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color Lighten(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)Math.Min(255, c.R + 255 * amount),
                (int)Math.Min(255, c.G + 255 * amount),
                (int)Math.Min(255, c.B + 255 * amount));
        }

        private static Color Darken(Color c, float factor)
        {
            return Color.FromArgb(c.A, (int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));
        }
    }

    // ------------------------------------------------------------------------
    // AppIcon - draw the window icon at runtime so the project needs no binary
    // asset that could be lost in a source checkout.
    // ------------------------------------------------------------------------
    internal static class AppIcon
    {
        public static Icon Create()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(64, 64))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    MainForm.Smooth(g);
                    g.Clear(Color.Transparent);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(58, 150, 255)))
                    using (GraphicsPath p = FlatButton.Rounded(new Rectangle(4, 4, 55, 55), 14))
                    {
                        g.FillPath(b, p);
                    }
                    using (Font f = new Font("Segoe UI Semibold", 40f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush tb = new SolidBrush(Color.White))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString("M", f, tb, new RectangleF(0, -2, 64, 64), sf);
                    }
                    IntPtr h = bmp.GetHicon();
                    return Icon.FromHandle(h);
                }
            }
            catch { return SystemIcons.Application; }
        }
    }
}
