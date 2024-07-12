using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        public override bool DisplayAsCompleted => false;
        public override bool DisplayAsFailed => false;

        public override int State
        {
            get => base.State;
            set
            {
                base.State = value;
                if (base.State > 0)
                {
                    caves.ForEach(c => c.MissionsToDisplayOnSonar.Remove(this));
                }
            }
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            byte caveCount = msg.ReadByte();
            for (int i = 0; i < caveCount; i++)
            {
                byte selectedCaveIndex = msg.ReadByte();
                if (selectedCaveIndex < 255 && Level.Loaded != null)
                {
                    if (selectedCaveIndex < Level.Loaded.Caves.Count)
                    {
                        var selectedCave = Level.Loaded.Caves[selectedCaveIndex];
                        selectedCave.MissionsToDisplayOnSonar.Add(this);
                        caves.Add(selectedCave);
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Cave index out of bounds when reading nest mission data. Index: {selectedCaveIndex}, number of caves: {Level.Loaded.Caves.Count}");
                    }
                }
            }

            for (int i = 0; i < resourceAmounts.Count; i++)
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

            for(int i = 0; i < resourceAmounts.Count; i++)
            {
                var identifier = msg.ReadIdentifier();
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
