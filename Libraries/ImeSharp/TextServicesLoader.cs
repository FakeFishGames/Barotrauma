using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;
using ImeSharp.Native;
using TsfSharp;

namespace ImeSharp
{
    // Creates ITfThreadMgr instances, the root object of the Text Services
    // Framework.
    internal class TextServicesLoader
    {
        public static readonly Guid CLSID_TF_ThreadMgr = new Guid("529a9e6b-6587-4f23-ab9e-9c7d683e3c50");
        public static readonly Guid IID_ITfThreadMgr = new Guid("aa80e801-2021-11d2-93e0-0060b067b86e");
        public static readonly Guid IID_ITfThreadMgrEx = new Guid("3e90ade3-7594-4cb0-bb58-69628f5f458c");
        public static readonly Guid IID_ITfThreadMgr2 = new Guid("0AB198EF-6477-4EE8-8812-6780EDB82D5E");

        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------

        #region Constructors

        // Private ctor to prevent anyone from instantiating this static class.
        private TextServicesLoader() { }

        #endregion Constructors

        //------------------------------------------------------
        //
        //  public Properties
        //
        //------------------------------------------------------

        #region public Properties

        /// <summary>
        /// Loads an instance of the Text Services Framework.
        /// </summary>
        /// <returns>
        /// May return null if no text services are available.
        /// </returns>
        public static ITfThreadMgrEx Load()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                Debug.WriteLine("CRASH: ImeSharp won't work on MTA thread!!!");

            if (ServicesInstalled)
            {
                // NB: a COMException here means something went wrong initialzing Cicero.
                // Cicero will throw an exception if it doesn't think it should have been
                // loaded (no TIPs to run), you can check that in msctf.dll's NoTipsInstalled
                // which lives in nt\windows\advcore\ctf\lib\immxutil.cpp.  If that's the
                // problem, ServicesInstalled is out of sync with Cicero's thinking.
                IntPtr ret;
                var hr = NativeMethods.CoCreateInstance(CLSID_TF_ThreadMgr,
                    IntPtr.Zero,
                    NativeMethods.CLSCTX_INPROC_SERVER,
                    IID_ITfThreadMgrEx, out ret);

                if (hr == NativeMethods.S_OK)
                    return new ITfThreadMgrEx(ret);
            }

            return null;
        }

        /// <summary>
        /// return true if current OS version is Windows 7 or below.
        /// </summary>
        public static bool IsWindows7OrBelow()
        {
            if (Environment.OSVersion.Version.Major <= 5)
                return true;

            if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor <= 1)
                return true;

            return false;
        }

        /// <summary>
        /// Informs the caller if text services are installed for the current user.
        /// </summary>
        /// <returns>
        /// true if one or more text services are installed for the current user, otherwise false.
        /// </returns>
        /// <remarks>
        /// If this method returns false, TextServicesLoader.Load is guarenteed to return null.
        /// Callers can use this information to avoid overhead that would otherwise be
        /// required to support text services.
        /// </remarks>
        public static bool ServicesInstalled
        {
            get
            {
                lock (s_servicesInstalledLock)
                {
                    if (s_servicesInstalled == InstallState.Unknown)
                    {
                        s_servicesInstalled = TIPsWantToRun() ? InstallState.Installed : InstallState.NotInstalled;
                    }
                }

                return (s_servicesInstalled == InstallState.Installed);
            }
        }

        #endregion public Properties

        //------------------------------------------------------
        //
        //  public Events
        //
        //------------------------------------------------------

        //------------------------------------------------------
        //
        //  Private Methods
        //
        //------------------------------------------------------

        #region Private Methods

        //
        // This method tries to stop Avalon from loading Cicero when there are no TIPs to run.
        // The perf tradeoff is a typically small number of registry checks versus loading and
        // initializing cicero.
        //
        // The Algorithm:
        //
        // Do a quick check vs. the global disable flag, return false if it is set.
        // For each key under HKLM\SOFTWARE\Microsoft\CTF\TIP (a TIP or category clsid)
        //  If the the key has a LanguageProfile subkey (it's a TIP clsid)
        //      Iterate under the matching TIP entry in HKCU.
        //          For each key under the LanguageProfile (a particular LANGID)
        //              For each key under the LANGID (an assembly GUID)
        //                  Try to read the Enable value.
        //                  If the value is set non-zero, then stop all processing and return true.
        //                  If the value is set zero, continue.
        //                  If the value does not exist, continue (default is disabled).
        //      If any Enable values were found under HKCU for the TIP, then stop all processing and return false.
        //      Else, no Enable values have been found thus far and we keep going to investigate HKLM.
        //      Iterate under the TIP entry in HKLM.
        //          For each key under the LanguageProfile (a particular LANGID)
        //              For each key under the LANGID (an assembly GUID)
        //                  Try to read the Enable value.
        //                  If the value is set non-zero, then stop all processing and return true.
        //                  If the value does not exist, then stop all processing and return true (default is enabled).
        //                  If the value is set zero, continue.
        // If we finish iterating all entries under HKLM without returning true, return false.
        //

        private static bool TIPsWantToRun()
        {
            object obj;
            RegistryKey key;
            bool tipsWantToRun = false;

            key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\CTF", false);

            // Is cicero disabled completely for the current user?
            if (key != null)
            {
                obj = key.GetValue("Disable Thread Input Manager");

                if (obj is int && (int)obj != 0)
                    return false;
            }

            // Loop through all the TIP entries for machine and current user.
            tipsWantToRun = IterateSubKeys(Registry.LocalMachine, "SOFTWARE\\Microsoft\\CTF\\TIP", new IterateHandler(SingleTIPWantsToRun), true) == EnableState.Enabled;

            return tipsWantToRun;
        }

        // Returns EnableState.Enabled if one or more TIPs are installed and
        // enabled for the current user.
        private static EnableState SingleTIPWantsToRun(RegistryKey keyLocalMachine, string subKeyName, bool localMachine)
        {
            EnableState result;

            if (subKeyName.Length != CLSIDLength)
                return EnableState.Disabled;

            // We want subkey\LanguageProfile key.
            // Loop through all the langid entries for TIP.

            // First, check current user.
            result = IterateSubKeys(Registry.CurrentUser, "SOFTWARE\\Microsoft\\CTF\\TIP\\" + subKeyName + "\\LanguageProfile", new IterateHandler(IsLangidEnabled), false);

            // Any explicit value short circuits the process.
            // Otherwise check local machine.
            if (result == EnableState.None || result == EnableState.Error)
            {
                result = IterateSubKeys(keyLocalMachine, subKeyName + "\\LanguageProfile", new IterateHandler(IsLangidEnabled), true);

                if (result == EnableState.None)
                {
                    result = EnableState.Enabled;
                }
            }

            return result;
        }

        // Returns EnableState.Enabled if the supplied subkey is a valid LANGID key with enabled
        // cicero assembly.
        private static EnableState IsLangidEnabled(RegistryKey key, string subKeyName, bool localMachine)
        {
            if (subKeyName.Length != LANGIDLength)
                return EnableState.Error;

            // Loop through all the assembly entries for the langid
            return IterateSubKeys(key, subKeyName, new IterateHandler(IsAssemblyEnabled), localMachine);
        }

        // Returns EnableState.Enabled if the supplied assembly key is enabled.
        private static EnableState IsAssemblyEnabled(RegistryKey key, string subKeyName, bool localMachine)
        {
            RegistryKey subKey;
            object obj;

            if (subKeyName.Length != CLSIDLength)
                return EnableState.Error;

            // Open the local machine assembly key.
            subKey = key.OpenSubKey(subKeyName);

            if (subKey == null)
                return EnableState.Error;

            // Try to read the "Enable" value.
            obj = subKey.GetValue("Enable");

            if (obj is int)
            {
                return ((int)obj == 0) ? EnableState.Disabled : EnableState.Enabled;
            }

            return EnableState.None;
        }

        // Calls the supplied delegate on each of the children of keyBase.
        private static EnableState IterateSubKeys(RegistryKey keyBase, string subKey, IterateHandler handler, bool localMachine)
        {
            RegistryKey key;
            string[] subKeyNames;
            EnableState state;

            key = keyBase.OpenSubKey(subKey, false);

            if (key == null)
                return EnableState.Error;

            subKeyNames = key.GetSubKeyNames();
            state = EnableState.Error;

            foreach (string name in subKeyNames)
            {
                switch (handler(key, name, localMachine))
                {
                    case EnableState.Error:
                        break;
                    case EnableState.None:
                        if (localMachine) // For lm, want to return here right away.
                            return EnableState.None;

                        // For current user, remember that we found no Enable value.
                        if (state == EnableState.Error)
                        {
                            state = EnableState.None;
                        }
                        break;
                    case EnableState.Disabled:
                        state = EnableState.Disabled;
                        break;
                    case EnableState.Enabled:
                        return EnableState.Enabled;
                }
            }

            return state;
        }

        #endregion Private Methods

        //------------------------------------------------------
        //
        //  Private Properties
        //
        //------------------------------------------------------

        //------------------------------------------------------
        //
        //  Private Fields
        //
        //------------------------------------------------------

        #region Private Fields

        // String consts used to validate registry entires.
        private const int CLSIDLength = 38;  // {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
        private const int LANGIDLength = 10; // 0x12345678

        // Status of a TIP assembly.
        private enum EnableState
        {
            Error,      // Invalid entry.
            None,       // No explicit Enable entry on the assembly.
            Enabled,    // Assembly is enabled.
            Disabled    // Assembly is disabled.
        };

        // Callback delegate for the IterateSubKeys method.
        private delegate EnableState IterateHandler(RegistryKey key, string subKeyName, bool localMachine);

        // Install state.
        private enum InstallState
        {
            Unknown,        // Haven't checked to see if any TIPs are installed yet.
            Installed,      // Checked and installed.
            NotInstalled    // Checked and not installed.
        }

        // Cached install state value.
        // Writes are not thread safe, but we don't mind the neglible perf hit
        // of potentially writing it twice.
        private static InstallState s_servicesInstalled = InstallState.Unknown;
        private static object s_servicesInstalledLock = new object();

        #endregion Private Fields
    }
}
