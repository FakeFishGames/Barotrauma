using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class EntityRemover : IServerSerializable
    {
        public UInt32 NetStateID
        {
            get;
            private set;
        }

        private readonly Queue<Entity> removeQueue;

        public List<Entity> removedEntities = new List<Entity>();

        public EntityRemover()
        {
            removeQueue = new Queue<Entity>();
        }

        public void QueueItem(Item item)
        {
            if (GameMain.Client != null) return;

            removeQueue.Enqueue(item);
        }

        public void Update()
        {
            if (GameMain.Client != null) return;

            while (removeQueue.Count > 0)
            {
                var entity = removeQueue.Dequeue();
                removedEntities.Add(entity);

                entity.Remove();
                NetStateID = (UInt32)removedEntities.Count;
            }
        }

        public void ServerWrite(Lidgren.Network.NetOutgoingMessage message, Client client)
        {
            if (GameMain.Server == null) return;

            List<Entity> entities = removedEntities.Skip((int)client.lastRecvEntityRemoveID).ToList();

            message.Write((UInt32)removedEntities.Count);

            message.Write((UInt16)entities.Count);
            foreach (Entity entity in entities)
            {
                message.Write(entity.ID);
            }
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            if (GameMain.Server != null) return;

            UInt32 ID = message.ReadUInt32();

            var entityCount = message.ReadUInt16();
            for (int i = 0; i < entityCount; i++)
            {
                ushort entityId = message.ReadUInt16();

                var entity = Entity.FindEntityByID(entityId);
                if (entity == null || ID - entityCount + i < NetStateID) continue; //already removed

                entity.Remove();
            }

            NetStateID = Math.Max(ID, NetStateID);
        }

        public void Clear()
        {
            NetStateID = 0;

            removeQueue.Clear();
            removedEntities.Clear();
        }
    }
}
