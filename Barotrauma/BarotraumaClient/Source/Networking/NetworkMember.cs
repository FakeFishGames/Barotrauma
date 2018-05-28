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
                GUI.DrawString(spriteBatch, new Vector2(GameMain.GraphicsWidth - 180.0f, 40),
                    TextManager.Get("EndRoundVotes").Replace("[y]", EndVoteCount.ToString()).Replace("[n]", (EndVoteMax - EndVoteCount).ToString()),
                    Color.White,
                    font: GUI.SmallFont);
            }

            if (respawnManager != null)
            {
                string respawnInfo = "";

                if (respawnManager.CurrentState == RespawnManager.State.Waiting &&
                    respawnManager.CountdownStarted)
                {
                    respawnInfo = TextManager.Get(respawnManager.UsingShuttle ? "RespawnShuttleDispatching" : "RespawningIn");
                    respawnInfo = respawnInfo.Replace("[time]", ToolBox.SecondsToReadableTime(respawnManager.RespawnTimer));
                }
                else if (respawnManager.CurrentState == RespawnManager.State.Transporting)
                {
                    respawnInfo = respawnManager.TransportTimer <= 0.0f ? 
                        "" : 
                        TextManager.Get("RespawnShuttleLeavingIn").Replace("[time]", ToolBox.SecondsToReadableTime(respawnManager.TransportTimer));
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
            var banReasonPrompt = new GUIMessageBox(
                TextManager.Get(ban ? "BanReasonPrompt" : "KickReasonPrompt"),
                "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") }, 400, 300);

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.6f), banReasonPrompt.Children[0].RectTransform, Anchor.Center));
            var banReasonBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform))
            {
                Wrap = true,
                MaxTextLength = 100
            };

            GUINumberInput durationInputDays = null, durationInputHours = null;
            GUITickBox permaBanTickBox = null;

            if (ban)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform), TextManager.Get("BanDuration"));
                permaBanTickBox = new GUITickBox(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform) { RelativeOffset = new Vector2(0.05f, 0.0f) }, 
                    TextManager.Get("BanPermanent"))
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

                durationInputDays = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 1000
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), TextManager.Get("Days"));
                durationInputHours = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 24
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), TextManager.Get("Hours"));
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
