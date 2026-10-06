// ============================================================================
//  MultisimLocator / MultisimSession
//  Finds an installed Multisim and all of its component-database folders, and
//  provides the health check that decides whether a launch actually worked.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MultisimLauncher
{
    /// <summary>Where Multisim lives and which database folders belong to it.</summary>
    internal sealed class MultisimInstall
    {
        public string ExePath;
        public string InstallDir;
        public string DatabaseDir;      // ProgramData\...\database
        public string UserDatabaseDir;  // %APPDATA%\...\database
        public string Version;          // e.g. "14.3"
        public string Source;           // how it was found (for the log)

        public bool IsUsable { get { return ExePath != null && File.Exists(ExePath); } }

        /// <summary>Folders that Jet writes .ldb lock files into.</summary>
        public string[] LockDirs
        {
            get
            {
                List<string> dirs = new List<string>();
                if (DatabaseDir != null && Directory.Exists(DatabaseDir)) dirs.Add(DatabaseDir);
                if (UserDatabaseDir != null && Directory.Exists(UserDatabaseDir)) dirs.Add(UserDatabaseDir);
                return dirs.ToArray();
            }
        }

        public override string ToString()
        {
            return (Version ?? "?") + " @ " + (InstallDir ?? "?");
        }
    }

    internal static class MultisimLocator
    {
        // Registry keys that hold the database paths. Note the 32-bit view:
        // Multisim is a 32-bit application, so its settings live in WOW6432Node.
        private static readonly string[] CommonKeys = new string[]
        {
            @"SOFTWARE\WOW6432Node\National Instruments\Circuit Design Suite\14.3\Common",
            @"SOFTWARE\National Instruments\Circuit Design Suite\14.3\Common"
        };

        // Fallback locations, in the order worth trying. Covers the default C:
        // install and the very common case of Multisim installed onto D:.
        private static readonly string[] FallbackPatterns = new string[]
        {
            @"C:\Program Files (x86)\National Instruments\Circuit Design Suite {0}\multisim.exe",
            @"C:\Program Files\National Instruments\Circuit Design Suite {0}\multisim.exe",
            @"D:\Program Files (x86)\National Instruments\Circuit Design Suite {0}\multisim.exe",
            @"D:\Program Files\National Instruments\Circuit Design Suite {0}\multisim.exe",
            @"D:\National Instruments\Circuit Design Suite {0}\multisim.exe",
            @"E:\Program Files (x86)\National Instruments\Circuit Design Suite {0}\multisim.exe",
            @"E:\Program Files\National Instruments\Circuit Design Suite {0}\multisim.exe"
        };

        private static readonly string[] KnownVersions = new string[]
        {
            "15.0", "14.3", "14.2", "14.1", "14.0", "13.0", "12.0"
        };

        /// <summary>Locate an installed Multisim. Returns null when nothing is found.</summary>
        public static MultisimInstall Find()
        {
            // --- 1. registry: the authoritative source, and it also gives us the
            //        exact database folders, which may sit on another drive.
            foreach (string keyPath in CommonKeys)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (k == null) continue;
                        string master = k.GetValue("Database_Master") as string;
                        string corporate = k.GetValue("Database_Corporate") as string;

                        MultisimInstall inst = new MultisimInstall();
                        inst.Version = ExtractVersion(keyPath);
                        inst.DatabaseDir = master != null ? Path.GetDirectoryName(master) : null;
                        if (inst.DatabaseDir == null && corporate != null)
                            inst.DatabaseDir = Path.GetDirectoryName(corporate);

                        string progFiles = k.GetValue("Plugins") as string;
                        if (!string.IsNullOrEmpty(progFiles))
                        {
                            string exe = Path.Combine(progFiles, "multisim.exe");
                            if (File.Exists(exe)) inst.ExePath = exe;
                        }
                        if (inst.ExePath == null && inst.DatabaseDir != null)
                        {
                            // database dir is  <install>\..\..\database ; walk up for the exe
                            string guess = GuessExeFromDatabaseDir(inst.DatabaseDir);
                            if (guess != null) inst.ExePath = guess;
                        }

                        inst.UserDatabaseDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            @"National Instruments\Circuit Design Suite\" + inst.Version + @"\database");

                        if (inst.ExePath != null)
                        {
                            inst.InstallDir = Path.GetDirectoryName(inst.ExePath);
                            inst.Source = "registry " + keyPath;
                            return inst;
                        }
                    }
                }
                catch { }
            }

            // --- 2. fallback: scan plausible install roots. This is what makes
            //        a Multisim installed onto D: work without registry help.
            foreach (string version in KnownVersions)
            {
                foreach (string pattern in FallbackPatterns)
                {
                    string exe = string.Format(pattern, version);
                    if (!File.Exists(exe)) continue;

                    MultisimInstall inst = new MultisimInstall();
                    inst.ExePath = exe;
                    inst.InstallDir = Path.GetDirectoryName(exe);
                    inst.Version = version;
                    inst.Source = "filesystem scan";
                    inst.DatabaseDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        @"National Instruments\Circuit Design Suite\" + version + @"\database");
                    inst.UserDatabaseDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"National Instruments\Circuit Design Suite\" + version + @"\database");
                    return inst;
                }
            }

            // --- 3. last resort: Program Files\database template folder implies a version
            return null;
        }

        private static string ExtractVersion(string keyPath)
        {
            // ...Circuit Design Suite\14.3\Common  ->  14.3
            string[] parts = keyPath.Split('\\');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "Circuit Design Suite") return parts[i + 1];
            }
            return "14.3";
        }

        private static string GuessExeFromDatabaseDir(string databaseDir)
        {
            // <root>\National Instruments\Circuit Design Suite\14.3\database
            // <root>\Program Files (x86)\National Instruments\Circuit Design Suite 14.3\
            try
            {
                DirectoryInfo db = new DirectoryInfo(databaseDir);
                DirectoryInfo versionDir = db.Parent;          // ...\14.3
                if (versionDir == null) return null;
                string version = versionDir.Name;
                DirectoryInfo niDir = versionDir.Parent;       // ...\National Instruments
                if (niDir == null) return null;

                string[] roots = new string[]
                {
                    @"C:\Program Files (x86)", @"C:\Program Files",
                    @"D:\Program Files (x86)", @"D:\Program Files", @"D:\",
                    @"E:\Program Files (x86)", @"E:\Program Files", @"E:\"
                };
                foreach (string root in roots)
                {
                    string exe = Path.Combine(root, @"National Instruments\Circuit Design Suite " + version + @"\multisim.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
            catch { }
            return null;
        }
    }

    // ------------------------------------------------------------------------
    // Session health
    // ------------------------------------------------------------------------
    internal static class SessionHealth
    {
        public const int RequiredLocks = 2;   // MSCOMP_S.ldb + CPCOMP_S.ldb

        /// <summary>Count the Jet lock files that exist right now.</summary>
        public static int CountLockFiles(MultisimInstall inst)
        {
            int n = 0;
            foreach (string dir in inst.LockDirs)
            {
                try
                {
                    n += Directory.GetFiles(dir, "*.ldb").Length;
                }
                catch { }
            }
            return n;
        }

        public static void RemoveLockFiles(MultisimInstall inst)
        {
            foreach (string dir in inst.LockDirs)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*.ldb"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// True when the instance looks healthy: the component databases opened
        /// (lock files present) AND the main window exists.
        /// Both parts matter - the main window appears even on a failed launch,
        /// and lock files alone do not prove the UI came up.
        /// </summary>
        public static bool IsHealthy(uint pid, MultisimInstall inst)
        {
            if (CountLockFiles(inst) < RequiredLocks) return false;
            foreach (IntPtr h in Win32.TopLevelWindows(pid))
            {
                if (!Win32.IsWindowVisible(h)) continue;
                string cls = Win32.ClassOf(h);
                if (cls.StartsWith("Multisim", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the "Problem accessing the database" modal box is on screen.
        /// The message lives in an Edit control that ignores GetWindowText, so
        /// WM_GETTEXT (with a timeout) is required. The marker is assembled from
        /// code points so this source file stays pure ASCII and cannot be broken
        /// by a codepage mismatch in the build environment.
        /// </summary>
        private static readonly string MarkerAccessDb =
            new string(new char[] { (char)0x8BBF, (char)0x95EE, (char)0x6570, (char)0x636E, (char)0x5E93 }); // "access database"

        public static bool HasDatabaseErrorDialog(uint pid)
        {
            foreach (IntPtr h in Win32.TopLevelWindows(pid))
                if (IsDatabaseErrorDialog(h)) return true;
            return false;
        }

        /// <summary>
        /// Whether one specific window is the database error box.
        ///
        /// Split out from HasDatabaseErrorDialog because the launcher has to make
        /// this decision per window while hiding a starting instance: the splash
        /// screen is a dialog too, with the same class and the same title, so
        /// class and title cannot separate them. Only the content can.
        ///
        /// The visible/responsive guards that the enumeration used to apply are
        /// kept here, so both callers behave identically.
        /// </summary>
        public static bool IsDatabaseErrorDialog(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            if (!Win32.IsWindowVisible(h)) return false;
            if (Win32.ClassOf(h) != "#32770") return false;
            if (!Win32.IsResponsive(h)) return false;
            if (Win32.TextOf(h).Trim() != "Multisim") return false;

            foreach (IntPtr c in Win32.ChildWindows(h))
            {
                string t = Win32.MsgTextOf(c, 4096);
                if (t == null) continue;
                if (t.IndexOf(MarkerAccessDb, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}
