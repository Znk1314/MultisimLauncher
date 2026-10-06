// ============================================================================
//  TestAutostart.cs - dev tool (not shipped).
//
//  Exercises Autostart.SetEnabled/IsEnabled against the real HKCU Run key and
//  restores whatever was there before it ran.
//
//  Why a separate harness: the checkbox is only reachable through the GUI, and
//  the interesting cases - a stale entry from a moved install, a failed write -
//  cannot be produced by clicking. This runs them directly.
//
//  Usage:  TestAutostart.exe
// ============================================================================

using System;
using Microsoft.Win32;

// Same namespace as Autostart so this harness can reach it: the class is
// internal to the launcher and deliberately not public.
namespace MultisimLauncher
{
    internal static class TestAutostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MultisimLauncher";

        private static int Main()
        {
            int failures = 0;

            // ---- preserve the existing state ------------------------------
            string original = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    if (k != null) original = k.GetValue(ValueName) as string;
            }
            catch { }
            Console.WriteLine("original value: " + (original == null ? "(none)" : "'" + original + "'"));
            Console.WriteLine();

            try
            {
                // ---- 1. off by default ------------------------------------
                Autostart.SetEnabled(false);
                failures += Check("disabled reads back as false", Autostart.IsEnabled() == false);

                // ---- 2. enable writes a usable command line ---------------
                bool ok = Autostart.SetEnabled(true);
                failures += Check("SetEnabled(true) reports success", ok);
                failures += Check("enabled reads back as true", Autostart.IsEnabled() == true);

                string written = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    if (k != null) written = k.GetValue(ValueName) as string;
                Console.WriteLine("  written: '" + written + "'");
                failures += Check("value ends with --preheat",
                    written != null && written.EndsWith("--preheat", StringComparison.OrdinalIgnoreCase));
                failures += Check("value quotes the exe path",
                    written != null && written.StartsWith("\"", StringComparison.Ordinal));

                // ---- 3. a stale path must read as OFF ---------------------
                // This is the case that would otherwise leave a tick showing
                // while nothing happens at login.
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                    k.SetValue(ValueName, "\"C:\\nowhere\\gone\\MultisimLauncher.exe\" --preheat");
                failures += Check("stale path reads back as false (not a lying tick)",
                    Autostart.IsEnabled() == false);

                // ---- 4. disabling removes the value -----------------------
                Autostart.SetEnabled(false);
                failures += Check("disabled reads back as false again", Autostart.IsEnabled() == false);
                string after = null;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    if (k != null) after = k.GetValue(ValueName) as string;
                failures += Check("value removed from the registry", after == null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION: " + ex);
                failures++;
            }
            finally
            {
                // ---- put back what was there -------------------------------
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (original == null) k.DeleteValue(ValueName, false);
                        else k.SetValue(ValueName, original);
                    }
                    Console.WriteLine();
                    Console.WriteLine("restored original state");
                }
                catch (Exception ex) { Console.WriteLine("restore failed: " + ex.Message); }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static int Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
            return ok ? 0 : 1;
        }
    }
}
