using Barotrauma.Networking;

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
            if (GameMain.Server == null || entity == null) { return; }
            
            GameMain.Server.CreateEntityEvent(this, new object[] { new SpawnOrRemove(entity, remove) });
            if (entity is Character character && character.Info != null)
            {
                foreach (var statKey in character.Info.SavedStatValues.Keys)
                {
                    GameMain.NetworkMember.CreateEntityEvent(character, new object[] { NetEntityEvent.Type.UpdatePermanentStats, statKey });
                }               
            }            
        }

        public void ServerWrite(IWriteMessage message, Client client, object[] extraData = null)
        {
            if (GameMain.Server == null) { return; }

            SpawnOrRemove entities = (SpawnOrRemove)extraData[0];

            message.Write(entities.Remove);
            if (entities.Remove)
            {
                message.Write(entities.OriginalID);
            }
            else
            {
                if (entities.Entity is Item item)
                {
                    message.Write((byte)SpawnableType.Item);
                    DebugConsole.Log("Writing item spawn data " + entities.Entity.ToString() + " (original ID: " + entities.OriginalID + ", current ID: " + entities.Entity.ID + ")");
                    item.WriteSpawnData(message, entities.OriginalID, entities.OriginalInventoryID, entities.OriginalItemContainerIndex, entities.OriginalSlotIndex);
                }
                else if (entities.Entity is Character character)
                {
                    message.Write((byte)SpawnableType.Character);
                    DebugConsole.Log("Writing character spawn data: " + entities.Entity.ToString() + " (original ID: " + entities.OriginalID + ", current ID: " + entities.Entity.ID + ")");
                    character.WriteSpawnData(message, entities.OriginalID, restrictMessageSize: true);
                }
            }
        }
    }
}
