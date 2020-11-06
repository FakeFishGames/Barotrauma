using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        private Dictionary<string, Pair<int, float>> ResourceClusters { get; } = new Dictionary<string, Pair<int, float>>();
        private Dictionary<string, List<Item>> SpawnedResources { get; } = new Dictionary<string, List<Item>>();
        private Dictionary<string, Item[]> RelevantLevelResources { get; } = new Dictionary<string, Item[]>();
        private List<Tuple<string, Vector2>> MissionClusterPositions { get; } = new List<Tuple<string, Vector2>>();

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                return MissionClusterPositions
                    .Where(p => SpawnedResources.ContainsKey(p.Item1) && AnyAreUncollected(SpawnedResources[p.Item1]))
                    .Select(p => p.Item2);
            }
        }

        public MineralMission(MissionPrefab prefab, Location[] locations) : base(prefab, locations)
        {
            var configElement = prefab.ConfigElement.Element("Items");
            foreach (var c in configElement.GetChildElements("Item"))
            {
                var identifier = c.GetAttributeString("identifier", null);
                if (string.IsNullOrWhiteSpace(identifier)) { continue; }
                if (ResourceClusters.ContainsKey(identifier))
                {
                    ResourceClusters[identifier].First++;
                }
                else
                {
                    ResourceClusters.Add(identifier, new Pair<int, float>(1, 0.0f));
                }
            }
        }

        public override void Start(Level level)
        {
            if (IsClient) { return; }
            foreach (var kvp in ResourceClusters)
            {
                var prefab = ItemPrefab.Find(null, kvp.Key);
                if (prefab == null) { continue; }
                var spawnedResources = level.GenerateMissionResources(prefab, kvp.Value.First, out float rotation);
                if (spawnedResources.None()) { continue; }
                SpawnedResources.Add(kvp.Key, spawnedResources);
                kvp.Value.Second = rotation;
            }
            CalculateMissionClusterPositions();
            FindRelevantLevelResources();
        }

        public override void Update(float deltaTime)
        {
            if (IsClient) { return; }
            switch (State)
            {
                case 0:
                    if (!EnoughHaveBeenCollected()) { return; }
                    State = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndPosition && !Submarine.MainSub.AtStartPosition) { return; }
                    State = 2;
                    break;
            }
        }

        public override void End()
        {
            if (!EnoughHaveBeenCollected()) { return; }
            GiveReward();
            completed = true;
        }

        private void FindRelevantLevelResources()
        {
            RelevantLevelResources.Clear();
            foreach (var identifier in ResourceClusters.Keys)
            {
                var items = Item.ItemList.Where(i => i.Prefab.Identifier == identifier &&
                    i.Submarine == null && i.ParentInventory == null &&
                    (!(i.GetComponent<Holdable>() is Holdable h) || (h.Attachable && h.Attached)))
                    .ToArray();
                RelevantLevelResources.Add(identifier, items);
            }
        }

        private bool EnoughHaveBeenCollected()
        {
            foreach (var kvp in ResourceClusters)
            {
                if (RelevantLevelResources.TryGetValue(kvp.Key, out var availableResources))
                {
                    var collected = availableResources.Count(r => HasBeenCollected(r));
                    var needed = kvp.Value.First;
                    if (collected < needed) { return false; }
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        private bool HasBeenCollected(Item item)
        {
            if (item == null) { return false; }
            if (item.Removed) { return false; }
            var owner = item.GetRootInventoryOwner();
            if (owner.Submarine != null && owner.Submarine.Info.Type == SubmarineType.Player)
            {
                return true;
            }
            else if (owner is Character c)
            {
                return c.Info != null && GameMain.GameSession.CrewManager.CharacterInfos.Contains(c.Info);
            }
            return false;
        }

        private bool AnyAreUncollected(IEnumerable<Item> items)
            => items.Any(i => !HasBeenCollected(i));

        private void CalculateMissionClusterPositions()
        {
            MissionClusterPositions.Clear();
            foreach (var kvp in SpawnedResources)
            {
                if (kvp.Value.None()) { continue; }
                var pos = Vector2.Zero;
                var itemCount = 0;
                foreach (var i in kvp.Value.Where(i => i != null && !i.Removed))
                {
                    pos += i.WorldPosition;
                    itemCount++;
                }
                pos /= itemCount;
                MissionClusterPositions.Add(new Tuple<string, Vector2>(kvp.Key, pos));
            }
        }
    }    
}
