using System;
using ImeSharp.Native;

namespace ImeSharp
{
    /// <summary>
    ///     Contains properties that are queries into the system's various settings.
    /// </summary>
    internal sealed class SafeSystemMetrics
    {

        private SafeSystemMetrics()
        {
        }

        /// <summary>
        ///     Maps to SM_CXDOUBLECLK
        /// </summary>
        public static int DoubleClickDeltaX
        {
            get { return NativeMethods.GetSystemMetrics(SM.CXDOUBLECLK); }
        }

        /// <summary>
        ///     Maps to SM_CYDOUBLECLK
        /// </summary>
        public static int DoubleClickDeltaY
        {
            get { return NativeMethods.GetSystemMetrics(SM.CYDOUBLECLK); }
        }


        /// <summary>
        ///     Maps to SM_CXDRAG
        /// </summary>
        public static int DragDeltaX
        {
            get { return NativeMethods.GetSystemMetrics(SM.CXDRAG); }
        }

        /// <summary>
        ///     Maps to SM_CYDRAG
        /// </summary>
        public static int DragDeltaY
        {
            get { return NativeMethods.GetSystemMetrics(SM.CYDRAG); }
        }

        ///<summary> 
        /// Is an IMM enabled ? Maps to SM_IMMENABLED
        ///</summary> 
        public static bool IsImmEnabled
        {
            get { return (NativeMethods.GetSystemMetrics(SM.IMMENABLED) != 0); }
        }

    }
}
