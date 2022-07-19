using System;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public void CreateNetworkEvent(SpawnOrRemove spawnOrRemove)
        {
            CreateNetworkEventProjSpecific(spawnOrRemove);
        }

        partial void CreateNetworkEventProjSpecific(SpawnOrRemove spawnOrRemove)
        {
            if (GameMain.Server == null || spawnOrRemove?.Entity == null) { return; }

            GameMain.Server.CreateEntityEvent(this, spawnOrRemove);
            if (spawnOrRemove is SpawnEntity)
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

            message.Write(entities is RemoveEntity);
            if (entities is RemoveEntity)
            {
                message.Write(entities.ID);
            }
            else
            {
                switch (entities.Entity)
                {
                    case Item item:
                        message.Write((byte)SpawnableType.Item);
                        DebugConsole.Log(
                            $"Writing item spawn data {item} (ID: {entities.ID})");
                        item.WriteSpawnData(message, entities.ID, entities.InventoryID, entities.ItemContainerIndex, entities.SlotIndex);
                        break;
                    case Character character:
                        message.Write((byte)SpawnableType.Character);
                        DebugConsole.Log(
                            $"Writing character spawn data: {character} (ID: {entities.ID})");
                        character.WriteSpawnData(message, entities.ID, restrictMessageSize: true);
                        break;
                }
            }
        }
    }
}
