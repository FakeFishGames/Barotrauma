using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma.ClientSource.Screens.PopUpDialogs
{
    /// <summary>
    /// Base class for creating pop-up dialogs which block out the background. This class contains 
    /// logic for keeping track of created dialogs for the purpose of GUI update addition.
    /// </summary>
    class PopUpDialog
    {
        private static readonly List<PopUpDialog> activeDialogs = new List<PopUpDialog>();

        public static void AddActiveDialogsToGUIUpdateList()
        {
            foreach (PopUpDialog dialog in activeDialogs)
            {
                dialog.RootFrame.AddToGUIUpdateList();
            }
        }

        public delegate void OnDialogClosedHandler();
        public OnDialogClosedHandler OnDialogClosed;

        protected GUIFrame RootFrame { get; private set; }

        public PopUpDialog()
        {
            RootFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: "GUIBackgroundBlocker");

            new GUIButton(new RectTransform(GUI.Canvas.RelativeSize, RootFrame.RectTransform, Anchor.Center), style: null)
            {
                OnClicked = (btn, userdata) => { if (GUI.MouseOn == btn || GUI.MouseOn == btn.TextBlock) CloseDialog(); return true; }
            };

            activeDialogs.Add(this);
        }

        public void CloseDialog()
        {
            if(RootFrame != null)
            {
                activeDialogs.Remove(this);
                // Since we are based off GUI.Canvas, which is a RectTransform, we don't have a direct Parent.
                // However, we should clear the parent of the rect transform (which removes us as a child) so 
                // that the frame can be garbage collected.
                RootFrame.RectTransform.Parent = null;
                RootFrame = null;
                OnDialogClosed?.Invoke();
            }
        }
    }
}
