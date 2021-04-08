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
                SubmarinePreview.Close();

                // Make sure the saving indicator is disabled when returning to main menu or lobby
                if (this == GameMain.MainMenuScreen || this == GameMain.NetLobbyScreen)
                {
                    GUI.DisableSavingIndicatorDelayed();
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
