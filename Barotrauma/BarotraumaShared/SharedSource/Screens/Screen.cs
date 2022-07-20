namespace Barotrauma
{
    abstract partial class Screen
    {
        public static Screen Selected { get; private set; }

        public static void SelectNull()
        {
            Selected = null;
        }

        public virtual void Deselect()
        {
        }

        public virtual void Select()
        {
            if (Selected != null && Selected != this)
            {
                Selected.Deselect();
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

#if CLIENT
            GUI.SettingsMenuOpen = false;
#endif
            Selected = this;
        }

        public virtual Camera Cam => null;

        public virtual bool IsEditor => false;

        public virtual void Update(double deltaTime)
        {
        }
    }
}
