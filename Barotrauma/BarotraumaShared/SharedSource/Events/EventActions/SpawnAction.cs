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
    public class SpawnAction : EventAction
    {
        public enum SpawnLocationType
        {
            Any,
            /// <summary>
            /// Spawnpoint inside the main submarine.
            /// </summary>
            MainSub,
            /// <summary>
            /// Spawnpoint inside an outpost.
            /// </summary>
            Outpost,
            /// <summary>
            /// Spawnpoint outside an outpost.
            /// </summary>
            OutsideOutpost,
            /// <summary>
            /// Spawnpoint on the main path through the level.
            /// </summary>
            MainPath,
            /// <summary>
            /// Spawnpoint in a cave. Only valid if there are caves in the level.
            /// </summary>
            Cave,
            /// <summary>
            /// Spawnpoint in an abyss cave. Only valid if there are abyss caves in the level.
            /// </summary>
            AbyssCave,
            /// <summary>
            /// Spawnpoint in a ruin. Only valid if there are ruins in the level.
            /// </summary>
            Ruin,
            /// <summary>
            /// Spawnpoint in a wreck. Only valid if there are wrecks in the level.
            /// </summary>
            Wreck,
            /// <summary>
            /// Spawnpoint in a beacon station. Only valid if there are beacon stations in the level.
            /// </summary>
            BeaconStation,
            /// <summary>
            /// A spawnpoint on the main path through the level. The difference to the <see cref="MainPath"/> type is that the closest possible spawnpoint is chosen.
            /// </summary>
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

        [Serialize(false, IsPropertySaveable.Yes, description: "If this is a creature, should creature saving in the campaign file be disabled for this character?")]
        public bool DisableCreatureSaving { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the item to spawn.")]
        public Identifier ItemIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the item to spawn.")]
        public Identifier ItemTag { get; set; }

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

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the spawned creature ignore the submarine and any connected subs that it's spawned on?")]
        public bool IgnoreOwnSubmarine { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the spawned creature ignore outpost submarines and any connected subs to it?")]
        public bool IgnoreOutpost { get; set; }

        [Serialize(CharacterTeamType.None, IsPropertySaveable.Yes, description: "Makes the creature ignore all submarines that have the specified team.")]
        public CharacterTeamType IgnoreSubmarineTeam { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, description: "Minimum distance from the outpost that the creature is allowed to spawn.")]
        public float MinDistanceFromOutpost { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should we spawn the entity even when no spawn points with matching tags were found?")]
        public bool RequireSpawnPointTag { get; set; }

        private readonly HashSet<Identifier> targetModuleTags = new HashSet<Identifier>();

        [Serialize(true, IsPropertySaveable.Yes, description: "If false, we won't spawn another character if one with the same identifier has already been spawned.")]
        public bool AllowDuplicates { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "Number of entities to spawn.")]
        public int Amount { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the item be spawned even if the target inventory is full (just spawning it at the position of the target)? Only valid if spawning an item in an inventory.")]
        public bool SpawnIfInventoryFull { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes, description: "Random offset to add to the spawn position.")]
        public float Offset { get; set; }

        [Serialize(default, IsPropertySaveable.Yes, description: "Position offset to be added to the spawn position.")]
        public Vector2 PosOffset { get; set; }

        [Serialize(InvSlotType.None, IsPropertySaveable.Yes, description: "Specifies which slots the item allowed to go in when spawning inside an inventory.")]
        public InvSlotType AllowedSlots { get; set; }

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

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the event continue even if the entity failed to spawn for whatever reason?")]
        public bool ContinueIfFailedToSpawn { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Forces the spawned character to be friendly towards the specified group. Note: normal team-friendliness logic still applies.")]
        public Identifier ForceFriendlyWithGroup { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, the spawning only happens in multiplayer, and the action does nothing in single player. Useful for e.g. spawning more enemies in some event in multiplayer.")]
        public bool MultiplayerOnly { get; set; }

        [Serialize(0, IsPropertySaveable.Yes, description: "The spawning only happens if there's at least this many players in the server. Players opted to spectate/AFK don't count. In single player, the player count is assumed to be one, and any value above 1 will mean the action does nothing in single player.")]
        public int MinPlayerCount { get; set; }

        private bool spawned;
        private Entity spawnedEntity;

        private readonly bool ignoreSpawnPointType;

        private readonly bool ignoreSubmarineTeamSet;

        public SpawnAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            ignoreSubmarineTeamSet = element.GetAttribute(nameof(IgnoreSubmarineTeam)) != null;
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

        private bool PlayerCountAndMultiplayerRequirementsMet()
        {
            if (MultiplayerOnly)
            {
                if (GameMain.GameSession?.GameMode is { IsSinglePlayer: true })
                {
                    return false;
                }
            }
            if (MinPlayerCount > 1)
            {
                if (GameMain.NetworkMember == null) { return false; }
#if SERVER
                if (GameMain.Server.GetPlayingClients().Count < MinPlayerCount) { return false; }
#else
                //client code: the clients shouldn't be executing actions or spawning anything, so we can treat this as a failure to meet the requirements
                return false;
#endif
            }
            return true;
        }

        public override bool IsFinished(ref string goTo)
        {
            if (!PlayerCountAndMultiplayerRequirementsMet())
            {
                return true;
            }

            if (spawnedEntity != null || ContinueIfFailedToSpawn)
            {
                return spawned;
            }
            else
            {
                return false;
            }
        }

        public override void Reset(bool shouldEndConversations = true)
        {
            spawned = false;
            spawnedEntity = null;
        }

        public override void Update(float deltaTime)
        {
            if (!PlayerCountAndMultiplayerRequirementsMet())
            {
                return;
            }

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
                    if (faction == null)
                    {
                        return
                            //try to find an NPC without a faction specified
                            NPCSet.Get(NPCSetIdentifier, NPCIdentifier.Replace("[faction]".ToIdentifier(), "".ToIdentifier()), logError: false) ??
                            //try to spawn a coalition NPC if a correct one can't be found
                            NPCSet.Get(NPCSetIdentifier, NPCIdentifier.Replace("[faction]".ToIdentifier(), "coalition".ToIdentifier()), logError: false);
                    }
                    else
                    {
                        return 
                            NPCSet.Get(NPCSetIdentifier, NPCIdentifier.Replace("[faction]".ToIdentifier(), faction.Prefab.Identifier), logError: false) ??
                            //try to spawn a coalition NPC if a correct one can't be found
                            NPCSet.Get(NPCSetIdentifier, NPCIdentifier.Replace("[faction]".ToIdentifier(), "coalition".ToIdentifier()), logError: false);
                    }
                }

                humanPrefab ??= NPCSet.Get(NPCSetIdentifier, NPCIdentifier, logError: true, contentPackageToLogInError: ParentEvent.Prefab.ContentPackage);

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
                            Entity.Spawner.AddCharacterToSpawnQueue(CharacterPrefab.HumanSpeciesName, OffsetSpawnPos(spawnPos.WorldPosition, Rand.Range(0.0f, Offset), PosOffset), humanPrefab.CreateCharacterInfo(), onSpawn: newCharacter =>
                            {
                                if (newCharacter == null) { return; }
                                newCharacter.DisableCreatureSaving = DisableCreatureSaving;
                                newCharacter.HumanPrefab = humanPrefab;
                                //don't set the TeamID directly: we want to leave the character's original team untouched,
                                //so they can behave offensively (and otherwise act "normally") if we spawn them in a hostile team inside a sub/outpost that doesn't belong to that team

                                //process the team change immediately in case the character is killed or made unconscious by the event (in which case the team change would not be processed)
                                newCharacter.SetOriginalTeamAndChangeTeam(TeamID, processImmediately: true);
                                newCharacter.EnableDespawn = false;
                                humanPrefab.GiveItems(newCharacter, newCharacter.Submarine, spawnPos as WayPoint);
                                if (LootingIsStealing)
                                {
                                    foreach (Item item in newCharacter.Inventory.FindAllItems(recursive: true))
                                    {
                                        item.SpawnedInNonPlayerOutpostOrSubmarine = true;
                                        item.AllowStealing = false;
                                    }
                                }
                                humanPrefab.InitializeCharacter(newCharacter, spawnPos);
                                if (!TargetTag.IsEmpty && newCharacter != null)
                                {
                                    ParentEvent.AddTarget(TargetTag, newCharacter);
                                }
                                spawnedEntity = newCharacter;
                                if (Level.Loaded?.StartOutpost?.Info is { } outpostInfo)
                                {
                                    outpostInfo.AddOutpostNPC(newCharacter);
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
                        Entity.Spawner.AddCharacterToSpawnQueue(SpeciesName, OffsetSpawnPos(spawnPos.WorldPosition, Rand.Range(0.0f, Offset), PosOffset), onSpawn: newCharacter =>
                        {
                            newCharacter.DisableCreatureSaving = DisableCreatureSaving;
                            newCharacter.ForceFriendlyWithGroup = ForceFriendlyWithGroup;

                            if (!TargetTag.IsEmpty && newCharacter != null)
                            {
                                ParentEvent.AddTarget(TargetTag, newCharacter);
                            }
                            spawnedEntity = newCharacter;
                            if (newCharacter is { AIController: EnemyAIController enemyAi })
                            {
                                List<Submarine> submarinesToIgnore = [];

                                if (IgnoreOutpost)
                                {
                                    Submarine.Loaded
                                        .Where(sub => sub.Info.IsOutpost)
                                        .ForEach(sub => enemyAi.SetUnattackableSubmarines(sub, clearExisting: false));
                                }

                                if (ignoreSubmarineTeamSet)
                                {
                                    Submarine.Loaded
                                        .Where(sub => sub.TeamID == IgnoreSubmarineTeam)
                                        .ForEach(sub => enemyAi.SetUnattackableSubmarines(sub, includeConnectedSubs: false, clearExisting: false));
                                }

                                if (newCharacter.Submarine != null && IgnoreOwnSubmarine)
                                {
                                    enemyAi.SetUnattackableSubmarines(newCharacter.Submarine, clearExisting: false);
                                }
                            }
                        });
                    }
                }
            }
            else if (!ItemIdentifier.IsEmpty || !ItemTag.IsEmpty)
            {
                ItemPrefab itemPrefab = null;
                if (!ItemIdentifier.IsEmpty)
                {
                    itemPrefab = MapEntityPrefab.FindByIdentifier(ItemIdentifier) as ItemPrefab;
                    if (itemPrefab == null)
                    {
                        DebugConsole.ThrowError($"Error in SpawnAction (item prefab \"{ItemIdentifier}\" not found)",
                            contentPackage: ParentEvent.Prefab.ContentPackage);
                    }
                }
                else if (!ItemTag.IsEmpty)
                {
                    itemPrefab = ItemPrefab.Prefabs.Where(ip => ip.Tags.Contains(ItemTag)).GetRandom(Rand.RandSync.Unsynced);
                }

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
                            Entity.Spawner.AddItemToSpawnQueue(itemPrefab, OffsetSpawnPos(spawnPos.WorldPosition, Rand.Range(0.0f, Offset), PosOffset), onSpawned: onSpawned);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < Amount; i++)
                    {
                        Entity.Spawner.AddItemToSpawnQueue(itemPrefab, spawnInventory, spawnIfInventoryFull: SpawnIfInventoryFull, onSpawned: onSpawned, slot: AllowedSlots);
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

            spawned = true;            
        }

        public static Vector2 OffsetSpawnPos(Vector2 pos, float randomOffset, Vector2 offset = default)
        {
            Hull hull = Hull.FindHull(pos);            
            pos += Rand.Vector(randomOffset);
            pos += offset;
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

                var potentialTargets = ParentEvent.GetTargets(SpawnPointTag)
                    //GetTargets returns waypoints by tag, but we're not interested in those at this point.
                    //Waypoints are handled in GetSpawnPos below, with some extra checks for e.g. waypoints being obstructed by subs
                    .Where(t => t is not WayPoint)
                    .Where(t => IsValidSubmarineType(SpawnLocation, t.Submarine));
                if (!AllowInPlayerView)
                {
                    potentialTargets = GetEntitiesNotInPlayerView(potentialTargets);
                }
                var target = potentialTargets.GetRandomUnsynced();
                if (target != null) { return target; }
            }

            SpawnType? spawnPointType = null;
            if (!ignoreSpawnPointType) { spawnPointType = SpawnPointType; }

            return GetSpawnPos(SpawnLocation, spawnPointType, targetModuleTags, SpawnPointTag.IsEmpty ? null : SpawnPointTag.ToEnumerable(), requireTaggedSpawnPoint: RequireSpawnPointTag, allowInPlayerView: AllowInPlayerView, minDistanceFromOutpost: MinDistanceFromOutpost,
                debugLogName: $"SpawnAction ({ParentEvent?.Prefab?.Identifier.Value ?? "null"})");
        }

        private static bool IsValidSubmarineType(SpawnLocationType spawnLocation, Submarine submarine)
        {
            return spawnLocation switch
            {
                SpawnLocationType.Any => true,
                SpawnLocationType.MainSub => submarine == Submarine.MainSub,
                SpawnLocationType.NearMainSub or SpawnLocationType.MainPath or SpawnLocationType.Cave or SpawnLocationType.AbyssCave => submarine == null,
                SpawnLocationType.Outpost => submarine is { Info.IsOutpost: true },
                SpawnLocationType.OutsideOutpost => submarine is { Info.IsOutpost: true },
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

        public static WayPoint GetSpawnPos(SpawnLocationType spawnLocation, SpawnType? spawnPointType, IEnumerable<Identifier> moduleFlags = null, IEnumerable<Identifier> spawnpointTags = null, 
            bool asFarAsPossibleFromAirlock = false, bool requireTaggedSpawnPoint = false, bool allowInPlayerView = true,
            string debugLogName = null, float minDistanceFromOutpost = 0f)
        {
            bool requireHull = spawnLocation == SpawnLocationType.MainSub || spawnLocation == SpawnLocationType.Outpost;
            bool requireNoHull = spawnLocation == SpawnLocationType.OutsideOutpost;

            IEnumerable<WayPoint> potentialSpawnPoints = WayPoint.WayPointList
                .FindAll(wp =>
                {
                    if (!IsValidSubmarineType(spawnLocation, wp.Submarine)) { return false; }
                    //explicitly tagged spawnpoints are allowed outside hulls (e.g. a spawnpoint tagged for spawning a monster outside an outpost)
                    bool hasRequestedTag = spawnpointTags != null && spawnpointTags.Any(tag => wp.Tags.Contains(tag));
                    if (wp.CurrentHull == null && requireHull && !hasRequestedTag) { return false; }
                    if (wp.CurrentHull != null && requireNoHull) { return false; }
                    //if the waypoint belongs to a sub (e.g. spawnpoint placed outside a sub or an outpost),
                    //but is inside another sub, exclude it
                    if (wp.Submarine != null &&
                        wp.CurrentHull?.Submarine != wp.Submarine &&
                        Hull.FindHull(wp.WorldPosition, useWorldCoordinates: true) is Hull insideHull &&
                        insideHull.Submarine != wp.Submarine)
                    {
                        return false;
                    }
                    if (wp.CurrentHull == null &&
                        Level.Loaded != null &&
                        Level.Loaded.IsPositionInsideWall(wp.WorldPosition))
                    {
                        return false;
                    }
                    return true;
                });      
            
            potentialSpawnPoints = potentialSpawnPoints.Where(wp => wp.ConnectedDoor == null && wp.Ladders == null && wp.IsTraversable);

            //find spawnpoints with the desired type, or any random spawnpoints if not specified
            IEnumerable<WayPoint> spawnPointsWithCorrectType;
            if (spawnPointType.HasValue)
            {
                spawnPointsWithCorrectType = potentialSpawnPoints.Where(wp =>
                    spawnPointType.Value.HasFlag(wp.SpawnType) &&
                    //need to handle zero (SpawnType.Path) separately, because spawnPointType will always have the flag 0
                    (wp.SpawnType != 0 || spawnPointType.Value == 0));
            }
            else
            {
                spawnPointsWithCorrectType = potentialSpawnPoints;
            }
            if (spawnPointsWithCorrectType.Any())
            {
                potentialSpawnPoints = spawnPointsWithCorrectType;
            }

            //with correct module flags, if there are any
            if (moduleFlags != null && moduleFlags.Any())
            {
                var spawnPointsWithCorrectFlags = potentialSpawnPoints.Where(wp => (wp.CurrentHull is Hull h && h.OutpostModuleTags.Any(moduleFlags.Contains)) || wp.Tags.Any(moduleFlags.Contains));
                Identifier noneTag = "none".ToIdentifier();
                if (spawnPointsWithCorrectFlags.Any())
                {
                    potentialSpawnPoints = spawnPointsWithCorrectFlags.ToList();
                }
                //"none" seems to be used as "I don't care where this NPC spawns" in the configs, let's not log a warning about a module with the flag "none" not being found
                else if (moduleFlags.Any( f => f != noneTag))
                {
                    DebugConsole.AddWarning($"Could not find any modules with the flag(s) \"{string.Join(", ", moduleFlags)}\" when trying to choose a spawn position for {debugLogName ?? "unknown"}).");
                }
            }

            //with correct spawn point tags, if there are any
            if (spawnpointTags != null && spawnpointTags.Any())
            {
                var spawnPointsWithTag = potentialSpawnPoints.Where(wp => spawnpointTags.Any(tag => wp.Tags.Contains(tag) && wp.ConnectedDoor == null && wp.IsTraversable));
                if (requireTaggedSpawnPoint || spawnPointsWithTag.Any())
                {
                    potentialSpawnPoints = spawnPointsWithTag.ToList();
                }
                else
                {
                    //no spawnpoints with the tag we want -> choose something with no tags
                    TryGetSpawnPointsWithNoTag();
                }
            }
            else
            {
                //if no tags are specified, prefer a spawnpoint with no tags, i.e. prefer a "generic" spawnpoint instead of some special one like a jail spawnpoint
                TryGetSpawnPointsWithNoTag();
            }

            void TryGetSpawnPointsWithNoTag()
            {
                var spawnPointsWithNoTag = potentialSpawnPoints.Where(wp => wp.Tags.None());
                if (spawnPointsWithNoTag.Any())
                {
                    potentialSpawnPoints = spawnPointsWithNoTag.ToList();
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

            //spawnpoints that match the desired criteria found, choose the best one next
            //  preferring non-path spawnpoints if there's any available
            var nonPathSpawnPoints = potentialSpawnPoints.Where(wp => wp.SpawnType != SpawnType.Path);
            var validSpawnPoints =
                nonPathSpawnPoints.Any() && spawnPointType != SpawnType.Path ?
                nonPathSpawnPoints :
                potentialSpawnPoints;

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

            switch (spawnLocation)
            {
                case SpawnLocationType.MainPath:
                case SpawnLocationType.NearMainSub:                    
                    validSpawnPoints = validSpawnPoints.Where(p =>
                        Submarine.FindContaining(p.WorldPosition, inflate: 0) == null);
                    if (Level.Loaded != null)
                    {
                        validSpawnPoints = validSpawnPoints.Where(p =>
                            p.WorldPosition.Y > Level.Loaded.AbyssStart &&
                            p.Cave == null && p.Ruin == null);
                    }
                    break;
                case SpawnLocationType.Cave:
                    validSpawnPoints = validSpawnPoints.Where(p => p.WorldPosition.Y > Level.Loaded.AbyssStart && p.Cave != null);
                    break;
                case SpawnLocationType.AbyssCave:
                    validSpawnPoints = validSpawnPoints.Where(p => p.WorldPosition.Y < Level.Loaded.AbyssStart && p.Cave != null);
                    break;
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

            if (minDistanceFromOutpost > 0f && Level.Loaded.StartOutpost is { } outpost)
            {
                var spawnPoints = validSpawnPoints
                    .Where(wp => Vector2.DistanceSquared(wp.WorldPosition, outpost.WorldPosition) > minDistanceFromOutpost * minDistanceFromOutpost);

                if (spawnPoints.Any())
                {
                    validSpawnPoints = spawnPoints.ToList();
                }
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