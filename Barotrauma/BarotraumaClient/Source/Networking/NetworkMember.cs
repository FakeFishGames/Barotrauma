using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

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
        protected ChatBox chatBox;

        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }

        private void InitProjSpecific()
        {
            inGameHUD = new GUIFrame(new Rectangle(0, 0, 0, 0), null, null);
            inGameHUD.CanBeFocused = false;

            chatBox = new ChatBox(inGameHUD, false);
            chatBox.OnEnterMessage += EnterChatMessage;
            chatBox.OnTextChanged += TypingChatMessage;
        }

        protected void SetRadioButtonColor()
        {
            var radioItem = Character.Controlled?.Inventory?.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
            chatBox.RadioButton.GetChild<GUIImage>().Color =
                (radioItem != null && Character.Controlled.HasEquippedItem(radioItem) && radioItem.GetComponent<WifiComponent>().CanTransmit()) ?
                Color.White : new Color(60, 60, 60, 255);
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
                    if (command != "") //PMing
                        textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Private];
                    else
                        textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];
                    break;
            }

            return true;
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

            if (string.IsNullOrWhiteSpace(message))
            {
                if (textBox == chatBox.InputBox) textBox.Deselect();
                return false;
            }

            if (this == GameMain.Server)
            {
                GameMain.Server.SendChatMessage(message, null, null);
            }
            else if (this == GameMain.Client)
            {
                GameMain.Client.SendChatMessage(message);
            }

            if (textBox == chatBox.InputBox) textBox.Deselect();

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
            if (!gameStarted || Screen.Selected != GameMain.GameScreen || GUI.DisableHUD) return;

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
                    respawnInfo = respawnManager.UsingShuttle ? "Respawn Shuttle dispatching in " : "Respawning players in ";
                    respawnInfo = respawnManager.RespawnTimer <= 0.0f ? "" : respawnInfo + ToolBox.SecondsToReadableTime(respawnManager.RespawnTimer);
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

        public virtual bool SelectCrewCharacter(Character character, GUIComponent characterFrame)
        {
            return false;
        }
        
        public void CreateKickReasonPrompt(string clientName, bool ban, bool rangeBan = false)
        {
            var banReasonPrompt = new GUIMessageBox(ban ? "Reason for the ban?" : "Reason for kicking?", "", new string[] { "OK", "Cancel" }, 400, 300);
            var banReasonBox = new GUITextBox(new Rectangle(0, 30, 0, 50), Alignment.TopCenter, "", banReasonPrompt.children[0]);
            banReasonBox.Wrap = true;
            banReasonBox.MaxTextLength = 100;

            GUINumberInput durationInputDays = null, durationInputHours = null;
            GUITickBox permaBanTickBox = null;

            if (ban)
            {
                new GUITextBlock(new Rectangle(0, 80, 0, 0), "Duration:", "", banReasonPrompt.children[0]);
                permaBanTickBox = new GUITickBox(new Rectangle(0, 110, 15, 15), "Permanent", Alignment.TopLeft, banReasonPrompt.children[0]);
                permaBanTickBox.Selected = true;

                var durationContainer = new GUIFrame(new Rectangle(0, 130, 0, 40), null, banReasonPrompt.children[0]);
                durationContainer.Visible = false;

                permaBanTickBox.OnSelected += (tickBox) =>
                {
                    durationContainer.Visible = !tickBox.Selected;
                    return true;
                };
                
                new GUITextBlock(new Rectangle(0, 0, 30, 20), "Days:", "", Alignment.TopLeft, Alignment.CenterLeft, durationContainer);
                durationInputDays = new GUINumberInput(new Rectangle(40, 0, 50, 20), "", GUINumberInput.NumberType.Int, durationContainer);
                durationInputDays.MinValueInt = 0;
                durationInputDays.MaxValueFloat = 1000;

                new GUITextBlock(new Rectangle(100, 0, 30, 20), "Hours:", "", Alignment.TopLeft, Alignment.CenterLeft, durationContainer);
                durationInputHours = new GUINumberInput(new Rectangle(150, 0, 50, 20), "", GUINumberInput.NumberType.Int, durationContainer);
                durationInputDays.MinValueInt = 0;
                durationInputDays.MaxValueFloat = 24;
            }

            banReasonPrompt.Buttons[0].OnClicked += (btn, userData) =>
            {
                if (ban)
                {
                    if (!permaBanTickBox.Selected)
                    {
                        TimeSpan banDuration = new TimeSpan(durationInputDays.IntValue, durationInputHours.IntValue, 0, 0);
                        BanPlayer(clientName, banReasonBox.Text, ban, banDuration);
                    }
                    else
                    {
                        BanPlayer(clientName, banReasonBox.Text, ban);
                    }
                }
                else
                {
                    KickPlayer(clientName, banReasonBox.Text);
                }
                return true;
            };
            banReasonPrompt.Buttons[0].OnClicked += banReasonPrompt.Close;
            banReasonPrompt.Buttons[1].OnClicked += banReasonPrompt.Close;
        }
    }
}
