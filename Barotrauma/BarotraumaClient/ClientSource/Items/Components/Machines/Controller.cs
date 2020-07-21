using Barotrauma.Networking;
using Microsoft.Xna.Framework.Graphics;
using System.ComponentModel;

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
                crewAreaOriginalState = crewManager.ToggleCrewListOpen;
            }
            crewManager.ToggleCrewListOpen = value;
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

#if DEBUG
        public override void CreateEditingHUD(SerializableEntityEditor editor)
        {
            base.CreateEditingHUD(editor);

            foreach (LimbPos limbPos in limbPositions)
            {
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(limbPos);

                PropertyDescriptor limbPosProperty = properties.Find("Position", false);
                editor.CreateVector2Field(limbPos, new SerializableProperty(limbPosProperty), limbPos.Position, limbPos.LimbType.ToString(), "");
            }
        }
#endif

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            State = msg.ReadBoolean();
            ushort userID = msg.ReadUInt16();
            if (userID == 0)
            {
                if (user != null)
                {
                    IsActive = false;
                    CancelUsing(user);
                    user = null;
                }
            }
            else
            {
                Character newUser = Entity.FindEntityByID(userID) as Character;
                if (newUser != user)
                {
                    CancelUsing(user);
                }
                user = newUser;
                IsActive = true;
            }
        }
    }
}
