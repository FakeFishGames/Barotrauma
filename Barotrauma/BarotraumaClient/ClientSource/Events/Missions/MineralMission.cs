using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            for (int i = 0; i < ResourceClusters.Count; i++)
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
                    if (SpawnedResources.TryGetValue(item.Prefab.Identifier, out var resources))
                    {
                        resources.Add(item);
                    }
                    else
                    {
                        SpawnedResources.Add(item.Prefab.Identifier, new List<Item>() { item });
                    }
                }
            }

            CalculateMissionClusterPositions();

            for(int i = 0; i < ResourceClusters.Count; i++)
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
                RelevantLevelResources.Add(identifier, resources);
            }
        }
    }
}
