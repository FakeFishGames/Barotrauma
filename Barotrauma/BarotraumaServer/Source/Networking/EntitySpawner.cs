using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public void CreateNetworkEvent(Entity entity, bool remove)
        {
            if (GameMain.Server != null && entity != null)
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { new SpawnOrRemove(entity, remove) });
            }
        }

        public void ServerWrite(Lidgren.Network.NetBuffer message, Client client, object[] extraData = null)
        {
            if (GameMain.Server == null) return;

            SpawnOrRemove entities = (SpawnOrRemove)extraData[0];

            message.Write(entities.Remove);

            if (entities.Remove)
            {
                message.Write(entities.Entity.ID);
            }
            else
            {
                if (entities.Entity is Item)
                {
                    message.Write((byte)SpawnableType.Item);
                    ((Item)entities.Entity).WriteSpawnData(message);
                }
                else if (entities.Entity is Character)
                {
                    message.Write((byte)SpawnableType.Character);
                    DebugConsole.NewMessage("WRITING CHARACTER DATA: " + (entities.Entity).ToString() + " (ID: " + entities.Entity.ID + ")", Color.Cyan);
                    ((Character)entities.Entity).WriteSpawnData(message);
                }
            }
        }
    }
}
