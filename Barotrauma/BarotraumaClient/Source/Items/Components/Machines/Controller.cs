using Barotrauma.Networking;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Controller : ItemComponent
    {
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (focusTarget != null && character.ViewTarget == focusTarget)
            {
                foreach (ItemComponent ic in focusTarget.Components)
                {
                    ic.DrawHUD(spriteBatch, character);
                }
            }
        }

        private bool crewAreaOriginalState;
        private bool chatBoxOriginalState;
        private bool isHUDsHidden;

        partial void HideHUDs(bool value)
        {
            if (isHUDsHidden == value) { return; }
            if (value == true)
            {
                ToggleCrewArea(false, storeOriginalState: true);
                ToggleChatBox(false, storeOriginalState: true);
            }
            else
            {
                ToggleCrewArea(crewAreaOriginalState, storeOriginalState: false);
                ToggleChatBox(chatBoxOriginalState, storeOriginalState: false);
            }
            isHUDsHidden = value;
        }

        private void ToggleCrewArea(bool value, bool storeOriginalState)
        {
            var crewManager = GameMain.GameSession?.CrewManager;
            if (crewManager == null) { return; }

            if (storeOriginalState)
            {
                crewAreaOriginalState = crewManager.ToggleCrewAreaOpen;
            }
            crewManager.ToggleCrewAreaOpen = value;
        }

        private void ToggleChatBox(bool value, bool storeOriginalState)
        {
            var crewManager = GameMain.GameSession?.CrewManager;
            if (crewManager == null) { return; }

            if (crewManager.IsSinglePlayer)
            {
                if (crewManager.ChatBox != null)
                {
                    if (storeOriginalState)
                    {
                        chatBoxOriginalState = crewManager.ChatBox.ToggleOpen;
                    }
                    crewManager.ChatBox.ToggleOpen = value;
                }
            }
            else if (GameMain.Client != null)
            {
                if (storeOriginalState)
                {
                    chatBoxOriginalState = GameMain.Client.ChatBox.ToggleOpen;
                }
                GameMain.Client.ChatBox.ToggleOpen = value;
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            state = msg.ReadBoolean();
        }
    }
}
