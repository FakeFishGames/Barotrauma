using System.Collections.Generic;

namespace Barotrauma
{
    internal abstract class ExclusiveDebugWindow<T> : DebugWindow
    {
        protected static readonly Dictionary<T, ExclusiveDebugWindow<T>> AllWindows = new();

        protected readonly T FocusedObject;

        protected ExclusiveDebugWindow(T obj, bool createRefreshButton = false) : base(createRefreshButton)
        {
            FocusedObject = obj;
            AllWindows.Add(obj, this);
        }

        protected static bool WindowExists(T obj)
        {
            if (!AllWindows.TryGetValue(obj, out ExclusiveDebugWindow<T> window)) { return false; }
            window.Frame.Flash(GUIStyle.Green);
            return true;
        }

        protected override void Update()
        {
            if (FocusedObject == null)
            {
                Close();
                return;
            }

            base.Update();
        }

        protected override void Close()
        {
            AllWindows.Remove(FocusedObject);
            base.Close();
        }
    }
}
