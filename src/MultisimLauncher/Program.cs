// ============================================================================
//  Multisim Launcher - retry-until-it-works launcher for NI Multisim
//  ---------------------------------------------------------------------------
//  Why this exists
//    On Windows 11 (24H2/25H2) NI Multisim 14.x intermittently fails at startup
//    with  "Problem accessing the database / The Master Database cannot be
//    accessed", which leaves the component library empty. Which cold start
//    succeeds is effectively random (~25-50% on the reference machine), the
//    failure is not caused by a damaged database, and the failing process just
//    parks on a modal dialog. This launcher simply keeps starting Multisim and
//    quietly discards the attempts that came up broken, until one is healthy.
//
//  Design notes
//    - Compiled with the in-box C# compiler (csc.exe). C# 5 syntax only.
//    - No terminal window: /target:winexe.
//    - Failed attempts stay invisible: the broken instance is closed again
//      immediately and only a status line is updated.
//    - Healthy launch is confirmed by the Jet lock files (.ldb) appearing AND
//      the main window existing AND the process surviving a settle period.
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MultisimLauncher
{
    internal static class Program
    {
        internal const string AppName = "Multisim Launcher";
        internal const string AppVersion = "1.0.2";

        // DPI awareness, belt and braces. The manifest already declares
        // PerMonitorV2; if for any reason that declaration is not honoured,
        // Windows bitmap-scales the whole window and on a 200% display every
        // font is drawn twice as large. Setting it at runtime as well costs
        // nothing, and it must happen BEFORE any window is created.
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] private static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        private static void ForceDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; }
            catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return; }   // 2 = PROCESS_PER_MONITOR_DPI_AWARE
            catch { }
            try { SetProcessDPIAware(); }
            catch { }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            // Self-test switches. These exist because driving this UI from a
            // script needs synthetic mouse events at the right DPI-aware
            // coordinates, which is fragile and put clicks in the wrong place
            // several times. Exercising the code path directly is both simpler
            // and a stronger check: it proves the behaviour, not the hit test.
            foreach (string a in args)
            {
                if (string.Equals(a, "--selftest-preheat", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "--selftest-tray", StringComparison.OrdinalIgnoreCase))
                {
                    ForceDpiAwareness();
                    // This is a /target:winexe build, so the process has no
                    // console and Console.WriteLine goes nowhere. Results are
                    // written to a file next to the launcher's own log, and also
                    // appended to that log so they sit beside the runtime events.
                    string kind = a.EndsWith("preheat", StringComparison.OrdinalIgnoreCase) ? "preheat" : "tray";
                    string resultPath = Path.Combine(Path.GetTempPath(), "multisimlauncher-selftest-" + kind + ".txt");

                    TextWriter saved = Console.Out;
                    StringWriter capture = new StringWriter();
                    int rc;
                    try
                    {
                        Console.SetOut(capture);
                        rc = kind == "preheat" ? SelfTest.PreheatToggle() : SelfTest.TrayCycle();
                    }
                    finally
                    {
                        Console.SetOut(saved);
                    }

                    string body = capture.ToString();
                    try { File.WriteAllText(resultPath, body, new UTF8Encoding(false)); }
                    catch { }

                    // Mirror into the launcher log so a failure is visible where
                    // the other diagnostics already are.
                    try
                    {
                        string dir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "MultisimLauncher");
                        Directory.CreateDirectory(dir);
                        File.AppendAllText(Path.Combine(dir, "launcher.log"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  --- selftest:" + kind
                            + " (exit " + rc + ") ---" + Environment.NewLine + body + Environment.NewLine,
                            new UTF8Encoding(false));
                    }
                    catch { }

                    Environment.Exit(rc);
                }
            }

            ForceDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), AppName + " - fatal error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // ------------------------------------------------------------------------
    // SelfTest - drives the tray and preheat logic without a mouse.
    //
    // Each check prints PASS/FAIL and the process exit code carries the number
    // of failures, so a build script can gate on it.
    // ------------------------------------------------------------------------
    internal static class SelfTest
    {
        internal static int PreheatToggle()
        {
            int failures = 0;
            bool original = Autostart.IsEnabled();
            Console.WriteLine("preheat was " + (original ? "on" : "off"));

            try
            {
                failures += Check("enable writes the startup entry", Autostart.SetEnabled(true));
                failures += Check("and it reads back as on", Autostart.IsEnabled());

                string cmd = null;
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser
                           .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                    if (k != null) cmd = k.GetValue("MultisimLauncher") as string;
                Console.WriteLine("  registry value: " + cmd);
                failures += Check("startup command carries --preheat",
                    cmd != null && cmd.IndexOf("--preheat", StringComparison.OrdinalIgnoreCase) >= 0);

                failures += Check("disable removes it", Autostart.SetEnabled(false) && !Autostart.IsEnabled());
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION " + ex.Message);
                failures++;
            }
            finally
            {
                // Leave the machine exactly as it was found.
                Autostart.SetEnabled(original);
                Console.WriteLine("restored preheat to " + (original ? "on" : "off"));
            }

            Console.WriteLine(failures == 0 ? "PREHEAT TOGGLE: PASS" : "PREHEAT TOGGLE: " + failures + " FAILED");
            return failures;
        }

        internal static int TrayCycle()
        {
            int failures = 0;
            Console.WriteLine("building the form hidden, then cycling the tray icon");

            MainForm f = null;
            try
            {
                f = new MainForm(new string[0]);
                // Force handle creation without ever showing the window.
                IntPtr h = f.Handle;
                failures += Check("form handle created", h != IntPtr.Zero);

                f.TraySelfTest();
                failures += Check("tray icon exists after the cycle", f.TrayIconAlive);

                f.RestoreSelfTest();
                failures += Check("window is visible again after restore", f.Visible);
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION " + ex.Message);
                failures++;
            }
            finally
            {
                if (f != null) { try { f.ForceClose(); } catch { } }
            }

            Console.WriteLine(failures == 0 ? "TRAY CYCLE: PASS" : "TRAY CYCLE: " + failures + " FAILED");
            return failures;
        }

        private static int Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
            return ok ? 0 : 1;
        }
    }

    // ------------------------------------------------------------------------
    // Win32 helpers: window enumeration, text reading with timeout, graceful
    // close. SendMessageTimeoutW is used everywhere instead of SendMessageW,
    // because a synchronous SendMessage blocks forever while multisim.exe is
    // busy loading its ~238 MB database.
    // ------------------------------------------------------------------------
    internal static class Win32
    {
        internal delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool PostMessageW(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr wp, StringBuilder lp,
                                                          uint flags, uint timeout, out IntPtr result);

        // Bring-an-existing-window-forward support. Needed because multisim.exe
        // is NOT single instance: starting it while it already runs produces a
        // second full copy, so "already running" has to be detected and the
        // existing window focused instead.
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();

        internal const uint WM_CLOSE = 0x0010;
        internal const uint WM_GETTEXT = 0x000D;
        internal const uint WM_NULL = 0x0000;
        internal const uint SMTO_ABORTIFHUNG = 0x0002;

        internal const int SW_RESTORE = 9;
        internal const int SW_SHOW = 5;

        /// <summary>Restore the window if minimised and bring it to the front.</summary>
        internal static bool FocusWindow(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            try
            {
                if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
                if (SetForegroundWindow(h)) return true;

                // SetForegroundWindow is allowed to refuse unless the caller owns
                // the foreground. Attaching to the foreground thread's input queue
                // lifts that restriction, which matters here because the launcher
                // may have been started by a scheduled or startup mechanism rather
                // than by a click.
                uint fg = 0;
                IntPtr fore = GetForegroundWindow();
                if (fore != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(fore, out pid);
                    fg = pid;
                }
                uint me = GetCurrentThreadId();
                if (fg != 0 && fg != me)
                {
                    if (AttachThreadInput(me, fg, true))
                    {
                        bool ok = SetForegroundWindow(h);
                        AttachThreadInput(me, fg, false);
                        if (ok) return true;
                    }
                }
                ShowWindow(h, SW_SHOW);
                return SetForegroundWindow(h);
            }
            catch { return false; }
        }

        internal static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassNameW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        internal static string TextOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(1024);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>WM_GETTEXT with a hard timeout; null when the window does not answer.</summary>
        internal static string MsgTextOf(IntPtr h, int len)
        {
            StringBuilder sb = new StringBuilder(len + 2);
            IntPtr res;
            IntPtr ok = SendMessageTimeoutW(h, WM_GETTEXT, (IntPtr)(len + 1), sb,
                                            SMTO_ABORTIFHUNG, 1000, out res);
            if (ok == IntPtr.Zero) return null;
            return sb.ToString();
        }

        internal static bool IsResponsive(IntPtr h)
        {
            IntPtr res;
            return SendMessageTimeoutW(h, WM_NULL, IntPtr.Zero, null, SMTO_ABORTIFHUNG, 150, out res) != IntPtr.Zero;
        }

        internal static IntPtr[] TopLevelWindows(uint pid)
        {
            System.Collections.Generic.List<IntPtr> list = new System.Collections.Generic.List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                uint w;
                GetWindowThreadProcessId(h, out w);
                if (w == pid) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list.ToArray();
        }

        internal static IntPtr[] ChildWindows(IntPtr parent)
        {
            System.Collections.Generic.List<IntPtr> list = new System.Collections.Generic.List<IntPtr>();
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr p) { list.Add(h); return true; }, IntPtr.Zero);
            return list.ToArray();
        }

        /// <summary>Post WM_CLOSE to every top level window of the process.</summary>
        internal static void PostCloseToAll(uint pid)
        {
            foreach (IntPtr h in TopLevelWindows(pid))
                PostMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
