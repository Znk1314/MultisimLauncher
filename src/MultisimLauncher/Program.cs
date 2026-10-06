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
        internal const string AppVersion = "1.0.0";

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
