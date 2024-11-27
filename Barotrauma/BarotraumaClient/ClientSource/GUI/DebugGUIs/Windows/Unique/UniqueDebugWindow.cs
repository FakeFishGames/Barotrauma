using System;
using System.Collections.Generic;

namespace Barotrauma
{
    internal abstract class UniqueDebugWindow<T> : DebugWindow
    {
        private static readonly Dictionary<Type, UniqueDebugWindow<T>> AllWindows = new();

        protected UniqueDebugWindow(bool createRefreshButton = false) : base(createRefreshButton)
        {
            AllWindows.Add(typeof(T), this);
        }

        protected static bool WindowExists()
        {
            if (AllWindows.TryGetValue(typeof(T), out UniqueDebugWindow<T> window))
            {
                window.Frame.Flash(GUIStyle.Green);
                return true;
            }
            return false;
        }

        protected override void Close()
        {
            AllWindows.Remove(typeof(T));
            base.Close();
        }
    }
}
