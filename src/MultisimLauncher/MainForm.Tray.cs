// ============================================================================
//  MainForm.Tray.cs - minimising to the notification area, and preheat mode.
//
//  Two behaviours live here because they share the same question - should this
//  window be on screen right now? - and splitting them would mean the answer was
//  computed in two places.
//
//  Closing the window minimises to the tray rather than exiting, so the retry
//  loop can finish its work in the background. Exiting is explicit: the tray
//  menu, or Quit in the sidebar. A user who wants a plain close-and-exit gets it
//  from the tray menu, and the balloon on the first minimise says where the
//  window went.
//
//  Preheat mode is a login launch: the window is never shown, so the user sees
//  nothing at all. If Multisim is already up, preheat does nothing - starting it
//  again would produce a second copy, because multisim.exe is not single
//  instance.
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace MultisimLauncher
{
    internal sealed partial class MainForm
    {
        /// <summary>Shown is used for preheat because the message loop is running by then.</summary>
        private void OnShown(object sender, EventArgs e)
        {
            SyncPreheatCheckbox();

            if (!_preheatMode) return;

            // Never on screen in this mode. ShowInTaskbar has to go too, or a
            // hidden window still leaves a taskbar button behind.
            ShowInTaskbar = false;
            Hide();
            Log("--- preheat started ---");
            StartPreheat();
        }

        /// <summary>
        /// Login-time warm-up: reuse a running Multisim, otherwise start the
        /// normal retry loop and stay out of sight.
        /// </summary>
        private void StartPreheat()
        {
            RunningInstance existing = MultisimSession.FindUsable();
            if (existing != null)
            {
                Log("preheat: Multisim already running (pid " + existing.Pid + "), nothing to do");
                ExitApplication();
                return;
            }

            _cancelled = false;
            _busy = true;
            _attempt = 0;
            _action.SetState(Strings.Stop, Strings.Running, Danger, true);
            SetState(Strings.PreheatShort, Busy);
            SetSub(Strings.PreheatWaiting);

            System.Threading.Thread t = new System.Threading.Thread(new System.Threading.ThreadStart(Worker));
            t.IsBackground = true;
            t.Start();
        }

        // --------------------------------------------------------------------
        // tray
        // --------------------------------------------------------------------
        private void EnsureTray()
        {
            if (_tray != null) return;

            _tray = new TrayIcon(Program.AppName, AppIcon.Create());
            _tray.ShowRequested += delegate
            {
                Ui(delegate { RestoreFromTray(); });
            };
            _tray.ExitRequested += delegate
            {
                Ui(delegate { ExitApplication(); });
            };
        }

        /// <summary>Hide the window and keep running in the notification area.</summary>
        private void MinimiseToTray(bool announce)
        {
            Log("minimise: requested");
            try
            {
                EnsureTray();
                Log("minimise: tray icon created");

                // Only explain where the window went the first time. After that
                // the user knows, and a balloon on every minimise is just noise.
                bool firstTime = !_hiddenToTray;
                _hiddenToTray = true;

                // ShowInTaskbar must be cleared while the window is hidden,
                // otherwise the taskbar keeps a button for an invisible window.
                ShowInTaskbar = false;
                Hide();
                Log("minimise: window hidden");

                if (announce && firstTime && _tray != null)
                    _tray.Balloon(Strings.TrayHint, Strings.TrayRestoreHint);

                Log("minimised to tray");
            }
            catch (Exception ex)
            {
                // Never let a tray failure become a crash: the launcher is more
                // useful without an icon than not running at all.
                Log("minimise failed: " + ex.Message);
            }
        }

        private void RestoreFromTray()
        {
            _hiddenToTray = false;
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;

            // Show() alone often leaves the window behind whatever is focused.
            try { Activate(); } catch { }
            try { Win32.FocusWindow(Handle); } catch { }
            Log("restored from tray");
        }

        /// <summary>The one place that actually quits.</summary>
        private void ExitApplication()
        {
            _exiting = true;
            _cancelled = true;
            try { Close(); } catch { }
        }

        /// <summary>Update the tray tooltip to mirror the current state.</summary>
        private void SetTrayStatus(string text)
        {
            if (_tray == null) return;
            _tray.SetTooltip(Program.AppName + " - " + text);
        }

        // --------------------------------------------------------------------
        // Self-test hooks. Used only by Program.SelfTest, which runs when the
        // executable is started with --selftest-tray. Driving this through
        // synthetic mouse events required DPI-correct coordinates and a window
        // that was genuinely on screen, and got the click in the wrong place
        // more than once; calling the code directly tests the behaviour itself.
        // --------------------------------------------------------------------
        internal void TraySelfTest()
        {
            MinimiseToTray(false);
        }

        internal void RestoreSelfTest()
        {
            RestoreFromTray();
        }

        internal bool TrayIconAlive
        {
            get { return _tray != null; }
        }

        /// <summary>Close without the tray interception, for tests.</summary>
        internal void ForceClose()
        {
            _exiting = true;
            _cancelled = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _tray != null)
            {
                // Without this the icon can stay in the notification area until
                // the mouse passes over it, long after the process is gone.
                _tray.Dispose();
                _tray = null;
            }
            base.Dispose(disposing);
        }
    }
}
