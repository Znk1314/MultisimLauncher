// ============================================================================
//  Autostart.cs - the "preheat at login" checkbox, backed by HKCU\...\Run.
//
//  Design notes
//    * HKCU, not HKLM. Writing here needs no elevation and no installer change,
//      so the checkbox can be a plain in-app toggle rather than something only
//      setup can do. It also means uninstalling does not leave a HKLM entry
//      behind that the user cannot remove.
//    * The startup entry points at the launcher with --preheat, so a login
//      launch behaves differently from a user launch: hidden window, no dialog,
//      straight into the retry loop, and it stays out of the way afterwards.
//    * The value stores the full command line, so it is rewritten whenever the
//      program moves. If a stale path is left over - the user moved the folder -
//      the entry silently stops working; Enabled() checks the path still exists
//      and reports false rather than lying about the current state.
// ============================================================================

using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MultisimLauncher
{
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MultisimLauncher";

        /// <summary>Full path of the running executable.</summary>
        internal static string ExePath()
        {
            try
            {
                // Not GetEntryAssembly().Location: that can report a shadow-copied
                // path in some hosting scenarios. The main module path is what the
                // shell would start.
                return Process.GetCurrentProcess().MainModule.FileName;
            }
            catch
            {
                try { return Assembly.GetEntryAssembly().Location; }
                catch { return null; }
            }
        }

        /// <summary>The command line a login launch should run.</summary>
        internal static string CommandLine()
        {
            string exe = ExePath();
            if (string.IsNullOrEmpty(exe)) return null;
            return "\"" + exe + "\" --preheat";
        }

        /// <summary>
        /// True when the startup entry exists AND still points at an executable
        /// that is there. A stale entry is reported as off, because that is what
        /// it effectively is.
        /// </summary>
        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    string v = k.GetValue(ValueName) as string;
                    if (string.IsNullOrEmpty(v)) return false;

                    string path = ExtractPath(v);
                    if (string.IsNullOrEmpty(path)) return false;
                    if (!System.IO.File.Exists(path)) return false;

                    // Present, but does it still describe this program? If the
                    // launcher was moved, treat it as off so the checkbox shows
                    // the truth instead of a stale tick.
                    string mine = ExePath();
                    return mine != null && string.Equals(path, mine, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        /// <summary>Create, update or remove the startup entry. Returns success.</summary>
        internal static bool SetEnabled(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return false;
                    if (on)
                    {
                        string cmd = CommandLine();
                        if (string.IsNullOrEmpty(cmd)) return false;
                        k.SetValue(ValueName, cmd, RegistryValueKind.String);
                    }
                    else
                    {
                        // DeleteValue on a missing name throws nothing when
                        // throwOnMissingValue is false, which is what we want.
                        k.DeleteValue(ValueName, false);
                    }
                }
                return IsEnabled() == on;
            }
            catch { return false; }
        }

        /// <summary>Pull the quoted executable path out of a Run command line.</summary>
        private static string ExtractPath(string commandLine)
        {
            string s = commandLine.Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) return s.Substring(1, end - 1);
                return null;
            }
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }
    }
}
