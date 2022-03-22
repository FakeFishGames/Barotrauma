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
        private readonly Dictionary<string, (int amount, float rotation)> resourceClusters  = new Dictionary<string, (int amount, float rotation)>();
        private readonly Dictionary<string, List<Item>> spawnedResources = new Dictionary<string, List<Item>>();
        private readonly Dictionary<string, Item[]> relevantLevelResources = new Dictionary<string, Item[]>();
        private readonly List<Tuple<string, Vector2>> missionClusterPositions = new List<Tuple<string, Vector2>>();

        private readonly HashSet<Level.Cave> caves = new HashSet<Level.Cave>();

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                return missionClusterPositions
                    .Where(p => spawnedResources.ContainsKey(p.Item1) && AnyAreUncollected(spawnedResources[p.Item1]))
                    .Select(p => p.Item2);
            }
        }

        public MineralMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            var configElement = prefab.ConfigElement.Element("Items");
            foreach (var c in configElement.GetChildElements("Item"))
            {
                var identifier = c.GetAttributeString("identifier", null);
                if (string.IsNullOrWhiteSpace(identifier)) { continue; }
                if (resourceClusters.ContainsKey(identifier))
                {
                    resourceClusters[identifier] = (resourceClusters[identifier].amount + 1, resourceClusters[identifier].rotation);
                }
                else
                {
                    resourceClusters.Add(identifier, (1, 0.0f));
                }
            }
        }

        protected override void StartMissionSpecific(Level level)
        {
            if (spawnedResources.Any())
            {
#if DEBUG
                throw new Exception($"SpawnedResources.Count > 0 ({spawnedResources.Count})");
#else
                DebugConsole.AddWarning("Spawned resources list was not empty at the start of a mineral mission. The mission instance may not have been ended correctly on previous rounds.");
                spawnedResources.Clear();
#endif
            }

            if (relevantLevelResources.Any())
            {
#if DEBUG
                throw new Exception($"RelevantLevelResources.Count > 0 ({relevantLevelResources.Count})");
#else
                DebugConsole.AddWarning("Relevant level resources list was not empty at the start of a mineral mission. The mission instance may not have been ended correctly on previous rounds.");
                relevantLevelResources.Clear();
#endif
            }

            if (missionClusterPositions.Any())
            {
#if DEBUG
                throw new Exception($"MissionClusterPositions.Count > 0 ({missionClusterPositions.Count})");
#else
                DebugConsole.AddWarning("Mission cluster positions list was not empty at the start of a mineral mission. The mission instance may not have been ended correctly on previous rounds.");
                missionClusterPositions.Clear();
#endif
            }

            caves.Clear();

            if (IsClient) { return; }
            foreach (var kvp in resourceClusters)
            {
                var prefab = ItemPrefab.Find(null, kvp.Key);
                if (prefab == null)
                {
                    DebugConsole.ThrowError("Error in MineralMission - " +
                        "couldn't find an item prefab with the identifier " + kvp.Key);
                    continue;
                }
                var spawnedResources = level.GenerateMissionResources(prefab, kvp.Value.amount, out float rotation);
                if (spawnedResources.Count < kvp.Value.amount)
                {
                    DebugConsole.ThrowError("Error in MineralMission - " +
                        "spawned " + spawnedResources.Count + "/" + kvp.Value.amount + " of " + prefab.Name);
                }
                if (spawnedResources.None()) { continue; }
                this.spawnedResources.Add(kvp.Key, spawnedResources);

                foreach (Level.Cave cave in Level.Loaded.Caves)
                {
                    foreach (Item spawnedResource in spawnedResources)
                    {
                        if (cave.Area.Contains(spawnedResource.WorldPosition))
                        {
                            cave.DisplayOnSonar = true;
                            caves.Add(cave);
                            break;
                        }
                    }
                }
            }
            CalculateMissionClusterPositions();
            FindRelevantLevelResources();
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (IsClient) { return; }
            switch (State)
            {
                case 0:
                    if (!EnoughHaveBeenCollected()) { return; }
                    State = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndExit && !Submarine.MainSub.AtStartExit) { return; }
                    State = 2;
                    break;
            }
        }

        public override void End()
        {
            if (EnoughHaveBeenCollected())
            {
                if (Prefab.LocationTypeChangeOnCompleted != null)
                {
                    ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                }
                GiveReward();
                completed = true;
            }
            foreach (var kvp in spawnedResources)
            {
                foreach (var i in kvp.Value)
                {
                    if (i != null && !i.Removed && !HasBeenCollected(i))
                    {
                        i.Remove();
                    }
                }
            }
            spawnedResources.Clear();
            relevantLevelResources.Clear();
            missionClusterPositions.Clear();
            failed = !completed && state > 0;
        }

        private void FindRelevantLevelResources()
        {
            relevantLevelResources.Clear();
            foreach (var identifier in resourceClusters.Keys)
            {
                var items = Item.ItemList.Where(i => i.Prefab.Identifier == identifier &&
                    i.Submarine == null && i.ParentInventory == null &&
                    (!(i.GetComponent<Holdable>() is Holdable h) || (h.Attachable && h.Attached)))
                    .ToArray();
                relevantLevelResources.Add(identifier, items);
            }
        }

        private bool EnoughHaveBeenCollected()
        {
            foreach (var kvp in resourceClusters)
            {
                if (relevantLevelResources.TryGetValue(kvp.Key, out var availableResources))
                {
                    var collected = availableResources.Count(r => HasBeenCollected(r));
                    var needed = kvp.Value.amount;
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
            missionClusterPositions.Clear();
            foreach (var kvp in spawnedResources)
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
                missionClusterPositions.Add(new Tuple<string, Vector2>(kvp.Key, pos));
            }
        }
    }    
}
