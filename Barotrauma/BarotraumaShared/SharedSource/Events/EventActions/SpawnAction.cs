using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Spawns an entity (e.g. item, NPC, monster).
    /// </summary>
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
            BeaconStation,
            NearMainSub
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

        [Serialize(SpawnLocationType.Any, IsPropertySaveable.Yes, description: "Where should the entity spawn? This can be restricted further with the other spawn point options.")]
        public SpawnLocationType SpawnLocation { get; set; }

        [Serialize(SpawnType.Human, IsPropertySaveable.Yes, description: "Type of spawnpoint to spawn the entity at. Ignored if SpawnPointTag is set.")] 
        public SpawnType SpawnPointType { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of a spawnpoint to spawn the entity at.")]
        public Identifier SpawnPointTag { get; set; }

        [Serialize(CharacterTeamType.FriendlyNPC, IsPropertySaveable.Yes, description: "Team of the NPC to spawn. Only valid when spawning a character.")]
        public CharacterTeamType TeamID { get; protected set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should we spawn the entity even when no spawn points with matching tags were found?")]
        public bool RequireSpawnPointTag { get; set; }

        private readonly HashSet<Identifier> targetModuleTags = new HashSet<Identifier>();

        [Serialize(true, IsPropertySaveable.Yes, description: "If false, we won't spawn another character if one with the same identifier has already been spawned.")]
        public bool AllowDuplicates { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "Number of entities to spawn.")]
        public int Amount { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes, description: "Random offset to add to the spawn position.")]
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

        [Serialize(true, IsPropertySaveable.Yes, description: "If disabled, the action will choose a spawn position away from players' views if one is available.")]
        public bool AllowInPlayerView { get; set; }

        private bool spawned;
        private Entity spawnedEntity;

        private readonly bool ignoreSpawnPointType;

        public SpawnAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            ignoreSpawnPointType = element.GetAttribute("spawnpointtype") == null;
            //backwards compatibility
            TeamID = element.GetAttributeEnum("teamtag", element.GetAttributeEnum("team", TeamID));
            if (element.GetAttribute("submarinetype") != null)
            {
                DebugConsole.ThrowError(
                    $"Error in even \"{(parentEvent.Prefab?.Identifier.ToString() ?? "unknown")}\". " +
                    $"The attribute \"submarinetype\" is not valid in {nameof(SpawnAction)}. Did you mean {nameof(SpawnLocation)}?",
                    contentPackage: ParentEvent.Prefab.ContentPackage);
            }
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
                HumanPrefab humanPrefab = null;
                if (Level.Loaded?.StartLocation is Location startLocation)
                {
                    humanPrefab = 
                        TryFindHumanPrefab(startLocation.Faction) ?? 
                        TryFindHumanPrefab(startLocation.SecondaryFaction);
                }
                HumanPrefab TryFindHumanPrefab(Faction faction)
                {
                    if (faction == null) { return null; }
                    return 
                        NPCSet.Get(NPCSetIdentifier, 
                        NPCIdentifier.Replace("[faction]".ToIdentifier(), faction.Prefab.Identifier), 
                        logError: false) ??
                        //try to spawn a coalition NPC if a correct one can't be found
                        NPCSet.Get(NPCSetIdentifier,
                        NPCIdentifier.Replace("[faction]".ToIdentifier(), "coalition".ToIdentifier()),
                        logError: false);
                }

                humanPrefab ??= NPCSet.Get(NPCSetIdentifier, NPCIdentifier, logError: true);

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
                        for (int i = 0; i < Amount; i++)
                        {
                            Entity.Spawner.AddCharacterToSpawnQueue(CharacterPrefab.HumanSpeciesName, OffsetSpawnPos(spawnPos.WorldPosition, Rand.Range(0.0f, Offset)), humanPrefab.CreateCharacterInfo(), onSpawn: newCharacter =>
                            {
                                if (newCharacter == null) { return; }
                                newCharacter.HumanPrefab = humanPrefab;
                                newCharacter.TeamID = TeamID;
                                newCharacter.EnableDespawn = false;
                                humanPrefab.GiveItems(newCharacter, newCharacter.Submarine, spawnPos as WayPoint);
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
                                if (Level.Loaded?.StartOutpost?.Info is { } outPostInfo)
                                {
                                    outPostInfo.AddOutpostNPCIdentifierOrTag(newCharacter, humanPrefab.Identifier);
                                    foreach (Identifier tag in humanPrefab.GetTags())
                                    {
                                        outPostInfo.AddOutpostNPCIdentifierOrTag(newCharacter, tag);
                                    }
                                }
#if SERVER
                                newCharacter.LoadTalents();
                                GameMain.NetworkMember.CreateEntityEvent(newCharacter, new Character.UpdateTalentsEventData());
#endif
                            });
                        }                        
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
                    for (int i = 0; i < Amount; i++)
                    {
                        Entity.Spawner.AddCharacterToSpawnQueue(SpeciesName, OffsetSpawnPos(spawnPos.WorldPosition, Rand.Range(0.0f, Offset)), onSpawn: newCharacter =>
                        {
                            if (!TargetTag.IsEmpty && newCharacter != null)
                            {
                                ParentEvent.AddTarget(TargetTag, newCharacter);
                            }
                            spawnedEntity = newCharacter;
                        });
                    }
                }
            }
            else if (!ItemIdentifier.IsEmpty)
            {
                if (MapEntityPrefab.FindByIdentifier(ItemIdentifier) is not ItemPrefab itemPrefab)
                {
                    DebugConsole.ThrowError("Error in SpawnAction (item prefab \"" + ItemIdentifier + "\" not found)",
                        contentPackage: ParentEvent.Prefab.ContentPackage);
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
                            DebugConsole.ThrowError($"Could not spawn \"{ItemIdentifier}\" in target inventory \"{TargetInventory}\" - matching target not found.",
                                contentPackage: ParentEvent.Prefab.ContentPackage);
                        }
                    }

                    if (spawnInventory == null)
                    {
                        ISpatialEntity spawnPos = GetSpawnPos();
                        if (spawnPos != null)
                        {
                            for (int i = 0; i < Amount; i++)
                            {
                                Entity.Spawner.AddItemToSpawnQueue(itemPrefab, OffsetSpawnPos(spawnPos.WorldPosition, Rand.Range(0.0f, Offset)), onSpawned: onSpawned);
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < Amount; i++)
                        {
                            Entity.Spawner.AddItemToSpawnQueue(itemPrefab, spawnInventory, onSpawned: onSpawned);

                        }
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

        public static Vector2 OffsetSpawnPos(Vector2 pos, float offset)
        {
            Hull hull = Hull.FindHull(pos);            
            pos += Rand.Vector(offset);
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
                IEnumerable<Item> potentialItems = Item.ItemList.Where(it => IsValidSubmarineType(SpawnLocation, it.Submarine));
                if (!AllowInPlayerView)
                {
                    potentialItems = GetEntitiesNotInPlayerView(potentialItems);
                }
                var item = potentialItems.Where(it => it.HasTag(SpawnPointTag)).GetRandomUnsynced();
                if (item != null) { return item; }

                var potentialTargets = ParentEvent.GetTargets(SpawnPointTag).Where(t => IsValidSubmarineType(SpawnLocation, t.Submarine));
                if (!AllowInPlayerView)
                {
                    potentialTargets = GetEntitiesNotInPlayerView(potentialTargets);
                }
                var target = potentialTargets.GetRandomUnsynced();
                if (target != null) { return target; }
            }

            SpawnType? spawnPointType = null;
            if (!ignoreSpawnPointType) { spawnPointType = SpawnPointType; }

            return GetSpawnPos(SpawnLocation, spawnPointType, targetModuleTags, SpawnPointTag.ToEnumerable(), requireTaggedSpawnPoint: RequireSpawnPointTag, allowInPlayerView: AllowInPlayerView);
        }

        private static bool IsValidSubmarineType(SpawnLocationType spawnLocation, Submarine submarine)
        {
            return spawnLocation switch
            {
                SpawnLocationType.Any => true,
                SpawnLocationType.MainSub => submarine == Submarine.MainSub,
                SpawnLocationType.NearMainSub => submarine == null,
                SpawnLocationType.MainPath => submarine == null,
                SpawnLocationType.Outpost => submarine is { Info.IsOutpost: true },
                SpawnLocationType.Wreck => submarine is { Info.IsWreck: true },
                SpawnLocationType.Ruin => submarine is { Info.IsRuin: true },
                SpawnLocationType.BeaconStation => submarine?.Info?.BeaconStationInfo != null,
                _ => throw new NotImplementedException(),
            };
        }

        /// <summary>
        /// Returns those of the entities that aren't in any player's view. If there are none, all the entities are returned.
        /// </summary>
        private static IEnumerable<T> GetEntitiesNotInPlayerView<T>(IEnumerable<T> entities) where T : ISpatialEntity
        {
            if (entities.Any(e => !IsInPlayerView(e)))
            {
                return entities.Where(e => !IsInPlayerView(e));
            }
            return entities;
        }

        private static bool IsInPlayerView(ISpatialEntity entity)
        {
            foreach (var character in Character.CharacterList)
            {
                if (!character.IsPlayer || character.IsDead) { continue; }
                if (character.CanSeeTarget(entity)) { return true; }
            }
            return false;
        }

        public static WayPoint GetSpawnPos(SpawnLocationType spawnLocation, SpawnType? spawnPointType, IEnumerable<Identifier> moduleFlags = null, IEnumerable<Identifier> spawnpointTags = null, bool asFarAsPossibleFromAirlock = false, bool requireTaggedSpawnPoint = false, bool allowInPlayerView = true)
        {
            bool requireHull = spawnLocation == SpawnLocationType.MainSub || spawnLocation == SpawnLocationType.Outpost;
            List<WayPoint> potentialSpawnPoints = WayPoint.WayPointList.FindAll(wp => IsValidSubmarineType(spawnLocation, wp.Submarine) && (wp.CurrentHull != null || !requireHull));           
            potentialSpawnPoints = potentialSpawnPoints.FindAll(wp => wp.ConnectedDoor == null && wp.Ladders == null && wp.IsTraversable);
            if (moduleFlags != null && moduleFlags.Any())
            {
                var spawnPoints = potentialSpawnPoints.Where(wp => wp.CurrentHull is Hull h && h.OutpostModuleTags.Any(moduleFlags.Contains));
                if (spawnPoints.Any())
                {
                    potentialSpawnPoints = spawnPoints.ToList();
                }
            }
            if (spawnpointTags != null && spawnpointTags.Any())
            {
                var spawnPoints = potentialSpawnPoints.Where(wp => spawnpointTags.Any(tag => wp.Tags.Contains(tag) && wp.ConnectedDoor == null && wp.IsTraversable));
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

            if (spawnLocation == SpawnLocationType.MainPath || spawnLocation == SpawnLocationType.NearMainSub)
            {
                validSpawnPoints = validSpawnPoints.Where(p => 
                    Submarine.Loaded.None(s => ToolBox.GetWorldBounds(s.Borders.Center, s.Borders.Size).ContainsWorld(p.WorldPosition)));
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

            if (!allowInPlayerView)
            {
                validSpawnPoints = GetEntitiesNotInPlayerView(validSpawnPoints);
            }

            if (spawnLocation == SpawnLocationType.NearMainSub && Submarine.MainSub != null)
            {
                WayPoint closestPoint = validSpawnPoints.First();
                float closestDist = float.PositiveInfinity;
                foreach (WayPoint wp in validSpawnPoints)
                {
                    float dist = Vector2.DistanceSquared(wp.WorldPosition, Submarine.MainSub.WorldPosition);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestPoint = wp;
                    }
                }
                return closestPoint;
            }
            else if (asFarAsPossibleFromAirlock && airlockSpawnPoints.Any())
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