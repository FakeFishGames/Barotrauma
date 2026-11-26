using System;
using Barotrauma.Networking;

namespace Barotrauma
{
    public partial class EntitySpawner : Entity, IServerSerializable
    {
        public void CreateNetworkEvent(SpawnOrRemove spawnOrRemove)
        {
            CreateNetworkEventProjSpecific(spawnOrRemove);
        }

        partial void CreateNetworkEventProjSpecific(SpawnOrRemove spawnOrRemove)
        {
            if (GameMain.Server == null || spawnOrRemove?.Entity == null) { return; }

            bool isSpawnEvent = spawnOrRemove is SpawnEntity;
#if DEBUG
            if (isSpawnEvent)
            {
                // Discrepancies here cause very hairy bugs, so let's make sure they are loud
                foreach (ServerEntityEvent entityEvent in GameMain.Server.EntityEventManager.Events)
                {
                    if (entityEvent.Entity == spawnOrRemove.Entity)
                    {
                        DebugConsole.ThrowError(
                            $"A network event ({entityEvent.Data?.GetType().Name ?? "null"}) was created for the entity " +
                            $"{spawnOrRemove.Entity} before the entity's spawn event. " +
                            "The clients will not be able to process the event, because they don't know the entity yet." +
                            $"\nThe event was created at:\n{entityEvent.StackTrace}");
                    }
                }
            }
#endif

            GameMain.Server.CreateEntityEvent(this, spawnOrRemove);
            if (isSpawnEvent)
            {
                if (spawnOrRemove.Entity is Character { Info: { } } character && !character.Removed)
                {
                    foreach (var statKey in character.Info.SavedStatValues.Keys)
                    {
                        GameMain.NetworkMember.CreateEntityEvent(character, new Character.UpdatePermanentStatsEventData(statKey));
                    }
                }
            }
        }

        public void ServerEventWrite(IWriteMessage message, Client client, NetEntityEvent.IData extraData = null)
        {
            if (GameMain.Server is null) { return; }
            if (!(extraData is SpawnOrRemove entities)) { throw new Exception($"Malformed {nameof(EntitySpawner)} event: expected {nameof(SpawnOrRemove)}"); }

            message.WriteBoolean(entities is RemoveEntity);
            if (entities is RemoveEntity)
            {
                message.WriteUInt16(entities.ID);
            }
            else
            {
                switch (entities.Entity)
                {
                    case Item item:
                        message.WriteByte((byte)SpawnableType.Item);
                        DebugConsole.Log(
                            $"Writing item spawn data {item} (ID: {entities.ID})");
                        item.WriteSpawnData(message, entities.ID, entities.InventoryID, entities.ItemContainerIndex, entities.SlotIndex);
                        break;
                    case Character character:
                        message.WriteByte((byte)SpawnableType.Character);
                        DebugConsole.Log(
                            $"Writing character spawn data: {character} (ID: {entities.ID})");
                        character.WriteSpawnData(message, entities.ID, restrictMessageSize: true);
                        break;
                }
            }
        }
    }
}
