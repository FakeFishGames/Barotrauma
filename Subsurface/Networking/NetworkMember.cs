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

        protected TimeSpan updateInterval;
        protected DateTime updateTimer;

        protected bool gameStarted;
        
        public void AddChatMessage(string message, ChatMessageType messageType)
        {
            Game1.netLobbyScreen.NewChatMessage(message, messageColor[(int)messageType]);
            if (Game1.gameSession != null) Game1.gameSession.NewChatMessage(message, messageColor[(int)messageType]);

            GUI.PlayMessageSound();
        }
    }

    enum ChatMessageType
    {
        Default, Admin, Dead, Server
    }
}
