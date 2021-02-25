namespace Barotrauma
{
    partial class Screen
    {
        private static Screen selected;
        
        public static Screen Selected
        {
            get { return selected; }
        }

        public static void SelectNull()
        {
            selected = null;
        }

        public virtual void Deselect()
        {
        }

        public virtual void Select()
        {
            if (selected != null && selected != this)
            {
                selected.Deselect();
#if CLIENT
                GUIContextMenu.CurrentContextMenu = null;
                GUI.ClearCursorWait();
                //make sure any textbox in the previously selected screen doesn't stay selected
                if (GUI.KeyboardDispatcher.Subscriber != DebugConsole.TextBox)
                {
                    GUI.KeyboardDispatcher.Subscriber = null;
                    GUI.ScreenChanged = true;
                }
#endif
            }
            selected = this;
        }

        public virtual Camera Cam
        {
            get { return null; }
        }
        
        public virtual void Update(double deltaTime)
        {
        }
    }
}
