using Barotrauma.Items.Components;
using Barotrauma.Networking;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public void ClientRead(ServerNetObject type, IReadMessage message, float sendingTime)
        {
            bool remove = message.ReadBoolean();

            if (remove)
            {
                ushort entityId = message.ReadUInt16();

                var entity = FindEntityByID(entityId);
                if (entity != null)
                {
                    DebugConsole.Log($"Received entity removal message for \"{entity}\".");
                    if (entity is Item item && item.Container?.GetComponent<Deconstructor>() != null)
                    {
                        GameAnalyticsManager.AddDesignEvent("ItemDeconstructed:" + (GameMain.GameSession?.GameMode?.Preset.Identifier ?? "none") + ":" + item.prefab.Identifier);
                    }
                    entity.Remove();
                }
                else
                {
                    DebugConsole.Log("Received entity removal message for ID " + entityId + ". Entity with a matching ID not found.");
                }
            }
            else
            {
                switch (message.ReadByte())
                {
                    case (byte)SpawnableType.Item:
                        var newItem = Item.ReadSpawnData(message, true);
                        if (newItem is Item item && item.Container?.GetComponent<Fabricator>() != null)
                        {
                            GameAnalyticsManager.AddDesignEvent("ItemFabricated:" + (GameMain.GameSession?.GameMode?.Preset.Identifier ?? "none") + ":" + item.prefab.Identifier);
                        }
                        break;
                    case (byte)SpawnableType.Character:
                        Character.ReadSpawnData(message);
                        break;
                    default:
                        DebugConsole.ThrowError("Received invalid entity spawn message (unknown spawnable type)");
                        break;
                }
            }
        }
    }
}
