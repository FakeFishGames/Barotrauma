using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;

namespace Barotrauma.Networking
{
    enum LogDataTypes
    {
        String = 0,
        Boolean = 1,
        Short = 2,
        Integar = 3,
        Float = 4
    }

    public enum MessageType
    {
        Chat,
        Doorinteraction,
        ItemInteraction,
        Inventory,
        Attack,
        Husk,
        Reactor,
        Set,
        Rewire,
        Spawns,
        Connection,
        ServerMessage,
        Error,
        NilMod
    }

    class LogEntry
    {
        public int logmessagetypeID;
        public string[] Data;
    }

    class logMessageType
    {
        public string Message;
        public List<Enum> DataTypes;

        public logMessageType(string message, List<Enum> datatypes, MessageType messageType)
        {
            Message = message;
            DataTypes = datatypes;
        }
    }

    class ClientLogServer
    {
        private readonly Color[] messageColor =
            {
            /* Old colours
            Color.LightBlue,                //Chat
            new Color(255, 142, 0),         //ItemInteraction
            new Color(238, 208, 0),         //Inventory
            new Color(204, 74, 78),         //Attack
            new Color(163, 73, 164),        //Spawning
            new Color(157, 225, 160),       //ServerMessage
            Color.Red                       //Error
            */

            Color.LightCoral,       //Chat
            Color.White,            //DoorInteraction
            Color.Orange,           //ItemInteraction
            Color.Yellow,           //Inventory
            Color.Red,              //Attack
            Color.MediumPurple,     //Husk
            Color.MediumSeaGreen,   //Reactor
            Color.ForestGreen,      //Set
            Color.LightPink,        //Rewire
            Color.DarkMagenta,      //Spawns
            Color.DarkCyan,         //Connection
            Color.Cyan,             //ServerMessage
            Color.Red,              //Error
            Color.Violet            //NilMod
        };

        private readonly string[] messageTypeName =
        {
            /* Old Message Type Names
            "Chat message",
            "Item interaction",
            "Inventory usage",
            "Attack & death",
            "Spawning",
            "Server message",
            "Error"
            */

            "Chat message",
            "Door interaction",
            "Item interaction",
            "Inventory usage",
            "Attack & death",
            "Husk Infection",
            "Reactor",
            "Powered/Pump set",
            "Wiring",
            "Spawning",
            "Connection Info",
            "Server message",
            "Error",
            "NilMod Extras"
        };

        public List<logMessageType> logMessageTypes;

        public Queue<string> MessageHistory;

        public void AddMessage(int key, string[] data)
        {

        }

        public void InitLogMessages()
        {

        }
    }
}
