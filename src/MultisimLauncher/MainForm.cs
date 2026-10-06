// ============================================================================
//  MainForm.cs - the GUI.
//
//  Layout follows the familiar launcher shape: a narrow sidebar on the left for
//  status and settings, a large hero area on the right carrying the circuit
//  artwork, and one dominant action button at the bottom of the hero.
//
//  Two rules the whole file obeys:
//    * every font is sized in PIXELS and scaled once by S()/SF(); mixing point
//      sizes with manual scaling scales text twice and overflows the window
//    * nothing here assumes a DPI: the scale is measured at runtime
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MultisimLauncher
{
    internal sealed class MainForm : Form
    {
        // ---- palette -------------------------------------------------------
        private static readonly Color Ink = Color.FromArgb(238, 244, 252);
        private static readonly Color InkDim = Color.FromArgb(150, 168, 192);
        private static readonly Color InkFaint = Color.FromArgb(108, 126, 150);
        private static readonly Color Accent = Color.FromArgb(64, 156, 255);
        private static readonly Color AccentSoft = Color.FromArgb(96, 178, 255);
        private static readonly Color Busy = Color.FromArgb(246, 170, 58);
        private static readonly Color Danger = Color.FromArgb(232, 92, 92);
        private static readonly Color Good = Color.FromArgb(74, 205, 140);
        private static readonly Color SideFill = Color.FromArgb(255, 11, 19, 32);
        private static readonly Color SideEdge = Color.FromArgb(26, 120, 180, 255);

        // ---- design-time geometry (scaled at runtime) ----------------------
        private const int DesignW = 1100, DesignH = 700;
        private const int SideW = 236;
        private const int BtnW = 300, BtnH = 62;

        // ---- controls ------------------------------------------------------
        private readonly FlatButton _action;
        private readonly SidebarItem _navDetails;
        private readonly SidebarItem _navLog;
        private readonly SidebarItem _navQuit;
        private readonly SidebarItem _navHidden;
        private readonly Label _stateText;
        private readonly Label _stateSub;
        private readonly Label _attemptText;
        private readonly Label _dbMaster;
        private readonly Label _dbCorporate;
        private readonly Label _pathLine;

        // ---- state ---------------------------------------------------------
        private MultisimInstall _install;
        private volatile bool _cancelled;
        private volatile bool _busy;
        private Process _process;
        private int _attempt;
        private int _successCount;
        private float _scale = 1f;
        private Image _background;
        private bool _showDetails;

        // Start Multisim off screen and only reveal an instance that works.
        // On by default; without it the user watches every failed attempt's
        // splash and error box go past.
        private bool _hideWhileStarting = true;

        // The thread that keeps a starting instance hidden. Separate from the
        // health checks because those block on SendMessageTimeout and would
        // otherwise throttle the hiding to a few times a second.
        private Thread _hideThread;
        private volatile bool _hideStop;
        private volatile uint _hidePid;

        // Retry tuning. Failures cluster in time, so there is a gap between
        // attempts rather than hammering the button.
        private const int SettleMs = 2500;
        private const int StartTimeoutMs = 45000;
        private const int BetweenAttemptsMs = 1200;
        private const int MaxAttempts = 40;

        public MainForm(string[] args)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)) _showDetails = true;
            }

            _scale = MeasureScale();

            Text = Program.AppName + " " + Program.AppVersion;
            ClientSize = new Size(S(DesignW), S(DesignH));
            MinimumSize = new Size(S(DesignW), S(DesignH));
            MaximumSize = new Size(S(DesignW), S(DesignH));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 18, 32);
            DoubleBuffered = true;
            Icon = AppIcon.Create();
            _background = LoadBackground();

            // ================= sidebar =====================================
            Panel side = new Panel();
            side.Location = new Point(0, 0);
            side.Size = new Size(S(SideW), S(DesignH));
            side.BackColor = SideFill;
            side.Paint += SidebarPaint;
            Controls.Add(side);

            // brand block
            Label brand = new Label();
            brand.AutoSize = false;
            brand.Text = "Multisim Launcher";
            brand.Font = F("Segoe UI Semibold", 20f, FontStyle.Bold);
            brand.ForeColor = Ink;
            brand.BackColor = SideFill;
            brand.Location = new Point(S(24), S(26));
            brand.Size = new Size(S(SideW - 40), S(34));
            side.Controls.Add(brand);

            Label tagline = new Label();
            tagline.AutoSize = false;
            tagline.Text = Strings.Tagline;
            tagline.Font = F("Segoe UI", 11.5f);
            tagline.ForeColor = InkFaint;
            tagline.BackColor = SideFill;
            tagline.Location = new Point(S(25), S(64));
            // wide enough that the line never wraps into the section header below
            tagline.Size = new Size(S(SideW + 30), S(22));
            side.Controls.Add(tagline);

            // --- STATUS section ---
            side.Controls.Add(MakeSectionHeader(Strings.Status, S(122)));

            // Row pitch is generous on purpose: at 200% scaling a 12px label
            // occupies about 24 physical pixels, so 30px keeps CJK glyphs from
            // touching the row below.
            _attemptText = MakeSideLabel(side, "", S(152), InkDim);
            _dbMaster = MakeSideLabel(side, "", S(182), InkDim);
            _dbCorporate = MakeSideLabel(side, "", S(212), InkDim);

            // --- GENERAL section ---
            side.Controls.Add(MakeSectionHeader(Strings.General, S(266)));

            _navHidden = new SidebarItem(Strings.HiddenStart);
            _navHidden.Location = new Point(S(14), S(292));
            _navHidden.Size = new Size(S(SideW - 28), S(38));
            _navHidden.SetChecked(_hideWhileStarting);
            _navHidden.Click += delegate
            {
                _hideWhileStarting = !_hideWhileStarting;
                _navHidden.SetChecked(_hideWhileStarting);
                Log("hidden start " + (_hideWhileStarting ? "on" : "off"));
            };
            side.Controls.Add(_navHidden);

            _navDetails = new SidebarItem(Strings.ShowDetails);
            _navDetails.Location = new Point(S(14), S(334));
            _navDetails.Size = new Size(S(SideW - 28), S(38));
            _navDetails.SetChecked(_showDetails);
            _navDetails.Click += delegate
            {
                _showDetails = !_showDetails;
                _navDetails.SetChecked(_showDetails);
                RefreshDetailLabels();
            };
            side.Controls.Add(_navDetails);

            _navLog = new SidebarItem(Strings.OpenLog);
            _navLog.Location = new Point(S(14), S(376));
            _navLog.Size = new Size(S(SideW - 28), S(38));
            _navLog.Click += delegate { OpenLogFile(); };
            side.Controls.Add(_navLog);

            _navQuit = new SidebarItem(Strings.Quit);
            _navQuit.Location = new Point(S(14), S(418));
            _navQuit.Size = new Size(S(SideW - 28), S(38));
            _navQuit.Click += delegate { Close(); };
            side.Controls.Add(_navQuit);

            // ================= hero area ===================================
            // Everything on the right is drawn by OnPaint / child labels placed
            // over it, so the artwork can run edge to edge.
            _stateText = new Label();
            _stateText.AutoSize = false;
            _stateText.BackColor = Color.Transparent;
            _stateText.ForeColor = Ink;
            _stateText.Font = F("Segoe UI Semibold", 34f, FontStyle.Bold);
            _stateText.TextAlign = ContentAlignment.MiddleLeft;
            _stateText.Location = new Point(S(SideW + 44), S(DesignH - 300));
            _stateText.Size = new Size(S(DesignW - SideW - 88), S(50));
            _stateText.Text = Strings.Ready;
            Controls.Add(_stateText);

            _stateSub = new Label();
            _stateSub.AutoSize = false;
            _stateSub.BackColor = Color.Transparent;
            _stateSub.ForeColor = InkDim;
            _stateSub.Font = F("Segoe UI", 13f);
            _stateSub.TextAlign = ContentAlignment.MiddleLeft;
            _stateSub.Location = new Point(S(SideW + 46), S(DesignH - 248));
            _stateSub.Size = new Size(S(DesignW - SideW - 92), S(28));
            _stateSub.Text = "";
            Controls.Add(_stateSub);

            _pathLine = new Label();
            _pathLine.AutoSize = false;
            _pathLine.BackColor = Color.Transparent;
            _pathLine.ForeColor = InkFaint;
            _pathLine.Font = F("Consolas", 11f);
            _pathLine.TextAlign = ContentAlignment.MiddleLeft;
            _pathLine.Location = new Point(S(SideW + 46), S(DesignH - 212));
            _pathLine.Size = new Size(S(DesignW - SideW - 92), S(60));
            _pathLine.Text = "";
            Controls.Add(_pathLine);

            // ---- the one dominant action button, bottom right --------------
            _action = new FlatButton();
            _action.Size = new Size(S(BtnW), S(BtnH));
            _action.Location = new Point(S(DesignW - BtnW - 44), S(DesignH - BtnH - 40));
            _action.SetState(Strings.Start, "", Accent, true);
            _action.Click += ActionClick;
            Controls.Add(_action);

            Load += OnLoad;
            FormClosing += OnFormClosing;
        }

        // --------------------------------------------------------------------
        // construction helpers
        // --------------------------------------------------------------------
        private float MeasureScale()
        {
            float s = 1f;
            try
            {
                using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            }
            catch { }
            if (s < 1f) s = 1f;
            if (s > 4f) s = 4f;
            return s;
        }

        private int S(int designPixels)
        {
            return (int)Math.Round(designPixels * _scale, MidpointRounding.AwayFromZero);
        }

        private float SF(float designPixels) { return designPixels * _scale; }

        /// <summary>Pixel-sized font, scaled once. See the note at the top.</summary>
        private Font F(string family, float designPixels, FontStyle style)
        {
            try { return new Font(family, SF(designPixels), style, GraphicsUnit.Pixel); }
            catch { return new Font(FontFamily.GenericSansSerif, SF(designPixels), style, GraphicsUnit.Pixel); }
        }

        private Font F(string family, float designPixels) { return F(family, designPixels, FontStyle.Regular); }

        private Label MakeSectionHeader(string text, int y)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Text = text;
            l.Font = F("Segoe UI Semibold", 11f, FontStyle.Bold);
            l.ForeColor = InkFaint;
            l.BackColor = SideFill;
            l.Location = new Point(S(24), y);
            l.Size = new Size(S(SideW - 40), S(20));
            return l;
        }

        private Label MakeSideLabel(Control parent, string text, int y, Color colour)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Text = text;
            l.Font = F("Segoe UI", 12f);
            l.ForeColor = colour;
            l.BackColor = SideFill;
            l.Location = new Point(S(24), y);
            l.Size = new Size(S(SideW - 40), S(22));
            parent.Controls.Add(l);
            return l;
        }

        /// <summary>
        /// The hero artwork. It is embedded in the executable, so a single-file
        /// build still shows the background and a missing assets folder cannot
        /// leave the UI blank.
        /// </summary>
        private Image LoadBackground()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("background.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream s = asm.GetManifestResourceStream(name))
                        {
                            if (s != null)
                            {
                                // Copy first: Image.FromStream needs the stream alive.
                                byte[] buf = new byte[s.Length];
                                int read = 0;
                                while (read < buf.Length)
                                {
                                    int n = s.Read(buf, read, buf.Length - read);
                                    if (n <= 0) break;
                                    read += n;
                                }
                                using (MemoryStream ms = new MemoryStream(buf, 0, read))
                                    return Image.FromStream(ms);
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // --------------------------------------------------------------------
        // lifecycle
        // --------------------------------------------------------------------
        private void OnLoad(object sender, EventArgs e)
        {
            _install = MultisimLocator.Find();
            if (_install == null || !_install.IsUsable)
            {
                SetState(Strings.NotFound, Danger);
                SetSub(Strings.InstallFirst);
                _action.SetState(Strings.Start, "", Accent, false);
                RefreshDetailLabels();
                return;
            }
            SetState(Strings.Ready, Ink);
            SetSub("Multisim " + _install.Version);
            RefreshDetailLabels();
            Log("found: " + _install.ExePath + " (" + _install.Source + ")");
        }

        private void RefreshDetailLabels()
        {
            if (_install == null)
            {
                if (_attemptText != null) _attemptText.Text = "";
                if (_dbMaster != null) _dbMaster.Text = "";
                if (_dbCorporate != null) _dbCorporate.Text = "";
                if (_pathLine != null) _pathLine.Text = "";
                return;
            }

            if (_attemptText != null)
            {
                _attemptText.Text = _attempt > 0
                    ? Strings.AttemptPrefix + _attempt + Strings.AttemptSuffix
                    : "";
            }

            if (_dbMaster != null && _dbCorporate != null)
            {
                int locks = SessionHealth.CountLockFiles(_install);
                bool master = locks >= 1;
                bool corporate = locks >= 2;
                _dbMaster.Text = Strings.MasterDb + "  " + (master ? Strings.Loaded : Strings.NotLoaded);
                _dbMaster.ForeColor = master ? Good : InkFaint;
                _dbCorporate.Text = Strings.CorporateDb + "  " + (corporate ? Strings.Loaded : Strings.NotLoaded);
                _dbCorporate.ForeColor = corporate ? Good : InkFaint;
            }

            if (_pathLine != null)
            {
                if (_showDetails)
                {
                    _pathLine.Text = _install.ExePath
                        + Environment.NewLine + "database: " + (_install.DatabaseDir ?? "?")
                        + Environment.NewLine + "source: " + _install.Source;
                }
                else
                {
                    _pathLine.Text = _install.ExePath;
                }
            }
        }

        private void SetState(string text, Color colour)
        {
            _stateText.Text = text;
            _stateText.ForeColor = colour;
        }

        private void SetSub(string text) { _stateSub.Text = text; }

        // --------------------------------------------------------------------
        // the one action button: Start  <->  Stop
        // --------------------------------------------------------------------
        private void ActionClick(object sender, EventArgs e)
        {
            if (_busy) { _cancelled = true; SetState(Strings.Stopping, Busy); return; }
            if (_install == null || !_install.IsUsable) return;

            _cancelled = false;
            _busy = true;
            _attempt = 0;
            _action.SetState(Strings.Stop, Strings.Running, Danger, true);
            SetState(Strings.Starting, Busy);
            SetSub("");
            Log("--- session start ---");

            Thread t = new Thread(new ThreadStart(Worker));
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>Keep cold-starting Multisim until one instance is healthy.</summary>
        private void Worker()
        {
            for (int i = 0; i < MaxAttempts; i++)
            {
                if (_cancelled) break;
                _attempt = i + 1;
                Ui(delegate
                {
                    SetState(Strings.Starting, Busy);
                    SetSub(Strings.AttemptPrefix + _attempt + Strings.AttemptSuffix);
                    RefreshDetailLabels();
                });

                bool ok = TryOneLaunch();
                if (_cancelled) break;

                if (ok)
                {
                    _successCount++;
                    int shown = _attempt;
                    Ui(delegate
                    {
                        SetState(Strings.Running, Good);
                        SetSub(shown == 1
                            ? Strings.FirstTryOk
                            : Strings.AttemptPrefix + shown + Strings.AttemptSuffix);
                        _action.SetState(Strings.Stop, Strings.Running, Danger, true);
                        RefreshDetailLabels();
                    });
                    Log("successful launch on attempt " + _attempt);

                    WatchUntilExit();

                    _busy = false;
                    _attempt = 0;
                    Ui(delegate
                    {
                        _action.SetState(Strings.Start, "", Accent, true);
                        SetState(Strings.Ready, Ink);
                        SetSub("Multisim " + _install.Version);
                        RefreshDetailLabels();
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
                _action.SetState(Strings.Start, "", Accent, true);
                if (cancelled) { SetState(Strings.Ready, Ink); SetSub("Multisim " + _install.Version); }
                else { SetState(Strings.GaveUp, Danger); SetSub(""); }
                RefreshDetailLabels();
            });
            Log("--- session end (no success) ---");
        }

        /// <summary>
        /// Diagnostic entry point: run one silent attempt and record, moment by
        /// moment, what is actually on screen.
        ///
        /// This exists because the whole feature is a claim about visibility -
        /// "the user never sees a failed attempt" - and that claim is only worth
        /// anything if it has been observed. Sampling the visible window count
        /// every 50 ms produces the evidence directly.
        ///
        /// The Multisim window is made to load its databases from a directory
        /// that does not exist, which is the fastest reliable way to force the
        /// failure path without touching the real installation.
        /// </summary>
        internal static int DiagnoseAttempt(int repetitions, bool forceFailure, bool noHideLoop, string outPath)
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();

            for (int run = 1; run <= repetitions; run++)
            {
                MainForm f = new MainForm(new string[0]);
                IntPtr handle = f.Handle;             // realise the window, never show it

                // OnLoad does not run because the window is never shown, so the
                // install has to be located here.
                f._install = MultisimLocator.Find();
                if (f._install == null)
                {
                    report.AppendLine("run " + run + ": Multisim not found");
                    try { f.ForceCloseForDiagnostics(); } catch { }
                    continue;
                }

                if (forceFailure)
                {
                    // LockDirs is derived from these, so pointing them somewhere
                    // that does not exist guarantees the lock-file count can never
                    // reach the threshold and the attempt is treated as failed.
                    // Nothing in the real installation is touched.
                    f._install.DatabaseDir = @"C:\__multisim_probe_nonexistent__";
                    f._install.UserDatabaseDir = @"C:\__multisim_probe_nonexistent__";
                }

                // Control case: skip the hide loop but keep STARTF_USESHOWWINDOW,
                // to tell "hiding breaks the launch" apart from "the hidden start
                // itself breaks it".
                if (noHideLoop) f._hideWhileStarting = false;

                report.AppendLine("=== run " + run + "  (forceFailure=" + forceFailure + ") ===");

                System.Threading.Thread t = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                        int samples = 0, visibleSeen = 0, maxVisible = 0;
                        bool done = false;

                        System.Threading.Thread worker = new System.Threading.Thread(delegate()
                        {
                            try { f.TryOneLaunch(); } catch { }
                            done = true;
                        });
                        worker.IsBackground = true;
                        worker.Start();

                        while (!done && sw.ElapsedMilliseconds < 90000)
                        {
                            int sourcePid = 0;
                            foreach (System.Diagnostics.Process q in System.Diagnostics.Process.GetProcessesByName("multisim"))
                            {
                                try { sourcePid = q.Id; } catch { }
                                finally { try { q.Dispose(); } catch { } }
                                break;   // only one attempt runs at a time
                            }

                            if (sourcePid != 0)
                            {
                                // Record every top-level window and its state,
                                // not just the visible ones. The hide loop runs
                                // far more often than this sampler yet reported
                                // almost no hiding, which can only mean the two
                                // are not seeing the same window set.
                                System.Text.StringBuilder states = new System.Text.StringBuilder();
                                int vis = 0;
                                foreach (IntPtr h in Win32.TopLevelWindows((uint)sourcePid))
                                {
                                    bool v = Win32.IsWindowVisible(h);
                                    if (v) vis++;
                                    states.Append(" ").Append(h).Append(v ? ":VIS" : ":hid");
                                }
                                samples++;
                                if (vis > 0) visibleSeen++;
                                if (vis > maxVisible) maxVisible = vis;

                                // Progress towards health, to tell "the database
                                // never opened" apart from "the window appeared
                                // but the check did not accept it".
                                if (samples % 20 == 0)
                                    report.AppendLine("  t=" + sw.ElapsedMilliseconds.ToString("00000")
                                                      + "ms  locks=" + SessionHealth.CountLockFiles(f._install)
                                                      + "  healthy=" + f.IsHealthyInstance((uint)sourcePid)
                                                      + "  errDialog=" + SessionHealth.HasDatabaseErrorDialog((uint)sourcePid));

                                if (vis > 0)
                                    report.AppendLine("  t=" + sw.ElapsedMilliseconds.ToString("00000")
                                                      + "ms  VISIBLE=" + vis);
                            }
                            System.Threading.Thread.Sleep(50);
                        }

                        report.AppendLine("  samples=" + samples
                                          + "  samplesWithVisibleWindow=" + visibleSeen
                                          + "  peak=" + maxVisible
                                          + "  hideTicks=" + MainForm.HideTicks
                                          + "  hideActions=" + MainForm.HideCount);
                    }
                    catch (Exception ex) { report.AppendLine("  EXCEPTION " + ex.Message); }
                });
                t.IsBackground = true;
                t.Start();
                t.Join(120000);

                try { f.ForceCloseForDiagnostics(); } catch { }
            }

            try { System.IO.File.WriteAllText(outPath, report.ToString(), new System.Text.UTF8Encoding(false)); }
            catch { }
            return 0;
        }

        /// <summary>Close without the tray/close interception, for diagnostics.</summary>
        internal void ForceCloseForDiagnostics()
        {
            try { Close(); } catch { }
        }

        /// <summary>
        /// One cold start; true only when the instance is usable.
        ///
        /// The start happens off screen. A Multisim that is going to fail shows
        /// its splash and its main window before the database error appears, so
        /// starting it normally means the user watches every failed attempt go
        /// by. Instead, the frame and splash are hidden as soon as they appear
        /// and only a confirmed-healthy instance is revealed - so the window the
        /// user finally sees is the one that actually works.
        /// </summary>
        internal bool TryOneLaunch()
        {
            KillResident();
            SessionHealth.RemoveLockFiles(_install);

            Process p;
            try
            {
                // StartHidden, not Process.Start: STARTF_USESHOWWINDOW is what
                // keeps the splash off screen from the very first frame. Without
                // it there is a short flash at the start of every attempt, which
                // the diagnostic measured and which is exactly what this feature
                // is supposed to remove.
                p = _hideWhileStarting
                    ? Win32.StartHidden(_install.ExePath, _install.InstallDir)
                    : Process.Start(new ProcessStartInfo(_install.ExePath)
                      {
                          WorkingDirectory = _install.InstallDir,
                          UseShellExecute = true   // exactly like a desktop double-click
                      });

                if (p == null && _hideWhileStarting)
                {
                    // CreateProcess can refuse; fall back rather than fail, since
                    // a visible start is still a working start.
                    Log("StartHidden failed, falling back to a normal start");
                    ProcessStartInfo psi = new ProcessStartInfo(_install.ExePath);
                    psi.WorkingDirectory = _install.InstallDir;
                    psi.UseShellExecute = true;
                    p = Process.Start(psi);
                }
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

            // Keep it off screen for the whole attempt. The thread polls at 12 ms
            // so a window that appears is gone before it can be noticed; the
            // health checks below are far too slow to do that themselves.
            if (_hideWhileStarting) StartHiding((uint)p.Id);

            while (sw.ElapsedMilliseconds < StartTimeoutMs)
            {
                if (_cancelled) break;
                Sleep(150);

                try { if (p.HasExited) break; } catch { break; }

                if (SessionHealth.HasDatabaseErrorDialog((uint)p.Id)) { sawDialog = true; break; }
                if (IsHealthyInstance((uint)p.Id)) { healthy = true; break; }
            }

            if (!healthy) { StopHiding(); Discard(p); return false; }

            // Databases are open - now make sure it stays that way. The known
            // broken instances used to slip through exactly here.
            Stopwatch settle = Stopwatch.StartNew();
            while (settle.ElapsedMilliseconds < SettleMs)
            {
                if (_cancelled) break;
                Sleep(150);
                try { if (p.HasExited) { StopHiding(); Discard(p); return false; } }
                catch { StopHiding(); Discard(p); return false; }
                if (SessionHealth.HasDatabaseErrorDialog((uint)p.Id)) { StopHiding(); Discard(p); return false; }
                if (SessionHealth.CountLockFiles(_install) < SessionHealth.RequiredLocks) { StopHiding(); Discard(p); return false; }
            }

            if (_cancelled) { StopHiding(); Discard(p); return false; }
            if (sawDialog) { StopHiding(); Discard(p); return false; }

            // Stop hiding BEFORE revealing, and wait for the thread to be gone.
            //
            // This ordering is the whole feature. An earlier version stopped the
            // thread in a finally block that ran after the reveal, so the hide
            // loop was still running at the moment the window was shown and put
            // it straight back - the successful case then never appeared at all.
            // StopHiding joins the thread, so once it returns nothing can hide
            // the window again.
            StopHiding();

            // Healthy: this is the one to show. Revealing it here, rather than
            // leaving it hidden, is the whole point - a working Multisim appears
            // as if it had started immediately.
            if (_hideWhileStarting) RevealShellWindow((uint)p.Id);

            Ui(delegate { RefreshDetailLabels(); });
            return true;
        }

        /// <summary>
        /// Health test for an attempt that is being kept hidden.
        ///
        /// SessionHealth.IsHealthy requires the main window to be *visible*, which
        /// is correct when the user is watching but makes the hidden start
        /// impossible to confirm: the window is deliberately hidden, so the test
        /// could never pass, every attempt ran to its timeout and was thrown away,
        /// and the databases opened for nothing. Measured: locks=3 for 60 seconds
        /// while healthy stayed false.
        ///
        /// So the window only has to exist here, not be visible - which is the
        /// right question anyway once the launcher is the thing controlling
        /// visibility. Everything else is unchanged: the databases must be open,
        /// and a partially built frame does not count.
        /// </summary>
        private bool IsHealthyInstance(uint pid)
        {
            if (SessionHealth.CountLockFiles(_install) < SessionHealth.RequiredLocks) return false;
            foreach (IntPtr h in Win32.TopLevelWindows(pid))
            {
                if (!Win32.IsMultisimShellWindow(h)) continue;
                // Existence of the frame is the signal; visibility is ours to set.
                return true;
            }
            return false;
        }

        /// <summary>
        /// Hide whatever a starting Multisim has just put on screen.
        ///
        /// Measured with --diag-attempt, which reported window classes and, for
        /// dialogs, their contents. Four findings shaped this:
        ///
        ///   * ShowWindowAsync only posts a message and returns; it had no effect
        ///     at all on another process's window, so the frame stayed visible
        ///     for the whole attempt. The synchronous ShowWindow hides it.
        ///
        ///   * the splash screen is a dialog - title "Multisim", class #32770 -
        ///     so an earlier "never hide dialogs" rule let it sit on screen for
        ///     the entire attempt. The database error box is also #32770 titled
        ///     "Multisim", so class and title cannot separate them.
        ///
        ///   * this method must not block. It originally called the error-dialog
        ///     test, which uses SendMessageTimeout with up to a second per child
        ///     window; on a 12 ms loop that throttled hiding to a few times a
        ///     second and windows were routinely caught on screen. Everything
        ///     here is now a cheap, non-blocking window query.
        ///
        ///   * the error box is hidden along with everything else, which is safe:
        ///     the health-check loop identifies it on its own and Discard closes
        ///     it moments later. Leaving it visible gained nothing and cost the
        ///     blocking call above.
        /// </summary>
        private void HideShellWindows(uint pid)
        {
            _hideTicks++;
            try
            {
                foreach (IntPtr h in Win32.TopLevelWindows(pid))
                {
                    try
                    {
                        if (!Win32.IsWindowVisible(h)) continue;
                        Win32.ShowWindow(h, Win32.SW_HIDE);
                        _hideCount++;
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Diagnostic counters, read by DiagnoseAttempt to prove the hide loop is
        // actually running rather than silently starved.
        internal static int HideTicks { get { return _hideTicks; } }
        internal static int HideCount { get { return _hideCount; } }
        private static int _hideTicks;
        private static int _hideCount;

        /// <summary>
        /// Keep a starting instance off screen until it is either healthy or
        /// discarded. Runs on its own thread; returns immediately and the caller
        /// carries on with the health checks.
        /// </summary>
        private void StartHiding(uint pid)
        {
            _hidePid = pid;
            _hideStop = false;
            _hideThread = new Thread(new ThreadStart(delegate()
            {
                while (!_hideStop)
                {
                    // Re-read each pass: Discard clears it to make the thread let
                    // go of a process that is being closed.
                    uint target = _hidePid;
                    if (target != 0) HideShellWindows(target);
                    Thread.Sleep(12);
                }
            }));
            _hideThread.IsBackground = true;
            _hideThread.Start();
        }

        private void StopHiding()
        {
            _hideStop = true;
            Thread t = _hideThread;
            _hideThread = null;
            if (t != null)
            {
                try { t.Join(500); } catch { }
            }
        }

        /// <summary>
        /// Reveal the main frame once the instance is known to be good, and put
        /// it in front. The splash is not restored - it has served its purpose.
        /// </summary>
        private void RevealShellWindow(uint pid)
        {
            try
            {
                foreach (IntPtr h in Win32.TopLevelWindows(pid))
                {
                    try
                    {
                        if (!Win32.IsMultisimShellWindow(h)) continue;
                        string cls = Win32.ClassOf(h);
                        // The frame is "Multisim*"; "LVFrame" is the LabVIEW host
                        // window, which stays behind it.
                        if (!cls.StartsWith("Multisim", StringComparison.Ordinal)) continue;

                        Win32.ShowWindow(h, Win32.SW_RESTORE);
                        Win32.ShowWindow(h, Win32.SW_SHOW);
                        Win32.RaiseWithoutActivating(h);
                        Win32.SetForegroundWindow(h);
                        Log("revealed the main window (hwnd " + h + ")");
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Close an attempt that did not make it, without any noise.</summary>
        private void Discard(Process p)
        {
            // Let go before closing it. Without this the hide thread and the
            // close race each other: the thread keeps hiding the window while
            // WM_CLOSE is being posted to it, which is needless churn on a
            // process that is on its way out.
            _hidePid = 0;

            try
            {
                if (p != null && !p.HasExited)
                {
                    Win32.PostCloseToAll((uint)p.Id);
                    if (!p.WaitForExit(2500))
                    {
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(3000); } catch { }
                    }
                }
            }
            catch { }
            SessionHealth.RemoveLockFiles(_install);
            Ui(delegate { RefreshDetailLabels(); });
        }

        private void KillResident()
        {
            Process[] all;
            try { all = Process.GetProcessesByName("multisim"); }
            catch { return; }
            foreach (Process p in all)
            {
                try { if (!p.HasExited) Win32.PostCloseToAll((uint)p.Id); }
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

        /// <summary>Sit quietly after a good launch until Multisim closes.</summary>
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
            if (_background != null) { try { _background.Dispose(); } catch { } }
        }

        private void OpenLogFile()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MultisimLauncher", "launcher.log");
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "", Encoding.UTF8);
                }
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log("open log failed: " + ex.Message); }
        }

        // --------------------------------------------------------------------
        // small helpers
        // --------------------------------------------------------------------
        private void Log(string line)
        {
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

        // --------------------------------------------------------------------
        // painting
        // --------------------------------------------------------------------
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Smooth(e.Graphics);
            // The sidebar is filled with an opaque colour and the artwork is
            // only drawn in the hero region. Keeping the two apart means no
            // control ever has to blend with a semi-transparent parent, which
            // is what produced unreadable ghost text earlier.
            int sideW = S(SideW);
            using (SolidBrush sb = new SolidBrush(SideFill))
            {
                e.Graphics.FillRectangle(sb, 0, 0, sideW, Height);
            }

            Rectangle hero = new Rectangle(sideW, 0, Width - sideW, Height);

            if (_background != null)
            {
                // cover-fit: fill the hero area without distorting the artwork
                float sx = (float)hero.Width / _background.Width;
                float sy = (float)hero.Height / _background.Height;
                float s = Math.Max(sx, sy);
                int w = (int)(_background.Width * s);
                int h = (int)(_background.Height * s);
                int x = hero.X + (hero.Width - w) / 2;
                int y = hero.Y + (hero.Height - h) / 2;
                e.Graphics.DrawImage(_background, new Rectangle(x, y, w, h));
            }
            else
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                    hero, Color.FromArgb(12, 24, 42), Color.FromArgb(18, 42, 72), 90f))
                {
                    e.Graphics.FillRectangle(b, hero);
                }
            }

            // scrim so the text over the artwork stays readable
            using (LinearGradientBrush sc = new LinearGradientBrush(
                hero, Color.FromArgb(150, 6, 14, 26), Color.FromArgb(40, 6, 14, 26), 25f))
            {
                e.Graphics.FillRectangle(sc, hero);
            }

            // left edge of the sidebar, a hairline for definition
            using (Pen p = new Pen(SideEdge, 1f))
            {
                e.Graphics.DrawLine(p, S(SideW), 0, S(SideW), Height);
            }
        }

        private void SidebarPaint(object sender, PaintEventArgs e)
        {
            Control c = (Control)sender;
            Smooth(e.Graphics);
            using (SolidBrush b = new SolidBrush(SideFill))
            {
                e.Graphics.FillRectangle(b, 0, 0, c.Width, c.Height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Smooth(e.Graphics);
        }

        internal static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // AntiAlias, NOT ClearTypeGridFit. ClearType needs to know the real
            // background colour to do subpixel blending; these labels are drawn
            // with a transparent background over a custom-painted parent, so
            // ClearType smears the glyphs into unreadable ghost text. Grayscale
            // antialiasing has no such requirement.
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        }
    }

    // ------------------------------------------------------------------------
    // FlatButton - the single dominant action button.
    // ------------------------------------------------------------------------
    internal sealed class FlatButton : Control
    {
        private string _label = "";
        private string _sublabel = "";
        private Color _base = Color.FromArgb(64, 156, 255);
        private bool _hover, _down, _enabledVisual = true;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public void SetState(string label, string sublabel, Color baseColor, bool enabledVisual)
        {
            _label = label;
            _sublabel = sublabel;
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
            if (!_enabledVisual) fill = Color.FromArgb(58, 70, 88);
            else if (_down) fill = Darken(_base, 0.82f);
            else if (_hover) fill = Lighten(_base, 0.10f);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Rounded(r, (int)(Height * 0.22f)))
            using (SolidBrush b = new SolidBrush(fill))
            {
                e.Graphics.FillPath(b, path);
            }

            bool twoLine = !string.IsNullOrEmpty(_sublabel);
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                if (twoLine)
                {
                    using (Font f1 = new Font("Segoe UI Semibold", Height * 0.28f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Font f2 = new Font("Segoe UI", Height * 0.17f, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (Brush b1 = new SolidBrush(Color.White))
                    using (Brush b2 = new SolidBrush(Color.FromArgb(218, 236, 255)))
                    {
                        RectangleF top = new RectangleF(0, Height * 0.14f, Width, Height * 0.44f);
                        RectangleF bot = new RectangleF(0, Height * 0.52f, Width, Height * 0.34f);
                        e.Graphics.DrawString(_label, f1, b1, top, sf);
                        e.Graphics.DrawString(_sublabel, f2, b2, bot, sf);
                    }
                }
                else
                {
                    using (Font f1 = new Font("Segoe UI Semibold", Height * 0.30f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (Brush b1 = new SolidBrush(_enabledVisual ? Color.White : Color.FromArgb(150, 158, 172)))
                    {
                        e.Graphics.DrawString(_label, f1, b1, new RectangleF(0, 0, Width, Height), sf);
                    }
                }
            }
        }

        internal static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
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
    // SidebarItem - flat navigation row with a hover highlight and an optional
    // "enabled/checked" marker for toggles such as Show details.
    // ------------------------------------------------------------------------
    internal sealed class SidebarItem : Control
    {
        private bool _hover;
        private bool _checked;

        public SidebarItem(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Font = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public void SetChecked(bool value) { _checked = value; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            MainForm.Smooth(e.Graphics);

            if (_hover)
            {
                using (GraphicsPath p = FlatButton.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 7))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(34, 120, 180, 255)))
                {
                    e.Graphics.FillPath(b, p);
                }
            }

            Color text = _hover ? Color.FromArgb(232, 240, 250) : Color.FromArgb(168, 184, 206);
            using (Font f = new Font("Segoe UI", Height * 0.34f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(text))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(Text, f, b, new RectangleF(Height * 0.30f, 0, Width - Height * 0.30f, Height), sf);
            }

            // a small dot marks a toggle that is currently on
            if (_checked)
            {
                float d = Height * 0.20f;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(64, 156, 255)))
                {
                    e.Graphics.FillEllipse(b, Height * 0.44f - d / 2, (Height - d) / 2, d, d);
                }
            }
        }
    }

    // ------------------------------------------------------------------------
    // AppIcon - drawn at runtime so a source checkout needs no binary asset.
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
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(64, 156, 255)))
                    using (GraphicsPath p = FlatButton.Rounded(new Rectangle(4, 4, 55, 55), 14))
                    {
                        g.FillPath(b, p);
                    }
                    using (Font f = new Font("Segoe UI Semibold", 34f, FontStyle.Bold, GraphicsUnit.Pixel))
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
