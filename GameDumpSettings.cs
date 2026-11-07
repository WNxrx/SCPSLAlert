using System;
using System.IO;
using Microsoft.Win32;

namespace SCPSLAlert
{
    internal static class GameDumpSettings
    {
        private const string KeyPath = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\SCPSL.exe";

        public static string DumpFolder
        {
            get { return Path.Combine(CrashPaths.GameCrash, "dump"); }
        }

        public static bool Available
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath))
                        return key != null;
                }
                catch { return false; }
            }
        }

        public static bool Enable()
        {
            try
            {
                string folder = DumpFolder;
                CrashPaths.Dir(folder);
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath))
                {
                    if (key == null) return false;
                    key.SetValue("DumpFolder", folder, RegistryValueKind.ExpandString);
                    key.SetValue("DumpCount", 5, RegistryValueKind.DWord);
                    key.SetValue("DumpType", 1, RegistryValueKind.DWord);
                }
                return true;
            }
            catch { return false; }
        }

        public static void Disable()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps", true))
                {
                    if (key != null) key.DeleteSubKeyTree("SCPSL.exe", false);
                }
            }
            catch { }
        }
    }
}
