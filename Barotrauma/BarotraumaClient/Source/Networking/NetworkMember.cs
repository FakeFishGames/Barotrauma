using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Lidgren.Network;
using Barotrauma.Items.Components;

namespace Barotrauma.Networking
{
    abstract partial class NetworkMember
    {
        protected CharacterInfo characterInfo;

        protected Character myCharacter;

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set { characterInfo = value; }
        }

        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        protected GUIFrame inGameHUD;
        protected GUIListBox chatBox;
        protected GUITextBox chatMsgBox;

        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }
        
        private void InitProjSpecific()
        {
            inGameHUD = new GUIFrame(new Rectangle(0, 0, 0, 0), null, null);
            inGameHUD.CanBeFocused = false;

            int width = (int)MathHelper.Clamp(GameMain.GraphicsWidth * 0.35f, 350, 500);
            int height = (int)MathHelper.Clamp(GameMain.GraphicsHeight * 0.15f, 100, 200);
            chatBox = new GUIListBox(new Rectangle(
                GameMain.GraphicsWidth - 20 - width,
                GameMain.GraphicsHeight - 40 - 25 - height,
                width, height),
                Color.White * 0.5f, "", inGameHUD);
            chatBox.Padding = Vector4.Zero;

            chatMsgBox = new GUITextBox(
                new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 20, chatBox.Rect.Width, 25),
                Color.White * 0.5f, Color.Black, Alignment.TopLeft, Alignment.Left, "", inGameHUD);
            chatMsgBox.Font = GUI.SmallFont;
            chatMsgBox.MaxTextLength = ChatMessage.MaxLength;
            chatMsgBox.Padding = Vector4.Zero;
            chatMsgBox.OnEnterPressed = EnterChatMessage;
            chatMsgBox.OnTextChanged = TypingChatMessage;
        }

        public bool TypingChatMessage(GUITextBox textBox, string text)
        {
            string tempStr;
            string command = ChatMessage.GetChatMessageCommand(text, out tempStr);
            switch (command)
            {
                case "r":
                case "radio":
                    textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Radio];
                    break;
                case "d":
                case "dead":
                    textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Dead];
                    break;
                default:
                    textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];
                    break;
            }

            return true;
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

            if (string.IsNullOrWhiteSpace(message)) return false;

            if (this == GameMain.Server)
            {
                GameMain.Server.SendChatMessage(message, null, null);
            }
            else if (this == GameMain.Client)
            {
                GameMain.Client.SendChatMessage(message);
            }

            if (textBox == chatMsgBox) textBox.Deselect();

            return true;
        }

        public virtual void AddToGUIUpdateList()
        {
            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                inGameHUD.AddToGUIUpdateList();
            }
        }

        public virtual void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (!gameStarted || Screen.Selected != GameMain.GameScreen) return;

            GameMain.GameSession.CrewManager.Draw(spriteBatch);

            inGameHUD.Draw(spriteBatch);

            if (EndVoteCount > 0)
            {
                if (GameMain.NetworkMember.myCharacter == null)
                {
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 180.0f, 40),
                        "Votes to end the round (y/n): " + EndVoteCount + "/" + (EndVoteMax - EndVoteCount), Color.White, null, 0, GUI.SmallFont);
                }
                else
                {
                    GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 140.0f, 40),
                        "Votes (y/n): " + EndVoteCount + "/" + (EndVoteMax - EndVoteCount), Color.White, null, 0, GUI.SmallFont);
                }
            }

            if (respawnManager != null)
            {
                string respawnInfo = "";

                if (respawnManager.CurrentState == RespawnManager.State.Waiting &&
                    respawnManager.CountdownStarted)
                {
                    respawnInfo = respawnManager.RespawnTimer <= 0.0f ? "" : "Respawn Shuttle dispatching in " + ToolBox.SecondsToReadableTime(respawnManager.RespawnTimer);

                }
                else if (respawnManager.CurrentState == RespawnManager.State.Transporting)
                {
                    respawnInfo = respawnManager.TransportTimer <= 0.0f ? "" : "Shuttle leaving in " + ToolBox.SecondsToReadableTime(respawnManager.TransportTimer);
                }

                if (!string.IsNullOrEmpty(respawnInfo))
                {
                    GUI.DrawString(spriteBatch,
                        new Vector2(120.0f, 10),
                        respawnInfo, Color.White, null, 0, GUI.SmallFont);
                }

            }
        }

        public virtual bool SelectCrewCharacter(Character character, GUIComponent crewFrame)
        {
            return false;
        }
        
        public void CreateKickReasonPrompt(string clientName, bool ban, bool rangeBan = false)
        {
            var banReasonPrompt = new GUIMessageBox(ban ? "Reason for the ban?" : "Reason for kicking?", "", new string[] { "OK" }, 400, 250);
            var textBox = new GUITextBox(new Rectangle(0, 0, 0, 50), Alignment.Center, "", banReasonPrompt.children[0]);
            textBox.Wrap = true;
            textBox.MaxTextLength = 100;

            banReasonPrompt.Buttons[0].OnClicked += (btn, userData) =>
            {
                KickPlayer(clientName, textBox.Text, ban, rangeBan);
                return true;
            };
            banReasonPrompt.Buttons[0].OnClicked += banReasonPrompt.Close;
        }
    }
}
