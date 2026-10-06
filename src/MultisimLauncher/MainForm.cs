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
    internal sealed partial class MainForm : Form
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
        private readonly ToggleItem _navPreheat;
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

        // Tray presence. Created lazily, the first time the window is hidden -
        // a user who never minimises never gets an icon in the notification area.
        private TrayIcon _tray;

        // True while the window is deliberately parked in the tray, so the close
        // handler can tell "user closed the window" from "we are shutting down".
        private bool _hiddenToTray;

        // Set only by the tray's Exit command and the Quit item, so that closing
        // the window can mean minimising while exiting still exits.
        private bool _exiting;

        // Started by --preheat: no visible window at all, just the retry loop.
        private readonly bool _preheatMode;

        // Set by TryOneLaunch when it focused a live Multisim instead of starting
        // one, so the UI can say what actually happened.
        private bool _reusedExisting;

        // Recorded in the closing handler purely so the log can show why the
        // window went away.
        private string _closeReason = "?";

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
                if (string.Equals(a, "--preheat", StringComparison.OrdinalIgnoreCase)) _preheatMode = true;
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

            // Preheat toggle. This is the only setting that changes how the app
            // behaves outside its own window, so it sits first, with its meaning
            // spelled out underneath: the checkbox label alone ("preheat at
            // login") does not say that it also means Multisim starts with the
            // machine.
            _navPreheat = new ToggleItem(Strings.Preheat);
            _navPreheat.Location = new Point(S(14), S(288));
            _navPreheat.Size = new Size(S(SideW - 28), S(30));
            _navPreheat.Click += delegate { OnPreheatToggle(); };
            side.Controls.Add(_navPreheat);

            Label preheatHint = new Label();
            preheatHint.AutoSize = false;
            preheatHint.Text = Strings.PreheatHint;
            preheatHint.Font = F("Segoe UI", 9.5f);
            preheatHint.ForeColor = InkFaint;
            preheatHint.BackColor = SideFill;
            preheatHint.Location = new Point(S(48), S(322));
            preheatHint.Size = new Size(S(SideW - 62), S(20));
            side.Controls.Add(preheatHint);

            _navDetails = new SidebarItem(Strings.ShowDetails);
            _navDetails.Location = new Point(S(14), S(356));
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
            _navLog.Location = new Point(S(14), S(398));
            _navLog.Size = new Size(S(SideW - 28), S(38));
            _navLog.Click += delegate { OpenLogFile(); };
            side.Controls.Add(_navLog);

            _navQuit = new SidebarItem(Strings.Quit);
            _navQuit.Location = new Point(S(14), S(440));
            _navQuit.Size = new Size(S(SideW - 28), S(38));
            _navQuit.Click += delegate { ExitApplication(); };
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
            Shown += OnShown;
            FormClosing += OnFormClosing;
            // Logged separately from FormClosing so a shutdown that skips the
            // closing handler - which is what an unexpected exit looks like - is
            // still visible in the log.
            FormClosed += delegate { Log("form closed (reason=" + _closeReason + ")"); };
        }

        // --------------------------------------------------------------------
        // preheat toggle
        // --------------------------------------------------------------------
        private void OnPreheatToggle()
        {
            bool want = !_navPreheat.Checked;

            if (want && !Autostart.SetEnabled(true))
            {
                // Writing HKCU\...\Run can fail under policy. Say so rather than
                // showing a tick that will not do anything at the next login.
                _navPreheat.SetChecked(false);
                SetSub(Strings.Preheat + " - " + Strings.NotFound);
                Log("preheat: could not write the startup entry");
                return;
            }
            if (!want) Autostart.SetEnabled(false);

            SyncPreheatCheckbox();
            Log("preheat " + (want ? "enabled" : "disabled"));
        }

        /// <summary>
        /// Read the real startup state rather than trusting the tick, so a stale
        /// entry left behind by a move shows up as off.
        /// </summary>
        private void SyncPreheatCheckbox()
        {
            bool on = Autostart.IsEnabled();
            if (_navPreheat != null) _navPreheat.SetChecked(on);
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
            SetTrayStatus(Strings.Ready);
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
                    bool reused = _reusedExisting;
                    Ui(delegate
                    {
                        SetState(reused ? Strings.AlreadyRunning : Strings.Running, reused ? AccentSoft : Good);
                        if (reused) SetSub(Strings.FocusedExisting);
                        else SetSub(shown == 1
                            ? Strings.FirstTryOk
                            : Strings.AttemptPrefix + shown + Strings.AttemptSuffix);
                        _action.SetState(Strings.Stop, Strings.Running, Danger, true);
                        SetTrayStatus(reused ? Strings.AlreadyRunning : Strings.Running);
                        RefreshDetailLabels();
                    });
                    Log(reused
                        ? "reused the running instance"
                        : "successful launch on attempt " + _attempt);

                    if (_preheatMode)
                    {
                        // Login warm-up. The work is done, so get out of the way
                        // entirely rather than sitting invisible in memory for the
                        // rest of the session.
                        Log("preheat: Multisim is up; launcher exiting");
                        ExitApplication();
                        return;
                    }

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

            if (_preheatMode)
            {
                // Nothing was achieved and there is no window to report it in.
                // Leave a note in the log and get out of the way; the user will
                // start the launcher by hand, which retries from scratch.
                Log("preheat: gave up without a working Multisim");
                ExitApplication();
                return;
            }

            Ui(delegate
            {
                _action.SetState(Strings.Start, "", Accent, true);
                if (cancelled) { SetState(Strings.Ready, Ink); SetSub("Multisim " + _install.Version); }
                else { SetState(Strings.GaveUp, Danger); SetSub(""); }
                SetTrayStatus(Strings.Ready);
                RefreshDetailLabels();
            });
            Log("--- session end (no success) ---");
        }

        /// <summary>One cold start; true only when the instance is usable.</summary>
        private bool TryOneLaunch()
        {
            // Before starting anything: is there already a good instance?
            //
            // This replaces an unconditional KillResident() here, which closed
            // every multisim.exe on the machine at the start of each attempt. It
            // was harmless only while the launcher was the sole way Multisim got
            // started. With preheating - or simply a second click on Start - it
            // would have destroyed a perfectly good running session and then
            // started a fresh one, which is the opposite of a warm start.
            //
            // multisim.exe is not single instance, so starting it again would
            // produce a second copy rather than focusing the first. Reusing the
            // live instance is therefore the only correct behaviour.
            RunningInstance existing = MultisimSession.FindUsable();
            if (existing != null)
            {
                Log("multisim already running (pid " + existing.Pid + "); focusing it instead of starting another");
                MultisimSession.Focus(existing);
                _reusedExisting = true;
                return true;
            }

            // Instances that are still loading, or parked on the database error,
            // are not usable. Clear them so this attempt starts from a clean
            // slate; otherwise their lock files and half-built windows confuse
            // the health check below.
            if (MultisimSession.AnyRunning()) KillResident();
            SessionHealth.RemoveLockFiles(_install);
            _reusedExisting = false;

            Process p;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(_install.ExePath);
                psi.WorkingDirectory = _install.InstallDir;
                psi.UseShellExecute = true;   // exactly like a desktop double-click
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

            // Databases are open - now make sure it stays that way. The known
            // broken instances used to slip through exactly here.
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

            Ui(delegate { RefreshDetailLabels(); });
            return true;
        }

        /// <summary>Close an attempt that did not make it, without any noise.</summary>
        private void Discard(Process p)
        {
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
            _closeReason = e.CloseReason.ToString();
            Log("form closing (reason=" + _closeReason + ", exiting=" + _exiting + ")");

            // Which close reasons should actually end the process?
            //
            // Deliberately a whitelist rather than "everything except
            // UserClosing". Testing showed a plain WM_CLOSE arrives as
            // TaskManagerClosing, not UserClosing, so the obvious inverse test
            // silently exited on a close that the user expects to be a minimise.
            // Anything not listed here parks the window in the tray instead.
            bool reallyQuit =
                _exiting ||                                          // Quit, or the tray's Exit
                e.CloseReason == CloseReason.WindowsShutDown ||      // machine is going down
                e.CloseReason == CloseReason.ApplicationExitCall ||  // Application.Exit()
                e.CloseReason == CloseReason.MdiFormClosing;

            if (!reallyQuit)
            {
                e.Cancel = true;
                MinimiseToTray(true);
                return;
            }

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
        public bool Checked { get { return _checked; } }

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
    // ToggleItem - a checkbox, drawn to match the sidebar.
    //
    // SidebarItem marks its state with a dot at Height * 0.44, which sits under
    // the label because the label starts at Height * 0.30. That is survivable for
    // a display preference, but not for a setting that changes what the machine
    // does at login: it has to look like something you can turn on.
    //
    // So this draws a real box on the left with the label to its right, and fills
    // it with an accent and a tick when on.
    // ------------------------------------------------------------------------
    internal sealed class ToggleItem : Control
    {
        private bool _hover;
        private bool _checked;

        public ToggleItem(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        public void SetChecked(bool value)
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }

        public bool Checked { get { return _checked; } }

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

            // the box: a square a little shorter than the row, vertically centred
            float box = Height * 0.52f;
            float bx = Height * 0.16f;
            float by = (Height - box) / 2f;
            RectangleF r = new RectangleF(bx, by, box, box);

            using (GraphicsPath p = FlatButton.Rounded(Rectangle.Round(r), (int)Math.Max(3f, box * 0.28f)))
            {
                if (_checked)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(64, 156, 255)))
                        e.Graphics.FillPath(b, p);
                }
                else
                {
                    using (Pen pen = new Pen(Color.FromArgb(96, 116, 142), Math.Max(1.2f, Height * 0.045f)))
                        e.Graphics.DrawPath(pen, p);
                }
            }

            if (_checked)
            {
                // a hand-drawn tick, so no glyph or font dependency
                using (Pen pen = new Pen(Color.White, Math.Max(1.6f, box * 0.16f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    PointF a = new PointF(bx + box * 0.24f, by + box * 0.52f);
                    PointF b = new PointF(bx + box * 0.44f, by + box * 0.72f);
                    PointF c = new PointF(bx + box * 0.78f, by + box * 0.28f);
                    e.Graphics.DrawLines(pen, new PointF[] { a, b, c });
                }
            }

            Color text = _hover ? Color.FromArgb(232, 240, 250)
                       : _checked ? Color.FromArgb(206, 224, 246)
                       : Color.FromArgb(168, 184, 206);

            float textX = bx + box + Height * 0.30f;
            using (Font f = new Font("Segoe UI", Height * 0.36f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(text))
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(Text, f, b,
                    new RectangleF(textX, 0, Width - textX, Height), sf);
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
