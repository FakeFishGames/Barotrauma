using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public void CreateNetworkEvent(Entity entity, bool remove)
        {
            CreateNetworkEventProjSpecific(entity, remove);
        }

        partial void CreateNetworkEventProjSpecific(Entity entity, bool remove)
        {
            if (GameMain.Server != null && entity != null)
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { new SpawnOrRemove(entity, remove) });
            }
        }

        public void ServerWrite(IWriteMessage message, Client client, object[] extraData = null)
        {
            if (GameMain.Server == null) return;

            SpawnOrRemove entities = (SpawnOrRemove)extraData[0];

            message.Write(entities.Remove);
            if (entities.Remove)
            {
                message.Write(entities.OriginalID);
            }
            else
            {
                if (entities.Entity is Item)
                {
                    message.Write((byte)SpawnableType.Item);
                    DebugConsole.Log("Writing item spawn data " + entities.Entity.ToString() + " (original ID: " + entities.OriginalID + ", current ID: " + entities.Entity.ID + ")");
                    ((Item)entities.Entity).WriteSpawnData(message, entities.OriginalID, entities.OriginalInventoryID, entities.OriginalItemContainerIndex);
                }
                else if (entities.Entity is Character)
                {
                    message.Write((byte)SpawnableType.Character);
                    DebugConsole.Log("Writing character spawn data: " + entities.Entity.ToString() + " (original ID: " + entities.OriginalID + ", current ID: " + entities.Entity.ID + ")");
                    ((Character)entities.Entity).WriteSpawnData(message, entities.OriginalID, restrictMessageSize: true);
                }
            }
        }
    }
}
