using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        public override bool IsAtCompletionState => false;
        public override bool IsAtFailureState => false;

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            byte caveCount = msg.ReadByte();
            for (int i = 0; i < caveCount; i++)
            {
                byte selectedCave = msg.ReadByte();
                if (selectedCave < 255 && Level.Loaded != null)
                {
                    if (selectedCave < Level.Loaded.Caves.Count)
                    {
                        Level.Loaded.Caves[selectedCave].DisplayOnSonar = true;
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Cave index out of bounds when reading nest mission data. Index: {selectedCave}, number of caves: {Level.Loaded.Caves.Count}");
                    }
                }
            }

            for (int i = 0; i < resourceClusters.Count; i++)
            {
                var amount = msg.ReadByte();
                var rotation = msg.ReadSingle();
                for (int j = 0; j < amount; j++)
                {
                    var item = Item.ReadSpawnData(msg);
                    if (item.GetComponent<Holdable>() is Holdable h)
                    {
                        h.AttachToWall();
                        item.Rotation = rotation;
                    }
                    if (spawnedResources.TryGetValue(item.Prefab.Identifier, out var resources))
                    {
                        resources.Add(item);
                    }
                    else
                    {
                        spawnedResources.Add(item.Prefab.Identifier, new List<Item>() { item });
                    }
                }
            }

            CalculateMissionClusterPositions();

            for(int i = 0; i < resourceClusters.Count; i++)
            {
                var identifier = msg.ReadString();
                var count = msg.ReadByte();
                var resources = new Item[count];
                for (int j = 0; j < count; j++)
                {
                    var id = msg.ReadUInt16();
                    var entity = Entity.FindEntityByID(id);
                    if (!(entity is Item item)) { continue; }
                    resources[j] = item;
                }
                relevantLevelResources.Add(identifier, resources);
            }
        }
    }
}
