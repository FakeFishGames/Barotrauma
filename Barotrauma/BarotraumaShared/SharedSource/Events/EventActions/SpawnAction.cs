using System;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class SpawnAction : EventAction
    {
        public enum SpawnLocationType
        {
            MainSub,
            Outpost,
            MainPath,
            Ruin,
            Wreck
        }

        [Serialize("", true, description: "Species name of the character to spawn.")]
        public string SpeciesName { get; set; }

        [Serialize("", true, description: "Identifier of the NPC set to choose from.")]
        public string NPCSetIdentifier { get; set; }

        [Serialize("", true, description: "Identifier of the NPC.")]
        public string NPCIdentifier { get; set; }

        [Serialize(true, true, description: "Should taking the items of this npc be considered as stealing?")]
        public bool LootingIsStealing { get; set; }

        [Serialize("", true, description: "Identifier of the item to spawn.")]
        public string ItemIdentifier { get; set; }

        [Serialize("", true, description: "The spawned entity will be assigned this tag. The tag can be used to refer to the entity by other actions of the event.")]
        public string TargetTag { get; set; }

        [Serialize("", true, description: "Tag of an entity with an inventory to spawn the item into.")]
        public string TargetInventory { get; set; }

        [Serialize(SpawnLocationType.MainSub, true)]
        public SpawnLocationType SpawnLocation { get; set; }

        [Serialize(SpawnType.Human, true)] 
        public SpawnType SpawnPointType { get; set; }

        [Serialize("", true)]
        public string SpawnPointTag { get; set; }

        private readonly HashSet<string> targetModuleTags = new HashSet<string>();

        [Serialize("", true, "What outpost module tags does the entity prefer to spawn in.")]
        public string TargetModuleTags
        {
            get => string.Join(",", targetModuleTags);
            set
            {
                targetModuleTags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string[] splitTags = value.Split(',');
                    foreach (var s in splitTags)
                    {
                        targetModuleTags.Add(s);
                    }
                }
            }
        }

        [Serialize(false, true, description: "Should the AI ignore this item. This will prevent outpost NPCs cleaning up or otherwise using important items intended to be left for the players.")]
        public bool IgnoreByAI { get; set; }

        private bool spawned;
        private Entity spawnedEntity;

        private readonly bool ignoreSpawnPointType;

        public SpawnAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            ignoreSpawnPointType = !element.Attributes().Any(a => a.Name.ToString().Equals("spawnpointtype", StringComparison.OrdinalIgnoreCase));            
        }

        public override bool IsFinished(ref string goTo)
        {
            if (spawnedEntity != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public override void Reset()
        {
            spawned = false;
            spawnedEntity = null;
        }

        public override void Update(float deltaTime)
        {
            if (spawned) { return; }
            
            if (!string.IsNullOrEmpty(NPCSetIdentifier) && !string.IsNullOrEmpty(NPCIdentifier))
            {
                HumanPrefab humanPrefab = NPCSet.Get(NPCSetIdentifier, NPCIdentifier);
                ISpatialEntity spawnPos = GetSpawnPos();
                Entity.Spawner.AddToSpawnQueue(CharacterPrefab.HumanSpeciesName, OffsetSpawnPos(spawnPos?.WorldPosition ?? Vector2.Zero, 100.0f), onSpawn: newCharacter =>
                {
                    newCharacter.TeamID = CharacterTeamType.FriendlyNPC;
                    newCharacter.EnableDespawn = false;
                    humanPrefab.GiveItems(newCharacter, newCharacter.Submarine);
                    if (LootingIsStealing)
                    {
                        foreach (Item item in newCharacter.Inventory.AllItems)
                        {
                            item.SpawnedInOutpost = true;
                        }
                    }
                    humanPrefab.InitializeCharacter(newCharacter, spawnPos);
                    if (!string.IsNullOrEmpty(TargetTag) && newCharacter != null)
                    {
                        ParentEvent.AddTarget(TargetTag, newCharacter);
                    }
                    spawnedEntity = newCharacter;
                });
            }
            else if (!string.IsNullOrEmpty(SpeciesName))
            {
                Entity.Spawner.AddToSpawnQueue(SpeciesName, OffsetSpawnPos(GetSpawnPos()?.WorldPosition ?? Vector2.Zero, 100.0f), onSpawn: newCharacter =>
                {
                    if (!string.IsNullOrEmpty(TargetTag) && newCharacter != null)
                    {
                        ParentEvent.AddTarget(TargetTag, newCharacter);
                    }
                    spawnedEntity = newCharacter;
                });
            }
            else if (!string.IsNullOrEmpty(ItemIdentifier))
            {
                if (!(MapEntityPrefab.Find(null, identifier: ItemIdentifier) is ItemPrefab itemPrefab))
                {
                    DebugConsole.ThrowError("Error in SpawnAction (item prefab \"" + ItemIdentifier + "\" not found)");
                }
                else
                {
                    Inventory spawnInventory = null;
                    if (!string.IsNullOrEmpty(TargetInventory))
                    {
                        var targets = ParentEvent.GetTargets(TargetInventory);
                        if (targets.Any())
                        {
                            var target = targets.First(t => t is Item || t is Character);
                            if (target is Character character)
                            {
                                spawnInventory = character.Inventory;
                            }
                            else if (target is Item item)
                            {
                                spawnInventory = item.OwnInventory;
                            }
                        }

                        if (spawnInventory == null)
                        {
                            DebugConsole.ThrowError($"Could not spawn \"{ItemIdentifier}\" in target inventory \"{TargetInventory}\"");
                        }
                    }

                    if (spawnInventory == null)
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, OffsetSpawnPos(GetSpawnPos()?.WorldPosition ?? Vector2.Zero, 100.0f), onSpawned: onSpawned);
                    }
                    else
                    {
                        Entity.Spawner.AddToSpawnQueue(itemPrefab, spawnInventory, onSpawned: onSpawned);
                    }
                    void onSpawned(Item newItem)
                    {
                        if (newItem != null)
                        {
                            if (!string.IsNullOrEmpty(TargetTag))
                            {
                                ParentEvent.AddTarget(TargetTag, newItem);
                            }
                            if (IgnoreByAI)
                            {
                                newItem.AddTag("ignorebyai");
                            }
                        }
                        spawnedEntity = newItem;
                    }
                }
            }

            spawned = true;
            
        }

        public static Vector2 OffsetSpawnPos(Vector2 pos, float offsetAmount)
        {
            Hull hull = Hull.FindHull(pos);
            pos += Rand.Vector(offsetAmount);
            if (hull != null)
            {
                float margin = 50.0f;
                pos = new Vector2(
                    MathHelper.Clamp(pos.X, hull.WorldRect.X + margin, hull.WorldRect.Right - margin),
                    MathHelper.Clamp(pos.Y, hull.WorldRect.Y - hull.WorldRect.Height + margin, hull.WorldRect.Y - margin));
            }
            return pos;
        }

        private ISpatialEntity GetSpawnPos()
        {
            if (!string.IsNullOrWhiteSpace(SpawnPointTag))
            {
                List<Item> potentialItems = SpawnLocation switch
                {
                    SpawnLocationType.MainSub => Item.ItemList.FindAll(it => it.Submarine == Submarine.MainSub),
                    SpawnLocationType.MainPath => Item.ItemList.FindAll(it => it.Submarine == null && it.ParentRuin == null),
                    SpawnLocationType.Outpost => Item.ItemList.FindAll(it => it.Submarine != null && it.Submarine.Info.IsOutpost),
                    SpawnLocationType.Wreck => Item.ItemList.FindAll(it => it.Submarine != null && it.Submarine.Info.IsWreck),
                    SpawnLocationType.Ruin => Item.ItemList.FindAll(it => it.ParentRuin != null),
                    _ => throw new NotImplementedException()
                };

                var item = potentialItems.Where(it => it.HasTag(SpawnPointTag)).GetRandom();
                if (item != null) { return item; }

                var target = ParentEvent.GetTargets(SpawnPointTag).GetRandom();
                if (target != null) { return target; }
            }

            SpawnType? spawnPointType = null;
            if (!ignoreSpawnPointType) { spawnPointType = SpawnPointType; }

            return GetSpawnPos(SpawnLocation, spawnPointType, targetModuleTags, SpawnPointTag.ToEnumerable());
        }

        public static WayPoint GetSpawnPos(SpawnLocationType spawnLocation, SpawnType? spawnPointType, IEnumerable<string> moduleFlags = null, IEnumerable<string> spawnpointTags = null, bool asFarAsPossibleFromAirlock = false)
        {
            List<WayPoint> potentialSpawnPoints = spawnLocation switch
            {
                SpawnLocationType.MainSub => WayPoint.WayPointList.FindAll(wp => wp.Submarine == Submarine.MainSub && wp.CurrentHull != null),
                SpawnLocationType.MainPath => WayPoint.WayPointList.FindAll(wp => wp.Submarine == null && wp.ParentRuin == null),
                SpawnLocationType.Outpost => WayPoint.WayPointList.FindAll(wp => wp.Submarine != null && wp.CurrentHull != null && wp.Submarine.Info.IsOutpost),
                SpawnLocationType.Wreck => WayPoint.WayPointList.FindAll(wp => wp.Submarine != null && wp.Submarine.Info.IsWreck),
                SpawnLocationType.Ruin => WayPoint.WayPointList.FindAll(wp => wp.ParentRuin != null),
                _ => throw new NotImplementedException()
            };

            potentialSpawnPoints = potentialSpawnPoints.FindAll(wp => wp.ConnectedDoor == null && wp.Ladders == null && !wp.isObstructed);

            var airlockSpawnPoints = potentialSpawnPoints.Where(wp => wp.CurrentHull?.OutpostModuleTags?.Contains("airlock") ?? false).ToList();
            if (moduleFlags != null && moduleFlags.Any())
            {
                List<WayPoint> spawnPoints = potentialSpawnPoints.Where(wp => wp.CurrentHull?.OutpostModuleTags?.Any(moduleFlags.Contains) ?? false).ToList();
                if (spawnPoints.Any())
                {
                    potentialSpawnPoints = spawnPoints;
                }
            }

            if (spawnpointTags != null && spawnpointTags.Any())
            {
                var spawnPoints = potentialSpawnPoints.Where(wp => spawnpointTags.Any(tag => wp.Tags.Contains(tag)))
                                                                 .Where(wp => wp.ConnectedDoor == null && !wp.isObstructed);
                if (spawnPoints.Any())
                {
                    potentialSpawnPoints = spawnPoints.ToList();
                }
            }

            if (potentialSpawnPoints.Count == 0)
            {
                DebugConsole.ThrowError($"Could not find a spawn point for a SpawnAction (spawn location: {spawnLocation})");
                return null;
            }

            IEnumerable<WayPoint> validSpawnPoints;
            if (spawnPointType.HasValue)
            {
                validSpawnPoints = potentialSpawnPoints.FindAll(wp => spawnPointType.Value.HasFlag(wp.SpawnType));
            }
            else
            {
                validSpawnPoints = potentialSpawnPoints.FindAll(wp => wp.SpawnType != SpawnType.Path);
                if (!validSpawnPoints.Any()) { validSpawnPoints = potentialSpawnPoints; }
            }

            //don't spawn in an airlock module if there are other options
            if (airlockSpawnPoints.Count() < validSpawnPoints.Count())
            {
                validSpawnPoints = validSpawnPoints.Except(airlockSpawnPoints);
            }

            if (!validSpawnPoints.Any())
            {
                DebugConsole.ThrowError($"Could not find a spawn point of the correct type for a SpawnAction (spawn location: {spawnLocation}, type: {spawnPointType}, module flags: {((moduleFlags == null || !moduleFlags.Any()) ? "none" : string.Join(", ", moduleFlags))})");
                return potentialSpawnPoints.GetRandom();
            }

            //avoid using waypoints if there's any actual spawnpoints available
            if (validSpawnPoints.Any(wp => wp.SpawnType != SpawnType.Path))
            {
                validSpawnPoints = validSpawnPoints.Where(wp => wp.SpawnType != SpawnType.Path);
            }

            //if not trying to spawn at a tagged spawnpoint, favor spawnpoints without tags
            if (spawnpointTags == null || !spawnpointTags.Any())
            {
                var spawnPoints = validSpawnPoints.Where(wp => !wp.Tags.Any());
                if (spawnPoints.Any())
                {
                    validSpawnPoints = spawnPoints.ToList();
                }
            }

            if (asFarAsPossibleFromAirlock && airlockSpawnPoints.Any())
            {
                WayPoint furthestPoint = validSpawnPoints.First();
                float furthestDist = 0.0f;
                foreach (WayPoint waypoint in validSpawnPoints)
                {
                    float dist = Vector2.DistanceSquared(waypoint.WorldPosition, airlockSpawnPoints.First().WorldPosition);
                    if (dist > furthestDist)
                    {
                        furthestDist = dist;
                        furthestPoint = waypoint;
                    }
                }
                return furthestPoint;
            }
            else
            {
                return validSpawnPoints.GetRandom();
            }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(spawned)} {nameof(SpawnAction)} -> (Spawned entity: {spawnedEntity.ColorizeObject()})";
        }
    }
}