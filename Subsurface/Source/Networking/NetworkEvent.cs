using System.Collections.Generic;
using Lidgren.Network;
using System;

namespace Barotrauma.Networking
{
    enum NetworkEventDeliveryMethod
    {
        Unreliable = 0,
        ReliableChannel = 1,
        ReliableLindgren = 2
    }

    enum NetworkEventType
    {
        EntityUpdate = 0,
        ImportantEntityUpdate = 1,

        KillCharacter = 2,
        SelectCharacter = 3,

        ComponentUpdate = 4,
        ImportantComponentUpdate = 5,

        PickItem = 6,
        DropItem = 7,
        InventoryUpdate = 8,
        ItemFixed = 9,
        
        UpdateProperty = 10,
        WallDamage = 11
    }

    class NetworkEvent
    {
        public static List<NetworkEvent> Events = new List<NetworkEvent>();

        private static NetworkEventDeliveryMethod[] deliveryMethod;
        private static bool[] overridePrevious;

        static NetworkEvent()
        {
            deliveryMethod = new NetworkEventDeliveryMethod[Enum.GetNames(typeof(NetworkEventType)).Length];
            deliveryMethod[(int)NetworkEventType.ImportantEntityUpdate] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.ImportantComponentUpdate] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.KillCharacter] = NetworkEventDeliveryMethod.ReliableLindgren;
            deliveryMethod[(int)NetworkEventType.SelectCharacter] = NetworkEventDeliveryMethod.ReliableChannel;

            deliveryMethod[(int)NetworkEventType.ImportantComponentUpdate] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.PickItem] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.DropItem] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.InventoryUpdate] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.ItemFixed] = NetworkEventDeliveryMethod.ReliableLindgren;

            deliveryMethod[(int)NetworkEventType.UpdateProperty] = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.WallDamage] = NetworkEventDeliveryMethod.ReliableChannel;

            overridePrevious = new bool[deliveryMethod.Length];
            for (int i = 0; i < overridePrevious.Length; i++ )
            {
                overridePrevious[i] = true;
            }
            overridePrevious[(int)NetworkEventType.KillCharacter] = false;

            overridePrevious[(int)NetworkEventType.PickItem] = false;
            overridePrevious[(int)NetworkEventType.DropItem] = false;
            overridePrevious[(int)NetworkEventType.ItemFixed] = false;
        }

        private ushort id;

        private NetworkEventType eventType;

        private bool isClientEvent;

        private object data;

        public NetConnection SenderConnection;

        //private NetOutgoingMessage message;

        public ushort ID
        {
            get { return id; }
        }

        public bool IsClient
        {
            get { return isClientEvent; }
        }

        public NetworkEventDeliveryMethod DeliveryMethod
        {
            get { return deliveryMethod[(int)eventType]; }
        }

        public NetworkEventType Type
        { 
            get { return eventType; } 
        }

        public NetworkEvent(ushort id, bool isClient)
            : this(NetworkEventType.EntityUpdate, id, isClient)
        {
        }

        public NetworkEvent(NetworkEventType type, ushort id, bool isClient, object data = null)
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
                if (Events.Find(e => e.id == id && e.eventType == type) != null) return;
            }

            this.id = id;
            isClientEvent = isClient;

            this.data = data;

            Events.Add(this);
        }

        public bool FillData(NetBuffer message)
        {
            message.WriteRangedInteger(0, Enum.GetValues(typeof(NetworkEventType)).Length, (int)eventType);

            Entity e = Entity.FindEntityByID(id);
            if (e == null) return false;

            message.Write(id);

            try
            {
                if (!e.FillNetworkData(eventType, message, data)) return false;
            }

            catch
            {
#if DEBUG
                DebugConsole.ThrowError("Failed to write network message for entity "+e.ToString());
#endif

                return false;
            }

            return true;
        }

        public static void ReadMessage(NetIncomingMessage message, bool resend=false)
        {
            float sendingTime = message.ReadFloat();

            byte msgCount = message.ReadByte();
            long currPos = message.PositionInBytes;

            for (int i = 0; i < msgCount; i++)
            {

                byte msgLength = message.ReadByte();

                try
                {
                    NetworkEvent.ReadData(message, sendingTime, resend);
                }
                catch
                {
                    
                }
                //+1 because msgLength is one additional byte
                currPos += msgLength + 1;
                message.Position = currPos * 8;
            }
        }

        public static bool ReadData(NetIncomingMessage message, float sendingTime, bool resend=false)
        {
            NetworkEventType eventType;
            ushort id;

            try
            {
                eventType = (NetworkEventType)message.ReadRangedInteger(0, Enum.GetValues(typeof(NetworkEventType)).Length);
                id = message.ReadUInt16();
            }
            catch
            {
#if DEBUG
                DebugConsole.ThrowError("Received invalid network message");
#endif
                return false;
            }
            
            Entity e = Entity.FindEntityByID(id);
            if (e == null)
            {
#if DEBUG   
                DebugConsole.ThrowError("Couldn't find an entity matching the ID ''" + id + "''");   
#endif
                return false;
            }


            //System.Diagnostics.Debug.WriteLine(e.ToString());

            object data;

            try
            {
                e.ReadNetworkData(eventType, message, sendingTime, out data);
            }
            catch (Exception exception)
            {
#if DEBUG   
                DebugConsole.ThrowError("Received invalid network message", exception);
#endif
                return false;
            }

            if (resend)
            {
                var resendEvent = new NetworkEvent(eventType, id, false, data);
                resendEvent.SenderConnection = message.SenderConnection;
            }

            return true;
        }
    }
}
