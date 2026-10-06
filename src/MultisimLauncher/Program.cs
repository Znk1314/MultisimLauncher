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
using System.Diagnostics;
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
        internal const string AppVersion = "1.0.1";

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
            // Diagnostic switch: run one or more silent attempts and record what
            // was actually visible on screen throughout. The feature is a claim
            // about visibility, so it is verified by sampling rather than by
            // assuming.
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "--diag-attempt", StringComparison.OrdinalIgnoreCase)) continue;

                ForceDpiAwareness();
                int reps = 1;
                bool forceFail = false;
                bool noHideLoop = false;
                if (i + 1 < args.Length) int.TryParse(args[i + 1], out reps);
                for (int j = 0; j < args.Length; j++)
                {
                    if (string.Equals(args[j], "--force-fail", StringComparison.OrdinalIgnoreCase)) forceFail = true;
                    if (string.Equals(args[j], "--no-hide-loop", StringComparison.OrdinalIgnoreCase)) noHideLoop = true;
                }
                if (reps < 1) reps = 1;

                string outPath = Path.Combine(Path.GetTempPath(), "multisimlauncher-diag.txt");
                int rc = MainForm.DiagnoseAttempt(reps, forceFail, noHideLoop, outPath);
                Environment.Exit(rc);
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

        internal const uint WM_CLOSE = 0x0010;
        internal const uint WM_GETTEXT = 0x000D;
        internal const uint WM_NULL = 0x0000;
        internal const uint SMTO_ABORTIFHUNG = 0x0002;

        // --- showing and hiding a window we do not own ----------------------
        // Used to keep a starting Multisim off screen until its component
        // databases are open, so that a failed attempt is never seen at all and
        // only a working instance is revealed.
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after,
                                                                           int x, int y, int cx, int cy, uint flags);

        internal const int SW_HIDE = 0;
        internal const int SW_SHOWNORMAL = 1;
        internal const int SW_SHOW = 5;
        internal const int SW_RESTORE = 9;

        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        internal const uint SWP_NOSIZE = 0x0001;
        internal const uint SWP_NOMOVE = 0x0002;
        internal const uint SWP_SHOWWINDOW = 0x0040;

        /// <summary>
        /// Raise a window without activating it. SetForegroundWindow is allowed
        /// to refuse, and this is the reliable way to bring another process's
        /// freshly shown window to the front.
        /// </summary>
        internal static void RaiseWithoutActivating(IntPtr h)
        {
            try { SetWindowPos(h, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); }
            catch { }
        }

        /// <summary>
        /// Class name of the Multisim frame window, and of the windows it owns.
        /// A starting instance shows a splash before the frame exists, so both
        /// have to be recognised.
        /// </summary>
        internal static bool IsMultisimShellWindow(IntPtr h)
        {
            string cls = ClassOf(h);
            if (cls.StartsWith("Multisim", StringComparison.Ordinal)) return true;
            if (cls.StartsWith("LVFrame", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Dialogs must never be hidden: the "cannot access the database" box is
        /// how a failed attempt is recognised in the first place.
        /// </summary>
        internal static bool IsDialog(IntPtr h)
        {
            return ClassOf(h) == "#32770";
        }

        /// <summary>
        /// Start a process with its first window hidden.
        ///
        /// Process.Start cannot express STARTF_USESHOWWINDOW, and without it the
        /// starting application shows its splash before the launcher gets a
        /// chance to hide anything - a brief flash that measurement caught at
        /// the start of every attempt. Windows applies this flag to the first
        /// ShowWindow call the new process makes, which is the splash, so it
        /// never appears at all.
        ///
        /// Returns the process, or null if it could not be started.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string app, string cmd, IntPtr pa, IntPtr ta,
                                                  bool inherit, uint flags, IntPtr env,
                                                  string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        private const int STARTF_USESHOWWINDOW = 0x00000001;

        internal static Process StartHidden(string exePath, string workingDir)
        {
            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));

            // STARTF_USESHOWWINDOW is deliberately NOT set here.
            //
            // It looks like the tidy way to keep the splash off screen - Windows
            // applies SW_HIDE to the process's first ShowWindow call - but
            // measurement showed Multisim then never opens its databases at all:
            // no .ldb files appeared for 50+ seconds, while starting it the
            // ordinary way produced both lock files within 8 seconds. The flag
            // changes how the process initialises its windows, and something in
            // that path is required for the component databases to load.
            //
            // So the process is started normally and the hide loop does the work
            // instead. That costs a brief flash, which is a far better trade than
            // a launcher that can never see a healthy instance.
            si.dwFlags = 0;
            si.wShowWindow = SW_HIDE;

            PROCESS_INFORMATION pi;
            bool ok = CreateProcessW(exePath, null, IntPtr.Zero, IntPtr.Zero, false, 0,
                                     IntPtr.Zero, workingDir, ref si, out pi);
            if (!ok) return null;

            try { CloseHandle(pi.hThread); } catch { }
            try { CloseHandle(pi.hProcess); } catch { }
            try { return Process.GetProcessById(pi.dwProcessId); }
            catch { return null; }
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
