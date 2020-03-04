#if WINDOWS_WSA || WINDOWS_UWP
using System;

namespace Foundation.Tasks
{
    public partial class TaskManager
    {
        /// <summary>
        /// Checks if this is the main thread
        /// </summary>
        public static bool IsMainThread
        {
            get { return Environment.CurrentManagedThreadId == MainThread; }
        }

        /// <summary>
        /// The Main Thread
        /// </summary>
        public static int MainThread { get; protected set; }

        /// <summary>
        /// The Current Thread
        /// </summary>
        public static int CurrentThread
        {
            get { return Environment.CurrentManagedThreadId; }
        }
    }
}
#endif
