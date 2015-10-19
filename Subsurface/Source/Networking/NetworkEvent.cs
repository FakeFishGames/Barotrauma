using System.Collections.Generic;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    enum NetworkEventType
    {
        UpdateEntity = 0,
        KillCharacter = 1,
        UpdateComponent = 2,
        DropItem = 3,
        InventoryUpdate = 4,
        PickItem = 5,
        UpdateProperty = 6,
        WallDamage = 7,
        SelectCharacter = 8
    }

    class NetworkEvent
    {
        public static List<NetworkEvent> events = new List<NetworkEvent>();

        private static bool[] isImportant = { false, true, false, true, true, true, true, true, true };
        private static bool[] overridePrevious = { true, false, true, false, false, false, true, true, true };

        private int id;

        private NetworkEventType eventType;

        private bool isClientEvent;

        private object data;

        //private NetOutgoingMessage message;

        public int ID
        {
            get { return id; }
        }

        public bool IsClient
        {
            get { return isClientEvent; }
        }

        public bool IsImportant
        {
            get { return isImportant[(int)eventType]; }
        }

        public NetworkEventType Type
        { 
            get { return eventType; } 
        }

        public NetworkEvent(int id, bool isClient)
            : this(NetworkEventType.UpdateEntity, id, isClient)
        {
        }

        public NetworkEvent(NetworkEventType type, int id, bool isClient, object data = null)
        {
            if (isClient)
            {
                if (GameMain.Server != null && GameMain.Server.Character == null) return;
            }
            else
            {
                if (GameMain.Server == null) return;
            }

            eventType = type;

            if (overridePrevious[(int)type])
            {
                if (events.Find(e => e.id == id && e.eventType == type) != null) return;
            }

            this.id = id;
            isClientEvent = isClient;

            this.data = data;

            events.Add(this);
        }

        public bool FillData(NetOutgoingMessage message)
        {
            message.Write((byte)eventType);

            Entity e = Entity.FindEntityByID(id);
            if (e == null) return false;

            message.Write(id);

            try
            {
                if (!e.FillNetworkData(eventType, message, data)) return false;
            }

            catch
            {
                return false;
            }

            return true;
        }

        public static bool ReadData(NetIncomingMessage message)
        {
            NetworkEventType eventType;
            int id;

            try
            {
                eventType = (NetworkEventType)message.ReadByte();
                id = message.ReadInt32();
            }
            catch
            {
                DebugConsole.ThrowError("Received invalid network message");
                return false;
            }
            
            Entity e = Entity.FindEntityByID(id);
            if (e == null)
            {
                //DebugConsole.ThrowError("Couldn't find an entity matching the ID ''" + id + "''");                
                return false;
            }

            System.Diagnostics.Debug.WriteLine("Networkevent entity: "+e.ToString());

            //System.Diagnostics.Debug.WriteLine("new message: " + eventType +" - "+e);
            try
            {
                e.ReadNetworkData(eventType, message);
            }
            catch
            {
                DebugConsole.ThrowError("Received invalid network message");
                return false;
            }

            return true;
        }
    }
}
