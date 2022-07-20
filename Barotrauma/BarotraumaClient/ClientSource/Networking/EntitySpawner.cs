using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        public readonly List<(Entity entity, bool isRemoval)> receivedEvents = new List<(Entity entity, bool isRemoval)>();

        public void ClientEventRead(IReadMessage message, float sendingTime)
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
                        GameAnalyticsManager.AddDesignEvent("ItemDeconstructed:" + (GameMain.GameSession?.GameMode?.Preset.Identifier ?? "none".ToIdentifier()) + ":" + item.Prefab.Identifier);
                    }
                    entity.Remove();
                }
                else
                {
                    DebugConsole.Log("Received entity removal message for ID " + entityId + ". Entity with a matching ID not found.");
                }
                receivedEvents.Add((entity, true));
            }
            else
            {
                switch (message.ReadByte())
                {
                    case (byte)SpawnableType.Item:
                        var newItem = Item.ReadSpawnData(message, true);
                        if (newItem == null)
                        {
                            DebugConsole.ThrowError("Received an item spawn message, but spawning the item failed.");
                        }
                        else
                        {
                            if (newItem.Container?.GetComponent<Fabricator>() != null)
                            {
                                GameAnalyticsManager.AddDesignEvent("ItemFabricated:" + (GameMain.GameSession?.GameMode?.Preset.Identifier ?? "none".ToIdentifier()) + ":" + newItem.Prefab.Identifier);
                            }
                            receivedEvents.Add((newItem, false));
                        }
                        break;
                    case (byte)SpawnableType.Character:
                        var character = Character.ReadSpawnData(message);
                        if (character == null)
                        {
                            DebugConsole.ThrowError("Received character spawn message, but spawning the character failed.");
                        }
                        else
                        {
                            receivedEvents.Add((character, false));
                        }
                        break;
                    default:
                        DebugConsole.ThrowError("Received invalid entity spawn message (unknown spawnable type)");
                        break;
                }
            }
        }
    }
}
