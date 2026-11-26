using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;

namespace Barotrauma.Items.Components
{
    partial class Controller : ItemComponent
    {
        private bool isHUDsHidden;

        public void UpdateMsg()
        {
            if (Character.Controlled == null) { return; }

            if (!string.IsNullOrEmpty(KickOutCharacterMsg) &&
                SelectingKicksCharacterOut &&
                User != null && !User.Removed)
            {
                DisplayMsg = TextManager.ParseInputTypes(TextManager.Get(KickOutCharacterMsg));
            }
            else if (!string.IsNullOrEmpty(PutOtherCharacterMsg) &&
                AllowPuttingInOtherCharacters &&
                CanPutSelectedCharacterInside(Character.Controlled.SelectedCharacter))
            {
                DisplayMsg = TextManager.ParseInputTypes(TextManager.Get(PutOtherCharacterMsg));
            }
            else
            {
                DisplayMsg = TextManager.ParseInputTypes(TextManager.Get(Msg)).Fallback(Msg);
            }

            CharacterHUD.RecreateHudTextsIfControlling(Character.Controlled);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            base.DrawHUD(spriteBatch, character);
            if (focusTarget != null && character.ViewTarget == focusTarget)
            {
                foreach (ItemComponent ic in focusTarget.Components)
                {
                    if (ic.ShouldDrawHUD(character))
                    {
                        ic.DrawHUD(spriteBatch, character);
                    }
                }

                bool noSignal = focusTarget?.GetComponent<Turret>() is { } turret &&
                                (turret.User != character || !turret.IsBeingActivelyUsed);

                if (noSignal)
                {
                    LocalizedString text = TextManager.Get("sonarnosignal");
                    GUIFont font = GUIStyle.LargeFont;
                    Vector2 textSize = font.MeasureString(text);

                    Vector2 pos = new Vector2(GameMain.GraphicsWidth / 2f, GameMain.GraphicsHeight * 0.3f) - textSize / 2f;

                    GUI.DrawString(spriteBatch, pos, text, GUIStyle.Red,
                                   backgroundColor: Color.Black * 0.66f,
                                   backgroundPadding: GUI.IntScale(10),
                                   font: font);
                }
            }
        }

        public override void AddToGUIUpdateList(int order = 0)
        {
            base.AddToGUIUpdateList(order);
            if (focusTarget != null && Character.Controlled.ViewTarget == focusTarget)
            {
                focusTarget.AddToGUIUpdateList(order);
            }
        }

        partial void HideHUDs(bool value)
        {
            if (isHUDsHidden == value) { return; }
            if (value)
            {
                GameMain.GameSession?.CrewManager?.AutoHideCrewList();
                ChatBox.AutoHideChatBox();
            }
            else
            {
                GameMain.GameSession?.CrewManager?.ResetCrewListOpenState();
                ChatBox.ResetChatBoxOpenState();
            }
            isHUDsHidden = value;
        }

#if DEBUG
        public override void CreateEditingHUD(SerializableEntityEditor editor)
        {
            base.CreateEditingHUD(editor);

            foreach (LimbPos limbPos in limbPositions)
            {
                PropertyInfo[] properties = limbPos.GetType().GetProperties();
                PropertyInfo limbPosProperty = properties.Find(nameof(LimbPos.Position), ignoreCase: false);
                editor.CreateVector2Field(limbPos, new SerializableProperty(limbPosProperty), limbPos.Position, limbPos.LimbType.ToString(), "");
            }
        }
#endif

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            State = msg.ReadBoolean();
            ushort userID = msg.ReadUInt16();
            if (userID == 0)
            {
                if (User != null)
                {
                    IsActive = false;
                    CancelUsing(User);
                    User = null;
                }
            }
            else
            {
                Character newUser = Entity.FindEntityByID(userID) as Character;
                if (newUser != User)
                {
                    CancelUsing(User);
                }
                User = newUser;

                // If the server assigned a user to this controller but the character is not selecting the item
                // on the client-side, force the selection to prevent desync. This is required for force attaching,
                // since the character placed into the controller may be unconscious, and in that state
                // the server no longer syncs the current SelectedItem to clients.
                if (ForceUserToStayAttached && 
                    user != null && 
                    !user.IsAnySelectedItem(Item))
                {
                    user.SelectedItem = Item;
                }

                IsActive = true;
            }
        }
    }
}
