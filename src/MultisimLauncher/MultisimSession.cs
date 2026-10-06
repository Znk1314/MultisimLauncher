// ============================================================================
//  MultisimSession.cs - working with an already-running Multisim.
//
//  Why this exists
//    multisim.exe is NOT single instance. Launching it while it is already
//    running produces a second complete copy, with its own process, its own
//    windows and its own ~150 MB of memory. Measured on the reference machine:
//    two starts in a row left two processes, both with a visible main window
//    titled "Multisim - [design1]".
//
//    That makes "is one already running?" a question the launcher has to ask
//    before every start, for two separate reasons:
//
//      * so a second click on Start focuses the running copy instead of
//        starting another one
//      * so preheating at login cannot end up leaving two copies running
//
//  All detection is by window class and process name, never by window title:
//  the title carries the open file name and the UI language, so it is not a
//  stable identifier.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MultisimLauncher
{
    /// <summary>A running multisim.exe and the window it is showing.</summary>
    internal sealed class RunningInstance
    {
        public int Pid;
        public IntPtr MainWindow;
        public bool HasMainWindow { get { return MainWindow != IntPtr.Zero; } }
    }

    internal static class MultisimSession
    {
        /// <summary>Class name of the Multisim frame window; starts with this.</summary>
        private const string MainWindowClassPrefix = "Multisim";

        /// <summary>Every multisim.exe process, newest information each call.</summary>
        public static Process[] Processes()
        {
            try { return Process.GetProcessesByName("multisim"); }
            catch { return new Process[0]; }
        }

        public static int Count()
        {
            Process[] all = Processes();
            foreach (Process p in all) { try { p.Dispose(); } catch { } }
            return all.Length;
        }

        /// <summary>
        /// The main frame window of a process, or IntPtr.Zero. Only visible
        /// windows of class "Multisim*" count: a process that is still loading
        /// its database has no such window yet, and one that failed to open the
        /// database never gets one.
        /// </summary>
        public static IntPtr MainWindowOf(uint pid)
        {
            foreach (IntPtr h in Win32.TopLevelWindows(pid))
            {
                if (!Win32.IsWindowVisible(h)) continue;
                if (Win32.ClassOf(h).StartsWith(MainWindowClassPrefix, StringComparison.Ordinal)) return h;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Snapshot of all running instances, each with its main window if it has
        /// one. Instances that are still starting up appear with a zero handle.
        /// </summary>
        public static List<RunningInstance> Snapshot()
        {
            List<RunningInstance> list = new List<RunningInstance>();
            Process[] all = Processes();
            foreach (Process p in all)
            {
                try
                {
                    RunningInstance ri = new RunningInstance();
                    ri.Pid = p.Id;
                    ri.MainWindow = MainWindowOf((uint)p.Id);
                    list.Add(ri);
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return list;
        }

        /// <summary>
        /// The instance worth reusing: the one already showing a main window.
        /// Returns null when nothing is far enough along to focus - an instance
        /// that is still loading, or one parked on the database error, is not
        /// something to hand the user.
        /// </summary>
        public static RunningInstance FindUsable()
        {
            foreach (RunningInstance ri in Snapshot())
                if (ri.HasMainWindow && !SessionHealth.HasDatabaseErrorDialog((uint)ri.Pid)) return ri;
            return null;
        }

        /// <summary>True when something is running, usable or not.</summary>
        public static bool AnyRunning()
        {
            return Count() > 0;
        }

        /// <summary>Bring an instance's main window to the front.</summary>
        public static bool Focus(RunningInstance ri)
        {
            if (ri == null) return false;
            IntPtr h = ri.MainWindow;
            if (h == IntPtr.Zero) h = MainWindowOf((uint)ri.Pid);
            if (h == IntPtr.Zero) return false;
            return Win32.FocusWindow(h);
        }
    }
}
