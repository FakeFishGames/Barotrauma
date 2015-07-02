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
        protected static Color[] messageColor = { Color.Black, Color.DarkRed, Color.DarkBlue, Color.DarkGreen };

        protected string name;

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

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
        
        public void AddChatMessage(string message, ChatMessageType messageType)
        {
            Game1.NetLobbyScreen.NewChatMessage(message, messageColor[(int)messageType]);
            if (Game1.GameSession != null) Game1.GameSession.NewChatMessage(message, messageColor[(int)messageType]);

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
