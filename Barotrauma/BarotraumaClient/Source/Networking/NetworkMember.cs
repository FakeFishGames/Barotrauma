using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma.Networking
{
    //TODO: remove hard-coded texts in this class
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
            inGameHUD = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                CanBeFocused = false
            };

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
            
            inGameHUD.DrawManually(spriteBatch);

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

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.6f), banReasonPrompt.Children[0].RectTransform, Anchor.Center));
            var banReasonBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform))
            {
                Wrap = true,
                MaxTextLength = 100
            };

            GUINumberInput durationInputDays = null, durationInputHours = null;
            GUITickBox permaBanTickBox = null;

            if (ban)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform), "Duration:");
                permaBanTickBox = new GUITickBox(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform), "Permanent")
                {
                    Selected = true
                };

                var durationContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform), isHorizontal: true)
                {
                    Visible = false
                };

                permaBanTickBox.OnSelected += (tickBox) =>
                {
                    durationContainer.Visible = !tickBox.Selected;
                    return true;
                };
                
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f),durationContainer.RectTransform),"Days:");
                durationInputDays = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 1000
                };

                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), "Hours:");
                durationInputHours = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 24
                };
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
