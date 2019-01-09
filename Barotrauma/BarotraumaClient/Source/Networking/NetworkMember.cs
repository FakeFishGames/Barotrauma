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
        protected GUIButton showLogButton;
        protected GUITickBox cameraFollowsSub;

        private float myCharacterFrameOpenState;
        
        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }

        public ChatBox ChatBox
        {
            get { return chatBox; }
        }

        private void InitProjSpecific()
        {
            inGameHUD = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                CanBeFocused = false
            };

            cameraFollowsSub = new GUITickBox(new RectTransform(new Vector2(0.05f, 0.05f), inGameHUD.RectTransform, anchor: Anchor.TopCenter), "Follow submarine");
            cameraFollowsSub.OnSelected += (tbox) =>
            {
                Camera.FollowSub = tbox.Selected;
                return true;
            };
            cameraFollowsSub.OnSelected(cameraFollowsSub);

            chatBox = new ChatBox(inGameHUD, isSinglePlayer: false);
            chatBox.OnEnterMessage += EnterChatMessage;
            chatBox.InputBox.OnTextChanged += TypingChatMessage;
        }

        protected void SetRadioButtonColor()
        {
            if (Character.Controlled == null || Character.Controlled.SpeechImpediment >= 100.0f)
            {
                chatBox.RadioButton.GetChild<GUIImage>().Color = new Color(60, 60, 60, 255);
            }
            else
            {
                var radioItem = Character.Controlled?.Inventory?.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
                chatBox.RadioButton.GetChild<GUIImage>().Color =
                    (radioItem != null && Character.Controlled.HasEquippedItem(radioItem) && radioItem.GetComponent<WifiComponent>().CanTransmit()) ?
                    Color.White : new Color(60, 60, 60, 255);
            }
        }

        public bool TypingChatMessage(GUITextBox textBox, string text)
        {
            return chatBox.TypingChatMessage(textBox, text);
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

            textBox.Deselect();
            textBox.Text = "";            

            return true;
        }

        public virtual void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;

            if (gameStarted && 
                Screen.Selected == GameMain.GameScreen)
            {
                inGameHUD.AddToGUIUpdateList();

                if (Character.Controlled == null)
                {
                    GameMain.NetLobbyScreen.MyCharacterFrame.AddToGUIUpdateList();
                }
            }
        }

        public void UpdateHUD(float deltaTime)
        {
            GUITextBox msgBox = (Screen.Selected == GameMain.GameScreen ? chatBox.InputBox : GameMain.NetLobbyScreen.TextBox);
            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                if (!GUI.DisableHUD)
                {
                    inGameHUD.UpdateManually(deltaTime);
                    chatBox.Update(deltaTime);
                    UpdateFollowSubTickBox();

                    if (Character.Controlled == null)
                    {
                        myCharacterFrameOpenState = GameMain.NetLobbyScreen.MyCharacterFrameOpen ? myCharacterFrameOpenState + deltaTime * 5 : myCharacterFrameOpenState - deltaTime * 5;
                        myCharacterFrameOpenState = MathHelper.Clamp(myCharacterFrameOpenState, 0.0f, 1.0f);

                        var myCharFrame = GameMain.NetLobbyScreen.MyCharacterFrame;
                        int padding = GameMain.GraphicsWidth - myCharFrame.Parent.Rect.Right;

                        myCharFrame.RectTransform.AbsoluteOffset =
                            Vector2.SmoothStep(new Vector2(-myCharFrame.Rect.Width - padding, 0.0f), new Vector2(-padding, 0), myCharacterFrameOpenState).ToPoint();
                    }
                }
                if (Character.Controlled == null || Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }
            }


            //tab doesn't autoselect the chatbox when debug console is open, 
            //because tab is used for autocompleting console commands
            if ((PlayerInput.KeyHit(InputType.Chat) || PlayerInput.KeyHit(InputType.RadioChat)) &&
                !DebugConsole.IsOpen && (Screen.Selected != GameMain.GameScreen || msgBox.Visible))
            {
                if (msgBox.Selected)
                {
                    msgBox.Text = "";
                    msgBox.Deselect();
                }
                else
                {
                    msgBox.Select();
                    if (Screen.Selected == GameMain.GameScreen && PlayerInput.KeyHit(InputType.RadioChat))
                    {
                        msgBox.Text = "r; ";
                    }
                }
            }
            if (ServerLog.LogFrame != null) ServerLog.LogFrame.AddToGUIUpdateList();
        }

        protected void UpdateFollowSubTickBox()
        {
            if (Character.Controlled == null) 
            {
                //player is in spectating mode
                if (cameraFollowsSub.Visible)
                    return;
                cameraFollowsSub.Visible = true;
            }
            else
            {
                if (!cameraFollowsSub.Visible)
                    return;
                cameraFollowsSub.Visible = false;
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

                if (respawnManager != null)
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

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.6f), banReasonPrompt.InnerFrame.RectTransform, Anchor.Center));
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
