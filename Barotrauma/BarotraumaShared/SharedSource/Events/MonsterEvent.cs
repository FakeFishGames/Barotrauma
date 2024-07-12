using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;

namespace Barotrauma
{
    class MonsterEvent : Event
    {
        /// <summary>
        /// The name of the species to spawn
        /// </summary>
        public readonly Identifier SpeciesName;

        /// <summary>
        /// Minimum amount of monsters to spawn. You can also use "Amount" if you want to spawn a fixed number of monsters.
        /// </summary>
        public readonly int MinAmount;
        /// <summary>
        /// Maximum amount of monsters to spawn. You can also use "Amount" if you want to spawn a fixed number of monsters.
        /// </summary>
        public readonly int MaxAmount;

        private readonly List<Character> monsters = new List<Character>();

        /// <summary>
        /// The monsters are spawned at least this distance away from the players and submarines.
        /// </summary>
        public readonly float SpawnDistance;

        /// <summary>
        /// Amount of random variance in the spawn position, in pixels. Can be used to prevent all the monsters from spawning at the exact same position.
        /// </summary>
        private readonly float scatter;

        /// <summary>
        /// Used for offsetting the spawns towards the end position of the level, so that they spawn farther afront the sub than normally. In pixels.
        /// </summary>
        private readonly float offset;

        /// <summary>
        /// Delay between spawning the monsters. Only relevant if the event spawns more than one monster.
        /// </summary>
        private readonly float delayBetweenSpawns;

        /// <summary>
        /// Number seconds before the event resets after all the monsters are dead. Can be used to make the event spawn monsters multiple times.
        /// </summary>
        private float resetTime;
        private float resetTimer;

        private Vector2? spawnPos;

        private bool disallowed;

        /// <summary>
        /// Where should the monster spawn?
        /// </summary>
        public readonly Level.PositionType SpawnPosType;

        /// <summary>
        /// If set, the monsters will spawn at a spawnpoint that has this tag. Only relevant for events that spawn monsters in a submarine, beacon station, wreck, outpost or ruin.
        /// </summary>
        private readonly string spawnPointTag;

        private bool spawnPending, spawnReady;

        /// <summary>
        /// Maximum number of the specific type of monster in the entire level. Can be used to prevent the event from spawning more monsters if there's
        /// already enough of that type of monster, e.g. spawned by another event or by a mission.
        /// </summary>
        public readonly int MaxAmountPerLevel = int.MaxValue;

        public IReadOnlyList<Character> Monsters => monsters;
        public Vector2? SpawnPos => spawnPos;
        public bool SpawnPending => spawnPending;

        public override Vector2 DebugDrawPos
        {
            get { return spawnPos ?? Vector2.Zero; }
        }

        public override string ToString()
        {
            if (MaxAmount <= 1)
            {
                return $"MonsterEvent ({SpeciesName}, {SpawnPosType})";
            }
            else if (MinAmount < MaxAmount)
            {
                return $"MonsterEvent ({SpeciesName} x{MinAmount}-{MaxAmount}, {SpawnPosType})";
            }
            else
            {
                return $"MonsterEvent ({SpeciesName} x{MaxAmount}, {SpawnPosType})";
            }
        }

        public MonsterEvent(EventPrefab prefab, int seed)
            : base(prefab, seed)
        {
            string speciesFile = prefab.ConfigElement.GetAttributeString("characterfile", "");
            CharacterPrefab characterPrefab = CharacterPrefab.FindByFilePath(speciesFile);
            if (characterPrefab != null)
            {
                SpeciesName = characterPrefab.Identifier;
            }
            else
            {
                SpeciesName = speciesFile.ToIdentifier();
            }

            if (SpeciesName.IsEmpty)
            {
                throw new Exception("speciesname is null!");
            }

            int defaultAmount = prefab.ConfigElement.GetAttributeInt("amount", 1);
            MinAmount = prefab.ConfigElement.GetAttributeInt("minamount", defaultAmount);
            MaxAmount = Math.Max(prefab.ConfigElement.GetAttributeInt("maxamount", 1), MinAmount);

            MaxAmountPerLevel = prefab.ConfigElement.GetAttributeInt("maxamountperlevel", int.MaxValue);

            SpawnPosType = prefab.ConfigElement.GetAttributeEnum("spawntype", Level.PositionType.MainPath);
            //backwards compatibility
            if (prefab.ConfigElement.GetAttributeBool("spawndeep", false))
            {
                SpawnPosType = Level.PositionType.Abyss;
            }

            spawnPointTag = prefab.ConfigElement.GetAttributeString("spawnpointtag", string.Empty);
            SpawnDistance = prefab.ConfigElement.GetAttributeFloat("spawndistance", 0);
            offset = prefab.ConfigElement.GetAttributeFloat("offset", 0);
            scatter = Math.Clamp(prefab.ConfigElement.GetAttributeFloat("scatter", 500), 0, 3000);
            delayBetweenSpawns = prefab.ConfigElement.GetAttributeFloat("delaybetweenspawns", 0.1f);
            resetTime = prefab.ConfigElement.GetAttributeFloat("resettime", 0);

            if (GameMain.NetworkMember != null)
            {
                List<Identifier> monsterNames = GameMain.NetworkMember.ServerSettings.MonsterEnabled.Keys.ToList();
                Identifier tryKey = monsterNames.Find(s => SpeciesName == s);

                if (!tryKey.IsEmpty)
                {
                    if (!GameMain.NetworkMember.ServerSettings.MonsterEnabled[tryKey])
                    {
                        disallowed = true; //spawn was disallowed by host
                    }
                }
            }
        }

        private static Submarine GetReferenceSub()
        {
            return EventManager.GetRefEntity() as Submarine ?? Submarine.MainSub;
        }

        public override IEnumerable<ContentFile> GetFilesToPreload()
        {
            var file = CharacterPrefab.FindBySpeciesName(SpeciesName)?.ContentFile;
            if (file == null)
            {
                DebugConsole.ThrowError($"Failed to find config file for species \"{SpeciesName}\".", 
                    contentPackage: Prefab.ContentPackage);
                yield break;
            }
            else
            {
                yield return file;
            }
        }

        protected override void InitEventSpecific(EventSet parentSet)
        {
            if (parentSet != null && resetTime == 0)
            {
                // Use the parent reset time only if there's no reset time defined for the event.
                resetTime = parentSet.ResetTime;
            }
            if (GameSettings.CurrentConfig.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized MonsterEvent (" + SpeciesName + ")", Color.White);
            }

            monsters.Clear();

            //+1 because Range returns an integer less than the max value
            int amount = Rand.Range(MinAmount, MaxAmount + 1);
            for (int i = 0; i < amount; i++)
            {
                string seed = i.ToString() + Level.Loaded.Seed;
                Character createdCharacter = Character.Create(SpeciesName, Vector2.Zero, seed, characterInfo: null, isRemotePlayer: false, hasAi: true, createNetworkEvent: true, throwErrorIfNotFound: false);
                if (createdCharacter == null)
                {
                    DebugConsole.AddWarning($"Error in MonsterEvent: failed to spawn the character \"{SpeciesName}\". Content package: \"{prefab.ConfigElement?.ContentPackage?.Name ?? "unknown"}\".",
                        Prefab.ContentPackage);
                    disallowed = true;
                    continue;
                }
                if (GameMain.GameSession.IsCurrentLocationRadiated())
                {
                    AfflictionPrefab radiationPrefab = AfflictionPrefab.RadiationSickness;
                    Affliction affliction = new Affliction(radiationPrefab, radiationPrefab.MaxStrength);
                    createdCharacter?.CharacterHealth.ApplyAffliction(null, affliction);
                    // TODO test multiplayer
                    createdCharacter?.Kill(CauseOfDeathType.Affliction, affliction, log: false);
                }
                createdCharacter.DisabledByEvent = true;
                monsters.Add(createdCharacter);
            }
        }

        public override string GetDebugInfo()
        {
            return 
                $"Finished: {IsFinished.ColorizeObject()}\n" +
                $"Amount: {MinAmount.ColorizeObject()} - {MaxAmount.ColorizeObject()}\n" +
                $"Spawn pending: {SpawnPending.ColorizeObject()}\n" +
                $"Spawn position: {SpawnPos.ColorizeObject()}";
        }

        private List<Level.InterestingPosition> GetAvailableSpawnPositions()
        {
            var availablePositions = Level.Loaded.PositionsOfInterest.FindAll(p => SpawnPosType.HasFlag(p.PositionType));
            var removals = new List<Level.InterestingPosition>();
            foreach (var position in availablePositions)
            {
                if (SpawnPosFilter != null && !SpawnPosFilter(position))
                {
                    removals.Add(position);
                    continue;
                }
                if (position.Submarine != null)
                {
                    if (position.Submarine.WreckAI != null && position.Submarine.WreckAI.IsAlive)
                    {
                        removals.Add(position);
                    }
                    else
                    {
                        continue;
                    }
                }
                if (position.PositionType != Level.PositionType.MainPath && 
                    position.PositionType != Level.PositionType.SidePath) 
                { 
                    continue; 
                }
                if (Level.Loaded.IsPositionInsideWall(position.Position.ToVector2()))
                {
                    removals.Add(position);
                }
                if (position.Position.Y < Level.Loaded.GetBottomPosition(position.Position.X).Y)
                {
                    removals.Add(position);
                }
            }
            removals.ForEach(r => availablePositions.Remove(r));         
            return availablePositions;
        }

        private Level.InterestingPosition chosenPosition;
        private void FindSpawnPosition(bool affectSubImmediately)
        {
            if (disallowed) { return; }

            spawnPos = Vector2.Zero;
            var availablePositions = GetAvailableSpawnPositions();
            chosenPosition = new Level.InterestingPosition(Point.Zero, Level.PositionType.MainPath, isValid: false);
            bool isRuinOrWreckOrCave = 
                SpawnPosType.HasFlag(Level.PositionType.Ruin) || 
                SpawnPosType.HasFlag(Level.PositionType.Wreck) ||
                SpawnPosType.HasFlag(Level.PositionType.Cave) || 
                SpawnPosType.HasFlag(Level.PositionType.AbyssCave);
            if (affectSubImmediately && !isRuinOrWreckOrCave && !SpawnPosType.HasFlag(Level.PositionType.Abyss))
            {
                if (availablePositions.None())
                {
                    //no suitable position found, disable the event
                    spawnPos = null;
                    disallowed = true;
                    return;
                }
                Submarine refSub = GetReferenceSub();
                if (Submarine.MainSubs.Length == 2 && Submarine.MainSubs[1] != null)
                {
                    refSub = Submarine.MainSubs.GetRandom(Rand.RandSync.Unsynced);
                }
                float closestDist = float.PositiveInfinity;
                //find the closest spawnposition that isn't too close to any of the subs
                foreach (var position in availablePositions)
                {
                    Vector2 pos = position.Position.ToVector2();
                    float dist = Vector2.DistanceSquared(pos, refSub.WorldPosition);
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub.Info.Type != SubmarineType.Player &&
                            sub.Info.Type != SubmarineType.EnemySubmarine &&
                            sub != GameMain.NetworkMember?.RespawnManager?.RespawnShuttle) 
                        { 
                            continue; 
                        }
                        
                        float minDistToSub = GetMinDistanceToSub(sub);
                        if (dist < minDistToSub * minDistToSub) { continue; }

                        if (closestDist == float.PositiveInfinity)
                        {
                            closestDist = dist;
                            chosenPosition = position;
                            continue;
                        }

                        //chosen position behind the sub -> override with anything that's closer or to the right
                        if (chosenPosition.Position.X < refSub.WorldPosition.X)
                        {
                            if (dist < closestDist || pos.X > refSub.WorldPosition.X)
                            {
                                closestDist = dist;
                                chosenPosition = position;
                            }
                        }
                        //chosen position ahead of the sub -> only override with a position that's also ahead
                        else if (chosenPosition.Position.X > refSub.WorldPosition.X)
                        {
                            if (dist < closestDist && pos.X > refSub.WorldPosition.X)
                            {
                                closestDist = dist;
                                chosenPosition = position;
                            }
                        }                        
                    }
                }
                //only found a spawnpos that's very far from the sub, pick one that's closer
                //and wait for the sub to move further before spawning
                if (closestDist > 15000.0f * 15000.0f)
                {
                    foreach (var position in availablePositions)
                    {
                        float dist = Vector2.DistanceSquared(position.Position.ToVector2(), refSub.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            chosenPosition = position;
                        }
                    }
                }
            }
            else
            {
                if (!isRuinOrWreckOrCave)
                {
                    float minDistance = 20000;
                    for (int i = 0; i < Submarine.MainSubs.Length; i++)
                    {
                        if (Submarine.MainSubs[i] == null) { continue; }
                        availablePositions.RemoveAll(p => Vector2.DistanceSquared(Submarine.MainSubs[i].WorldPosition, p.Position.ToVector2()) < minDistance * minDistance);
                    }
                }
                if (availablePositions.None())
                {
                    //no suitable position found, disable the event
                    spawnPos = null;
                    disallowed = true;
                    return;
                }
                chosenPosition = availablePositions.GetRandomUnsynced();
            }
            if (chosenPosition.IsValid)
            {
                spawnPos = chosenPosition.Position.ToVector2();
                if (chosenPosition.Submarine != null || chosenPosition.Ruin != null)
                {
                    var spawnPoint = WayPoint.GetRandom(SpawnType.Enemy, sub: chosenPosition.Submarine ?? chosenPosition.Ruin?.Submarine, useSyncedRand: false, spawnPointTag: spawnPointTag);
                    if (spawnPoint != null)
                    {
                        System.Diagnostics.Debug.Assert(spawnPoint.Submarine == (chosenPosition.Submarine ?? chosenPosition.Ruin?.Submarine));
                        spawnPos = spawnPoint.WorldPosition;
                    }
                    else
                    {
                        //no suitable position found, disable the event
                        spawnPos = null;
                        disallowed = true;
                        return;
                    }
                }
                else if (chosenPosition.PositionType == Level.PositionType.MainPath || chosenPosition.PositionType == Level.PositionType.SidePath)
                {
                    if (offset > 0)
                    {
                        var tunnelType = chosenPosition.PositionType == Level.PositionType.MainPath ? Level.TunnelType.MainPath : Level.TunnelType.SidePath;
                        var waypoints = WayPoint.WayPointList.FindAll(wp => 
                            wp.Submarine == null && 
                            wp.Ruin == null &&
                            wp.Tunnel?.Type == tunnelType &&
                            wp.WorldPosition.X > spawnPos.Value.X);

                        if (waypoints.None())
                        {
                            DebugConsole.AddWarning($"Failed to find a spawn position offset from {spawnPos.Value}.",
                                Prefab.ContentPackage);
                        }
                        else
                        {
                            float offsetSqr = offset * offset;
                            //find the waypoint whose distance from the spawnPos is closest to the desired offset
                            var targetWaypoint = waypoints.OrderBy(wp => 
                                Math.Abs(Vector2.DistanceSquared(wp.WorldPosition, spawnPos.Value) - offsetSqr)).FirstOrDefault();
                            if (targetWaypoint != null)
                            {
                                spawnPos = targetWaypoint.WorldPosition;
                            }
                        }
                    }
                    // Ensure that the position is not inside a submarine (in practice wrecks).
                    if (Submarine.Loaded.Any(s => ToolBox.GetWorldBounds(s.Borders.Center, s.Borders.Size).ContainsWorld(spawnPos.Value)))
                    {
                        //no suitable position found, disable the event
                        spawnPos = null;
                        disallowed = true;
                        return;
                    }
                }
                spawnPending = true;
            }
        }

        private float GetMinDistanceToSub(Submarine submarine) 
        {
            float minDist = Math.Max(Math.Max(submarine.Borders.Width, submarine.Borders.Height), Sonar.DefaultSonarRange * 0.9f);
            if (SpawnPosType.HasFlag(Level.PositionType.Abyss))
            {
                minDist *= 2;
            }
            return minDist;
        }

        public override void Update(float deltaTime)
        {
            if (disallowed) { return; }

            if (resetTimer > 0)
            {
                resetTimer -= deltaTime;
                if (resetTimer <= 0)
                {
                    if (ParentSet?.ResetTime > 0)
                    {
                        // If parent has reset time defined, the set is recreated. Otherwise we'll just reset this event.
                        Finish();
                    }
                    else
                    {
                        spawnReady = false;
                        spawnPos = null;
                    }
                }
                return;
            }

            if (spawnPos == null)
            {
                if (MaxAmountPerLevel < int.MaxValue)
                {
                    if (Character.CharacterList.Count(c => c.SpeciesName == SpeciesName) >= MaxAmountPerLevel)
                    {
                        // If the event is set to reset, let's just wait until the old corpse is removed (after being disabled).
                        if (resetTime == 0)
                        {
                            disallowed = true;
                        }
                        return;
                    }
                }

                FindSpawnPosition(affectSubImmediately: true);
                //the event gets marked as disallowed if a spawn point is not found
                if (isFinished || disallowed) { return; }
                spawnPending = true;
            }

            if (spawnPending)
            {
                System.Diagnostics.Debug.Assert(spawnPos.HasValue);
                if (spawnPos == null)
                {
                    disallowed = true;
                    return;
                }
                //wait until there are no submarines at the spawnpos
                if (SpawnPosType.HasFlag(Level.PositionType.MainPath) || SpawnPosType.HasFlag(Level.PositionType.SidePath) || SpawnPosType.HasFlag(Level.PositionType.Abyss))
                {
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineType.Player) { continue; }
                        float minDist = GetMinDistanceToSub(submarine);
                        if (Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value) < minDist * minDist)
                        {
                            // Too close to a player sub.
                            return;
                        }
                    }
                }
                float spawnDistance = SpawnDistance;
                if (spawnDistance <= 0)
                {
                    if (SpawnPosType.HasFlag(Level.PositionType.Cave))
                    {
                        spawnDistance = 8000;
                    }
                    else if (SpawnPosType.HasFlag(Level.PositionType.Ruin))
                    {
                        spawnDistance = 5000;
                    }
                    else if (SpawnPosType.HasFlag(Level.PositionType.Wreck) || SpawnPosType.HasFlag(Level.PositionType.BeaconStation))
                    {
                        spawnDistance = 3000;
                    }
                }
                if (spawnDistance > 0)
                {
                    bool someoneNearby = false;
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineType.Player) { continue; }
                        float distanceSquared = Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value);
                        if (distanceSquared < MathUtils.Pow2(spawnDistance))
                        {
                            someoneNearby = true;
                            if (chosenPosition.Submarine != null)
                            {
                                Vector2 from = Submarine.GetRelativeSimPositionFromWorldPosition(spawnPos.Value, chosenPosition.Submarine, chosenPosition.Submarine);
                                Vector2 to = Submarine.GetRelativeSimPositionFromWorldPosition(submarine.WorldPosition, chosenPosition.Submarine, submarine);
                                if (CheckLineOfSight(from, to, chosenPosition.Submarine))
                                {
                                    // Line of sight to a player sub -> don't spawn yet.
                                    return;
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c == Character.Controlled || c.IsRemotePlayer)
                        {
                            float distanceSquared = Vector2.DistanceSquared(c.WorldPosition, spawnPos.Value);
                            if (distanceSquared < MathUtils.Pow2(spawnDistance))
                            {
                                someoneNearby = true;
                                if (chosenPosition.Submarine != null)
                                {
                                    Vector2 from = Submarine.GetRelativeSimPositionFromWorldPosition(spawnPos.Value, chosenPosition.Submarine, chosenPosition.Submarine);
                                    Vector2 to = Submarine.GetRelativeSimPositionFromWorldPosition(c.WorldPosition, chosenPosition.Submarine, c.Submarine);
                                    if (CheckLineOfSight(from, to, chosenPosition.Submarine))
                                    {
                                        // Line of sight to a player character -> don't spawn. Disable the event to prevent monsters "magically" spawning here.
                                        disallowed = true;
                                        return;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                    if (!someoneNearby) { return; }
                    
                    static bool CheckLineOfSight(Vector2 from, Vector2 to, Submarine targetSub)
                    {
                        var bodies = Submarine.PickBodies(from, to, ignoredBodies: null, Physics.CollisionWall);
                        foreach (var b in bodies)
                        {
                            if (b.UserData is ISpatialEntity spatialEntity && spatialEntity.Submarine != targetSub)
                            {
                                // Different sub -> ignore
                                continue;
                            }
                            if (b.UserData is Structure s && !s.IsPlatform && s.CastShadow)
                            {
                                return false;
                            }
                            if (b.UserData is Item item && item.GetComponent<Door>() is Door door)
                            {
                                if (!door.IsBroken && !door.IsOpen)
                                {
                                    return false;
                                }
                            }
                        }
                        return true;
                    }
                }

                if (SpawnPosType.HasFlag(Level.PositionType.Abyss) || SpawnPosType.HasFlag(Level.PositionType.AbyssCave))
                {
                    bool anyInAbyss = false;
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineType.Player || submarine == GameMain.NetworkMember?.RespawnManager?.RespawnShuttle) { continue; }
                        if (submarine.WorldPosition.Y < 0)
                        {
                            anyInAbyss = true;
                            break;
                        }
                    }
                    if (!anyInAbyss) { return; }
                }

                spawnPending = false;

                float scatterAmount = scatter;
                if (SpawnPosType.HasFlag(Level.PositionType.SidePath))
                {
                    var sidePaths = Level.Loaded.Tunnels.Where(t => t.Type == Level.TunnelType.SidePath);
                    if (sidePaths.Any())
                    {
                        scatterAmount = Math.Min(scatter, sidePaths.Min(t => t.MinWidth) / 2);
                    }
                    else
                    {
                        scatterAmount = scatter;
                    }
                }
                else if (SpawnPosType.IsIndoorsArea())
                {
                    scatterAmount = 0;
                }

                int i = 0;
                foreach (Character monster in monsters)
                {
                    CoroutineManager.Invoke(() =>
                    {
                        //round ended before the coroutine finished
                        if (GameMain.GameSession == null || Level.Loaded == null) { return; }

                        if (monster.Removed) { return; }

                        System.Diagnostics.Debug.Assert(GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer, "Clients should not create monster events.");

                        Vector2 pos = spawnPos.Value;
                        if (scatterAmount > 0)
                        {
                            //try finding an offset position that's not inside a wall
                            int tries = 10;
                            do
                            {
                                tries--;
                                pos = spawnPos.Value + Rand.Vector(Rand.Range(0.0f, scatterAmount));

                                bool isValidPos = true;
                                if (Submarine.Loaded.Any(s => ToolBox.GetWorldBounds(s.Borders.Center, s.Borders.Size).ContainsWorld(pos)) ||
                                    Level.Loaded.Ruins.Any(r => ToolBox.GetWorldBounds(r.Area.Center, r.Area.Size).ContainsWorld(pos)) ||
                                    Level.Loaded.IsPositionInsideWall(pos))
                                {
                                    isValidPos = false;
                                }
                                else if (SpawnPosType.HasFlag(Level.PositionType.Cave) || SpawnPosType.HasFlag(Level.PositionType.AbyssCave))
                                {
                                    //trying to spawn in a cave, but the position is not inside a cave -> not valid
                                    if (Level.Loaded.Caves.None(c => c.Area.Contains(pos)))
                                    {
                                        isValidPos = false;
                                    }
                                }

                                if (isValidPos)
                                {
                                    //not inside anything, all good!
                                    break;
                                }
                                // This was the last try and couldn't find an offset position, let's use the exact spawn position.
                                if (tries == 0)
                                {
                                    pos = spawnPos.Value;
                                }
                            } while (tries > 0);
                        }

                        monster.Enabled = true;
                        monster.DisabledByEvent = false;
                        monster.AnimController.SetPosition(FarseerPhysics.ConvertUnits.ToSimUnits(pos));

                        var eventManager = GameMain.GameSession.EventManager;
                        if (eventManager != null && monster.Params.AI != null)
                        {
                            if (SpawnPosType.HasFlag(Level.PositionType.MainPath) || SpawnPosType.HasFlag(Level.PositionType.SidePath))
                            {
                                eventManager.CumulativeMonsterStrengthMain += monster.Params.AI.CombatStrength;
                                eventManager.AddTimeStamp(this);
                            }
                            else if (SpawnPosType.HasFlag(Level.PositionType.Ruin))
                            {
                                eventManager.CumulativeMonsterStrengthRuins += monster.Params.AI.CombatStrength;
                            }
                            else if (SpawnPosType.HasFlag(Level.PositionType.Wreck))
                            {
                                eventManager.CumulativeMonsterStrengthWrecks += monster.Params.AI.CombatStrength;
                            }
                            else if (SpawnPosType.HasFlag(Level.PositionType.Cave))
                            {
                                eventManager.CumulativeMonsterStrengthCaves += monster.Params.AI.CombatStrength;
                            }
                        }

                        if (monster == monsters.Last())
                        {
                            spawnReady = true;
                            //this will do nothing if the monsters have no swarm behavior defined, 
                            //otherwise it'll make the spawned characters act as a swarm
                            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
                            DebugConsole.NewMessage($"Spawned: {ToString()}. Strength: {StringFormatter.FormatZeroDecimal(monsters.Sum(m => m.Params.AI?.CombatStrength ?? 0))}.", Color.LightBlue, debugOnly: true);
                        }

                        if (GameMain.GameSession != null)
                        {
                            GameAnalyticsManager.AddDesignEvent(
                                $"MonsterSpawn:{GameMain.GameSession.GameMode?.Preset?.Identifier.Value ?? "none"}:{Level.Loaded?.LevelData?.Biome?.Identifier.Value ?? "none"}:{SpawnPosType}:{SpeciesName}",
                                value: GameMain.GameSession.RoundDuration);
                        }
                    }, delayBetweenSpawns * i);
                    i++;
                }
            }

            if (spawnReady)
            {
                if (monsters.None())
                {
                    Finish();
                }
                else if (monsters.All(m => m.IsDead))
                {
                    if (resetTime > 0)
                    {
                        resetTimer = resetTime;
                    }
                    else
                    {
                        Finish();
                    }
                }
            }
        }
    }
}
