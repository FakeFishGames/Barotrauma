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
            if (AllWindows.ContainsKey(obj))
            {
                AllWindows[obj].Frame.Flash(GUIStyle.Green);
                return true;
            }
            return false;
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
            base.Close();
            AllWindows.Remove(FocusedObject);
        }
    }
}
