using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using PositionType = Barotrauma.Level.PositionType;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        private struct ResourceCluster
        {
            public int Amount;
            public float Rotation;

            public ResourceCluster(int amount, float rotation)
            {
                Amount = amount;
                Rotation = rotation;
            }
            
            public static implicit operator ResourceCluster((int amount, float rotation) tuple) => new ResourceCluster(tuple.amount, tuple.rotation);
        }
        private readonly Dictionary<Identifier, ResourceCluster> resourceClusters  = new Dictionary<Identifier, ResourceCluster>();
        private readonly Dictionary<Identifier, List<Item>> spawnedResources = new Dictionary<Identifier, List<Item>>();
        private readonly Dictionary<Identifier, Item[]> relevantLevelResources = new Dictionary<Identifier, Item[]>();
        private readonly List<(Identifier Identifier, Vector2 Position)> missionClusterPositions = new List<(Identifier Identifier, Vector2 Position)>();

        private readonly HashSet<Level.Cave> caves = new HashSet<Level.Cave>();

        private readonly PositionType positionType = PositionType.Cave;
        /// <remarks>
        /// The list order is important.
        /// It defines the order in which we "override" <see cref="positionType"/> in case no valid position types are found
        /// in the level when generating them in <see cref="Level.GenerateMissionResources(ItemPrefab, int, PositionType, out float)"/>.
        /// </remarks>
        public static readonly ImmutableArray<PositionType> ValidPositionTypes = new PositionType[]
        {
            PositionType.Cave,
            PositionType.SidePath,
            PositionType.MainPath,
            PositionType.AbyssCave,
        }.ToImmutableArray();

        /// <summary>
        /// Percentage. Value between 0 and 1.
        /// </summary>
        private readonly float resourceHandoverAmount;

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                return missionClusterPositions
                    .Where(p => spawnedResources.ContainsKey(p.Item1) && AnyAreUncollected(spawnedResources[p.Item1]))
                    .Select(p => p.Item2);
            }
        }

        public override LocalizedString SuccessMessage => ModifyMessage(base.SuccessMessage);
        public override LocalizedString FailureMessage => ModifyMessage(base.FailureMessage);
        public override LocalizedString Description => ModifyMessage(description);
        public override LocalizedString Name => ModifyMessage(base.Name, false);
        public override LocalizedString SonarLabel => ModifyMessage(base.SonarLabel, false);

        public MineralMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            var positionType = prefab.ConfigElement.GetAttributeEnum("PositionType", in this.positionType);
            if (ValidPositionTypes.Contains(positionType))
            {
                this.positionType = positionType;
            }

            float handoverAmount = prefab.ConfigElement.GetAttributeFloat("ResourceHandoverAmount", 0.0f);
            resourceHandoverAmount = Math.Clamp(handoverAmount, 0.0f, 1.0f);

            var configElement = prefab.ConfigElement.GetChildElement("Items");
            foreach (var c in configElement.GetChildElements("Item"))
            {
                var identifier = c.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (identifier.IsEmpty) { continue; }
                if (resourceClusters.ContainsKey(identifier))
                {
                    resourceClusters[identifier] = (resourceClusters[identifier].Amount + 1, resourceClusters[identifier].Rotation);
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

            foreach ((Identifier identifier, ResourceCluster cluster) in resourceClusters)
            {
                if (MapEntityPrefab.FindByIdentifier(identifier) is not ItemPrefab prefab)
                {
                    DebugConsole.ThrowError($"Error in MineralMission: couldn't find an item prefab (identifier: \"{identifier}\")");
                    continue;
                }

                var spawnedResources = level.GenerateMissionResources(prefab, cluster.Amount, positionType, out float rotation, caves);
                if (spawnedResources.Count < cluster.Amount)
                {
                    DebugConsole.ThrowError($"Error in MineralMission: spawned only {spawnedResources.Count}/{cluster.Amount} of {prefab.Name}");
                }

                if (spawnedResources.None()) { continue; }

                this.spawnedResources.Add(identifier, spawnedResources);

                foreach (var cave in Level.Loaded.Caves)
                {
                    foreach (var spawnedResource in spawnedResources)
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

        protected override bool DetermineCompleted()
        {
            return EnoughHaveBeenCollected();
        }

        protected override void EndMissionSpecific(bool completed)
        {
            failed = !completed && state > 0;
            if (completed)
            {
                if (!IsClient)
                {
                    // When mission is completed successfully, half of the resources will be removed from the player (i.e. given to the outpost as a part of the mission)
                    var handoverResources = new List<Item>();
                    foreach (Identifier identifier in resourceClusters.Keys)
                    {
                        if (relevantLevelResources.TryGetValue(identifier, out var availableResources))
                        {
                            var collectedResources = availableResources.Where(HasBeenCollected);
                            if (!collectedResources.Any()) { continue; }
                            int handoverCount = (int)MathF.Round(resourceHandoverAmount * collectedResources.Count());
                            for (int i = 0; i < handoverCount; i++)
                            {
                                handoverResources.Add(collectedResources.ElementAt(i));
                            }
                        }
                    }
                    foreach (var resource in handoverResources)
                    {
                        resource.Remove();
                    }
                }
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
                    var collected = availableResources.Count(HasBeenCollected);
                    var needed = kvp.Value.Amount;
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
                missionClusterPositions.Add((kvp.Key, pos));
            }
        }

        protected override LocalizedString ModifyMessage(LocalizedString message, bool color = true)
        {
            int i = 1;
            foreach ((Identifier identifier, ResourceCluster cluster) in resourceClusters)
            {
                Replace($"[resourcename{i}]", ItemPrefab.FindByIdentifier(identifier)?.Name.Value ?? "");
                Replace($"[resourcequantity{i}]", cluster.Amount.ToString());
                i++;
            }
            Replace("[handoverpercentage]", ToolBox.GetFormattedPercentage(resourceHandoverAmount));
            return message;

            void Replace(string find, string replace)
            {
                if (color)
                {
                    replace = $"‖color:gui.orange‖{replace}‖end‖";
                }
                message = message.Replace(find, replace);
            }
        }
    }    
}
