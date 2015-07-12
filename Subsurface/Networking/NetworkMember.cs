using System;
using Microsoft.Xna.Framework;

namespace Subsurface.Networking
{
    enum PacketTypes
    {
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

        Traitor
    }

    class NetworkMember
    {
        protected static Color[] messageColor = { Color.White, Color.Red, Color.LightBlue, Color.LightGreen };

        protected string name;

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        protected GUIFrame inGameHUD;
        protected GUIListBox chatBox;

        protected bool gameStarted;

        public string Name
        {
            get { return name; }
            set
            {
                if (string.IsNullOrEmpty(name)) return;
                name = value;
            }
        }

        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }

        public NetworkMember()
        {
            inGameHUD = new GUIFrame(new Rectangle(0,0,0,0), null, null);

            int width = 350, height = 100;
            chatBox = new GUIListBox(new Rectangle(
                Game1.GraphicsWidth - 20 - width,
                Game1.GraphicsHeight - 40 - 25 - height,
                width, height),
                Color.White * 0.5f, GUI.style, inGameHUD);

            var textBox = new GUITextBox(
                new Rectangle(chatBox.Rect.X, chatBox.Rect.Y + chatBox.Rect.Height + 20, chatBox.Rect.Width, 25),
                Color.White * 0.5f, Color.Black, Alignment.TopLeft, Alignment.Left, GUI.style, inGameHUD);
            textBox.OnEnter = EnterChatMessage;
        }


        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            SendChatMessage(Game1.NetworkMember.Name + ": " + message);            

            textBox.Deselect();

            return true;
        }
        
        public void AddChatMessage(string message, ChatMessageType messageType)
        {
            Game1.NetLobbyScreen.NewChatMessage(message, messageColor[(int)messageType]);

            GUITextBlock msg = new GUITextBlock(new Rectangle(0, 0, 0, 20), message,
                ((chatBox.CountChildren % 2) == 0) ? Color.Transparent : Color.Black * 0.1f, messageColor[(int)messageType],
                Alignment.Left, null, null, true);

            msg.Padding = new Vector4(20.0f, 0, 0, 0);
            chatBox.AddChild(msg);

            while (chatBox.CountChildren > 20)
            {
                chatBox.RemoveChild(chatBox.children[0]);
            }

            GUI.PlayMessageSound();
        }

        public virtual void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Server) { }

        public virtual void Update() { }

        public virtual void Disconnect() { }

    }

    enum ChatMessageType
    {
        Default, Admin, Dead, Server
    }
}
