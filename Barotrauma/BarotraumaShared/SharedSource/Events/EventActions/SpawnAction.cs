using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class SpawnAction : EventAction
    {
        public enum SpawnLocationType
        {
            Any,
            MainSub,
            Outpost,
            MainPath,
            Ruin,
            Wreck,
            BeaconStation
        }

        [Serialize("", IsPropertySaveable.Yes, description: "Species name of the character to spawn.")]
        public Identifier SpeciesName { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the NPC set to choose from.")]
        public Identifier NPCSetIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the NPC.")]
        public Identifier NPCIdentifier { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should taking the items of this npc be considered as stealing?")]
        public bool LootingIsStealing { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the item to spawn.")]
        public Identifier ItemIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The spawned entity will be assigned this tag. The tag can be used to refer to the entity by other actions of the event.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of an entity with an inventory to spawn the item into.")]
        public Identifier TargetInventory { get; set; }

        [Serialize(SpawnLocationType.Any, IsPropertySaveable.Yes)]
        public SpawnLocationType SpawnLocation { get; set; }

        [Serialize(SpawnType.Human, IsPropertySaveable.Yes)] 
        public SpawnType SpawnPointType { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier SpawnPointTag { get; set; }

        [Serialize(CharacterTeamType.FriendlyNPC, IsPropertySaveable.Yes)]
        public CharacterTeamType Team { get; protected set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should we spawn the entity even when no spawn points with matching tags were found?")]
        public bool RequireSpawnPointTag { get; set; }

        private readonly HashSet<Identifier> targetModuleTags = new HashSet<Identifier>();

        [Serialize(true, IsPropertySaveable.Yes, description: "If false, we won't spawn another character if one with the same identifier has already been spawned.")]
        public bool AllowDuplicates { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes)]
        public float Offset { get; set; }

        [Serialize("", IsPropertySaveable.Yes, "What outpost module tags does the entity prefer to spawn in.")]
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
                        targetModuleTags.Add(s.ToIdentifier());
                    }
                }
            }
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the AI ignore this item. This will prevent outpost NPCs cleaning up or otherwise using important items intended to be left for the players.")]
        public bool IgnoreByAI { get; set; }

        private bool spawned;
        private Entity spawnedEntity;

        private readonly bool ignoreSpawnPointType;

        public SpawnAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            ignoreSpawnPointType = element.GetAttribute("spawnpointtype") == null;
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

            if (!NPCSetIdentifier.IsEmpty && !NPCIdentifier.IsEmpty)
            {
                HumanPrefab humanPrefab = NPCSet.Get(NPCSetIdentifier, NPCIdentifier);
                if (humanPrefab != null)
                {
                    if (!AllowDuplicates && 
                        Character.CharacterList.Any(c => c.Info?.HumanPrefabIds.NpcIdentifier == NPCIdentifier && c.Info?.HumanPrefabIds.NpcSetIdentifier == NPCSetIdentifier))
                    {
                        spawned = true;
                        return;
                    }
                    ISpatialEntity spawnPos = GetSpawnPos();
                    if (spawnPos != null)
                    {
                        Entity.Spawner.AddCharacterToSpawnQueue(CharacterPrefab.HumanSpeciesName, OffsetSpawnPos(spawnPos.WorldPosition, Offset), humanPrefab.CreateCharacterInfo(), onSpawn: newCharacter =>
                        {
                            if (newCharacter == null) { return; }
                            newCharacter.HumanPrefab = humanPrefab;
                            newCharacter.TeamID = Team;
                            newCharacter.EnableDespawn = false;
                            humanPrefab.GiveItems(newCharacter, newCharacter.Submarine);
                            if (LootingIsStealing)
                            {
                                foreach (Item item in newCharacter.Inventory.FindAllItems(recursive: true))
                                {
                                    item.SpawnedInCurrentOutpost = true;
                                    item.AllowStealing = false;
                                }
                            }
                            humanPrefab.InitializeCharacter(newCharacter, spawnPos);
                            if (!TargetTag.IsEmpty && newCharacter != null)
                            {
                                ParentEvent.AddTarget(TargetTag, newCharacter);
                            }
                            spawnedEntity = newCharacter;
                        });
                    }
                }
            }
            else if (!SpeciesName.IsEmpty)
            {
                if (!AllowDuplicates && Character.CharacterList.Any(c => c.SpeciesName == SpeciesName))
                {
                    spawned = true;
                    return;
                }
                ISpatialEntity spawnPos = GetSpawnPos();
                if (spawnPos != null)
                {
                    Entity.Spawner.AddCharacterToSpawnQueue(SpeciesName, OffsetSpawnPos(spawnPos.WorldPosition, Offset), onSpawn: newCharacter =>
                    {
                        if (!TargetTag.IsEmpty && newCharacter != null)
                        {
                            ParentEvent.AddTarget(TargetTag, newCharacter);
                        }
                        spawnedEntity = newCharacter;
                    });
                }
            }
            else if (!ItemIdentifier.IsEmpty)
            {
                if (MapEntityPrefab.FindByIdentifier(ItemIdentifier) is not ItemPrefab itemPrefab)
                {
                    DebugConsole.ThrowError("Error in SpawnAction (item prefab \"" + ItemIdentifier + "\" not found)");
                }
                else
                {
                    Inventory spawnInventory = null;
                    if (!TargetInventory.IsEmpty)
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
                        ISpatialEntity spawnPos = GetSpawnPos();
                        if (spawnPos != null)
                        {
                            Entity.Spawner.AddItemToSpawnQueue(itemPrefab, OffsetSpawnPos(spawnPos.WorldPosition, Offset), onSpawned: onSpawned);
                        }
                    }
                    else
                    {
                        Entity.Spawner.AddItemToSpawnQueue(itemPrefab, spawnInventory, onSpawned: onSpawned);
                    }
                    void onSpawned(Item newItem)
                    {
                        if (newItem != null)
                        {
                            if (!TargetTag.IsEmpty)
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
            if (!SpawnPointTag.IsEmpty)
            {
                List<Item> potentialItems = Item.ItemList.FindAll(it => IsValidSubmarineType(SpawnLocation, it.Submarine));
                var item = potentialItems.Where(it => it.HasTag(SpawnPointTag)).GetRandomUnsynced();
                if (item != null) { return item; }

                var target = ParentEvent.GetTargets(SpawnPointTag).Where(t => IsValidSubmarineType(SpawnLocation, t.Submarine)).GetRandomUnsynced();
                if (target != null) { return target; }
            }

            SpawnType? spawnPointType = null;
            if (!ignoreSpawnPointType) { spawnPointType = SpawnPointType; }

            return GetSpawnPos(SpawnLocation, spawnPointType, targetModuleTags, SpawnPointTag.ToEnumerable(), requireTaggedSpawnPoint: RequireSpawnPointTag);
        }

        private static bool IsValidSubmarineType(SpawnLocationType spawnLocation, Submarine submarine)
        {
            return spawnLocation switch
            {
                SpawnLocationType.Any => true,
                SpawnLocationType.MainSub => submarine == Submarine.MainSub,
                SpawnLocationType.MainPath => submarine == null,
                SpawnLocationType.Outpost => submarine is { Info: { IsOutpost: true } },
                SpawnLocationType.Wreck => submarine is { Info: { IsWreck: true } },
                SpawnLocationType.Ruin => submarine is { Info: { IsRuin: true } },
                SpawnLocationType.BeaconStation => submarine?.Info?.BeaconStationInfo != null,
                _ => throw new NotImplementedException(),
            };
        }

        public static WayPoint GetSpawnPos(SpawnLocationType spawnLocation, SpawnType? spawnPointType, IEnumerable<Identifier> moduleFlags = null, IEnumerable<Identifier> spawnpointTags = null, bool asFarAsPossibleFromAirlock = false, bool requireTaggedSpawnPoint = false)
        {
            bool requireHull = spawnLocation == SpawnLocationType.MainSub || spawnLocation == SpawnLocationType.Outpost;
            List<WayPoint> potentialSpawnPoints = WayPoint.WayPointList.FindAll(wp => IsValidSubmarineType(spawnLocation, wp.Submarine) && (wp.CurrentHull != null || !requireHull));
            
            potentialSpawnPoints = potentialSpawnPoints.FindAll(wp => wp.ConnectedDoor == null && wp.Ladders == null && !wp.isObstructed);

            if (moduleFlags != null && moduleFlags.Any())
            {
                List<WayPoint> spawnPoints = potentialSpawnPoints.Where(wp => wp.CurrentHull?.OutpostModuleTags.Any(moduleFlags.Contains) ?? false).ToList();
                if (spawnPoints.Any())
                {
                    potentialSpawnPoints = spawnPoints;
                }
            }

            if (spawnpointTags != null && spawnpointTags.Any())
            {
                var spawnPoints = potentialSpawnPoints
                    .Where(wp => spawnpointTags.Any(tag => wp.Tags.Contains(tag) && wp.ConnectedDoor == null && !wp.isObstructed));

                if (requireTaggedSpawnPoint || spawnPoints.Any())
                {
                    potentialSpawnPoints = spawnPoints.ToList();
                }
            }

            if (potentialSpawnPoints.None())
            {
                if (requireTaggedSpawnPoint && spawnpointTags != null && spawnpointTags.Any())
                {
                    DebugConsole.NewMessage($"Could not find a spawn point for a SpawnAction (spawn location: {spawnLocation} (tag: {string.Join(",", spawnpointTags)}), skipping.", color: Color.White);
                }
                else
                {
                    DebugConsole.ThrowError($"Could not find a spawn point for a SpawnAction (spawn location: {spawnLocation})");
                }
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
            var airlockSpawnPoints = potentialSpawnPoints.Where(wp => wp.CurrentHull?.OutpostModuleTags.Contains("airlock".ToIdentifier()) ?? false);
            if (airlockSpawnPoints.Count() < validSpawnPoints.Count())
            {
                validSpawnPoints = validSpawnPoints.Except(airlockSpawnPoints);
            }

            if (validSpawnPoints.None())
            {
                DebugConsole.ThrowError($"Could not find a spawn point of the correct type for a SpawnAction (spawn location: {spawnLocation}, type: {spawnPointType}, module flags: {((moduleFlags == null || !moduleFlags.Any()) ? "none" : string.Join(", ", moduleFlags))})");
                return potentialSpawnPoints.GetRandomUnsynced();
            }

            //avoid using waypoints if there's any actual spawnpoints available
            if (validSpawnPoints.Any(wp => wp.SpawnType != SpawnType.Path))
            {
                validSpawnPoints = validSpawnPoints.Where(wp => wp.SpawnType != SpawnType.Path);
            }

            //if not trying to spawn at a tagged spawnpoint, favor spawnpoints without tags
            if (spawnpointTags == null || spawnpointTags.None())
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
                return validSpawnPoints.GetRandomUnsynced();
            }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(spawned)} {nameof(SpawnAction)} -> (Spawned entity: {spawnedEntity.ColorizeObject()})";
        }
    }
}