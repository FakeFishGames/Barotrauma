using System.Collections.Generic;
using Lidgren.Network;
using System;
using System.Linq;

namespace Barotrauma.Networking
{
    enum NetworkEventDeliveryMethod
    {
        Unreliable = 0,
        ReliableChannel = 1,
        ReliableLidgren = 2
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
        WallDamage = 11,

        PhysicsBodyPosition = 12,

        ApplyStatusEffect = 13
    }

    class NetworkEvent
    {
        public static List<NetworkEvent> Events = new List<NetworkEvent>();

        private static NetworkEventDeliveryMethod[] deliveryMethod;
        private static bool[] overridePrevious;

        static NetworkEvent()
        {
            deliveryMethod = new NetworkEventDeliveryMethod[Enum.GetNames(typeof(NetworkEventType)).Length];
            deliveryMethod[(int)NetworkEventType.ImportantEntityUpdate]     = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.ImportantComponentUpdate]  = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.KillCharacter]             = NetworkEventDeliveryMethod.ReliableLidgren;
            deliveryMethod[(int)NetworkEventType.SelectCharacter]           = NetworkEventDeliveryMethod.ReliableChannel;

            deliveryMethod[(int)NetworkEventType.ImportantComponentUpdate]  = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.PickItem]                  = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.DropItem]                  = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.InventoryUpdate]           = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.ItemFixed]                 = NetworkEventDeliveryMethod.ReliableLidgren;

            deliveryMethod[(int)NetworkEventType.UpdateProperty]            = NetworkEventDeliveryMethod.ReliableChannel;
            deliveryMethod[(int)NetworkEventType.WallDamage]                = NetworkEventDeliveryMethod.ReliableChannel;

            deliveryMethod[(int)NetworkEventType.ApplyStatusEffect]         = NetworkEventDeliveryMethod.ReliableLidgren;

            overridePrevious = new bool[deliveryMethod.Length];
            for (int i = 0; i < overridePrevious.Length; i++ )
            {
                overridePrevious[i] = true;
            }
            overridePrevious[(int)NetworkEventType.KillCharacter]       = false;

            overridePrevious[(int)NetworkEventType.PickItem]            = false;
            overridePrevious[(int)NetworkEventType.DropItem]            = false;
            overridePrevious[(int)NetworkEventType.ItemFixed]           = false;
            overridePrevious[(int)NetworkEventType.ApplyStatusEffect]   = false;
        }

        private readonly ushort id;

        private readonly NetworkEventType eventType;

        private readonly bool isClientEvent;

        private readonly object data;

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

        public NetworkEvent(ushort id, bool allowClientSend)
            : this(NetworkEventType.EntityUpdate, id, allowClientSend)
        {
        }

        public NetworkEvent(NetworkEventType type, ushort id, bool allowClientSend, object data = null)
        {
            if (!allowClientSend && GameMain.Server == null) return;
            
            eventType = type;

            if (overridePrevious[(int)type])
            {
                if (type == NetworkEventType.ComponentUpdate || type == NetworkEventType.ImportantComponentUpdate)
                {
                    if (Events.Any(e => e.id == id && e.eventType == type && data == e.data)) return;
                }
                else
                {
                    if (Events.Any(e => e.id == id && e.eventType == type)) return;
                }

            }

            this.id = id;
            isClientEvent = allowClientSend;

            this.data = data;

            Events.Add(this);
        }

        public bool FillData(NetBuffer message)
        {
            message.Write((byte)eventType);

            Entity e = Entity.FindEntityByID(id);
            if (e == null) return false;

            message.Write(id);

            try
            {
                if (!e.FillNetworkData(eventType, message, data)) return false;
            }

            catch (Exception exception)
            {
#if DEBUG
                DebugConsole.ThrowError("Failed to write network message for entity "+e.ToString(), exception);
#endif

                return false;
            }

            return true;
        }

        public static void ReadMessage(NetIncomingMessage message, bool resend=false)
        {
            float sendingTime = message.ReadFloat();

            sendingTime = (float)message.SenderConnection.GetLocalTime(sendingTime);

            byte msgCount = message.ReadByte();
            long currPos = message.PositionInBytes;

            for (int i = 0; i < msgCount; i++)
            {

                byte msgLength = message.ReadByte();

                try
                {
                    ReadData(message, sendingTime, resend);
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
                eventType = (NetworkEventType)message.ReadByte();
                id = message.ReadUInt16();
            }
            catch (Exception exception)
            {
#if DEBUG
                DebugConsole.ThrowError("Received invalid network message", exception);
#endif
                return false;
            }
            
            Entity e = Entity.FindEntityByID(id);
            if (e == null)
            {
#if DEBUG   
                DebugConsole.ThrowError("Couldn't find an entity matching the ID \"" + id + "\"");   
#endif
                return false;
            }


            //System.Diagnostics.Debug.WriteLine(e.ToString());

            object data;

            try
            {
                if (!e.ReadNetworkData(eventType, message, sendingTime, out data))
                {
                    resend = false;
                }
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
