using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    enum PacketTypes
    {
        Unknown,

        Login,
        LoggedIn,
        LogOut,

        PlayerJoined,
        PlayerLeft,
        KickedOut,

        StartGame,
        EndGame,

        CharacterInfo,

        Chatmessage,
        UpdateNetLobby,

        NetworkEvent,

        Traitor,

        Vote,
        VoteStatus,

        ResendRequest,
        ReliableMessage,
        LatestMessageID        
    }

    enum VoteType
    {
        Unknown,
        Sub,
        Mode,
        EndRound
    }

    class NetworkMember
    {

        protected static Color[] messageColor = { Color.White, Color.Red, Color.LightBlue, Color.LightGreen };
        
        protected string name;

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        protected GUIFrame inGameHUD;
        protected GUIListBox chatBox;
        protected GUITextBox chatMsgBox;

        public int Port;

        private bool crewFrameOpen;
        private GUIButton crewButton;
        protected GUIFrame crewFrame;

        protected bool gameStarted;

        protected Character myCharacter;
        protected CharacterInfo characterInfo;

        public Voting Voting;

        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set { characterInfo = value; }
        }

        public string Name
        {
            get { return name; }
            set
            {
                if (string.IsNullOrEmpty(name)) return;
                name = value;
            }
        }

        public bool GameStarted
        {
            get { return gameStarted; }
        }

        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }

        public NetworkMember()
        {
            inGameHUD = new GUIFrame(new Rectangle(0,0,0,0), null, null);
            inGameHUD.CanBeFocused = false;

            int width = 350, height = 100;
            chatBox = new GUIListBox(new Rectangle(
                GameMain.GraphicsWidth - 20 - width,
                GameMain.GraphicsHeight - 40 - 25 - height,
                width, height),
                Color.White * 0.5f, GUI.Style, inGameHUD);

            chatMsgBox = new GUITextBox(
                new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 20, chatBox.Rect.Width, 25),
                Color.White * 0.5f, Color.Black, Alignment.TopLeft, Alignment.Left, GUI.Style, inGameHUD);
            chatMsgBox.Font = GUI.SmallFont;
            chatMsgBox.OnEnterPressed = EnterChatMessage;

            crewButton = new GUIButton(new Rectangle(chatBox.Rect.Right-80, chatBox.Rect.Y-30, 80, 20), "Crew", GUI.Style, inGameHUD);
            crewButton.OnClicked = ToggleCrewFrame;

            Voting = new Voting();
        }

        protected void CreateCrewFrame(List<Character> crew)
        {
            int width = 600, height = 400;

            crewFrame = new GUIFrame(new Rectangle(GameMain.GraphicsWidth / 2 - width / 2, GameMain.GraphicsHeight / 2 - height / 2, width, height), GUI.Style);
            crewFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            GUIListBox crewList = new GUIListBox(new Rectangle(0, 0, 280, 300), Color.White * 0.7f, GUI.Style, crewFrame);
            crewList.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
            crewList.OnSelected = SelectCrewCharacter;

            foreach (Character character in crew)
            {
                GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, null, crewList);
                frame.UserData = character;
                frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                frame.Color = (myCharacter == character) ? Color.Gold * 0.2f : Color.Transparent;
                frame.HoverColor = Color.LightGray * 0.5f;
                frame.SelectedColor = Color.Gold * 0.5f;

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    character.Info.Name + " ("+character.Info.Job.Name+")",
                    Color.Transparent, Color.White,
                    Alignment.Left, Alignment.Left,
                    null, frame);
                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                new GUIImage(new Rectangle(-10, 0, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, frame);
            }
            
            var closeButton = new GUIButton(new Rectangle(0,0, 80, 20), "Close", Alignment.BottomCenter, GUI.Style, crewFrame);
            closeButton.OnClicked = ToggleCrewFrame;
        }

        protected virtual bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            Character character = obj as Character;
            if (obj == null) return false;

            GUIComponent existingFrame = crewFrame.FindChild("selectedcharacter");
            if (existingFrame != null) crewFrame.RemoveChild(existingFrame);
            
            var previewPlayer = new GUIFrame(
                new Rectangle(0,0, 230, 300),
                new Color(0.0f, 0.0f, 0.0f, 0.8f), Alignment.TopRight, GUI.Style, crewFrame);
            previewPlayer.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            previewPlayer.UserData = "selectedcharacter";

            var infoFrame = character.Info.CreateInfoFrame(previewPlayer);

            return true;
        }

        private bool ToggleCrewFrame(GUIButton button, object obj)
        {
            crewFrameOpen = !crewFrameOpen;
            return true;
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            SendChatMessage(GameMain.NetworkMember.Name + ": " + message);            

            textBox.Deselect();

            return true;
        }
        
        public void AddChatMessage(string message, ChatMessageType messageType)
        {
            GameMain.NetLobbyScreen.NewChatMessage(message, messageColor[(int)messageType]);

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[1]);
            }

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20), message,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, messageColor[(int)messageType],
                Alignment.Left, null, null, true);
            msg.Font = GUI.SmallFont;

            msg.Padding = new Vector4(20.0f, 0, 0, 0);

            //float prevScroll = chatBox.BarScroll;

            float prevSize = chatBox.BarSize;
            float oldScroll = chatBox.BarScroll;

            msg.Padding = new Vector4(20, 0, 0, 0);
            chatBox.AddChild(msg);

            if ((prevSize == 1.0f && chatBox.BarScroll == 0.0f) || (prevSize < 1.0f && chatBox.BarScroll == 1.0f)) chatBox.BarScroll = 1.0f;

            GUI.PlayMessageSound();
        }

        public virtual void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Server) { }

        public virtual void Update(float deltaTime) 
        {
            if (gameStarted)
            {
                inGameHUD.Update(deltaTime);

                if (crewFrameOpen) crewFrame.Update(deltaTime);

                if (Character.Controlled != null && Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }
            }

            if (PlayerInput.KeyHit(Keys.Tab))
            {
                if (chatMsgBox.Selected)
                {
                    chatMsgBox.Text = "";
                    chatMsgBox.Deselect();
                }
                else
                {
                    chatMsgBox.Select();
                }
            }
        }

        public virtual void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (!gameStarted) return;

            inGameHUD.Draw(spriteBatch);

            if (crewFrameOpen) crewFrame.Draw(spriteBatch);
        }

        public virtual void Disconnect() { }

        protected byte PlayerCountToByte(int playerCount, int maxPlayers)
        {
            byte byteVal = (byte)playerCount;

            byteVal |= (byte)((maxPlayers - 1) << 4);

            return byteVal;
        }

        public static int ByteToPlayerCount(byte byteVal, out int maxPlayers)
        {
            maxPlayers = (byteVal >> 4)+1;

            int playerCount = byteVal & (byte)((1 << 4) - 1);

            return playerCount;
        }

    }

    enum ChatMessageType
    {
        Default, Admin, Dead, Server
    }
}
