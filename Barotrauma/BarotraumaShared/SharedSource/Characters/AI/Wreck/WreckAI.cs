using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.Networking;
using System.Linq;
using System;
using System.Diagnostics;

namespace Barotrauma
{
    internal class SubmarineTurretAI
    {
        public Submarine Submarine { get; protected set; }
        protected readonly List<Turret> turrets = new List<Turret>();
        public Identifier FriendlyTag;

        public SubmarineTurretAI(Submarine submarine, Identifier friendlyTag = default)
        {
            FriendlyTag = friendlyTag;
            Submarine = submarine;
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine != Submarine) { continue; }
                var turret = item.GetComponent<Turret>();
                if (turret != null)
                {
                    turrets.Add(turret);
                    // Set false, because we manage the turrets in the Update method.
                    turret.AutoOperate = false;
                    // Set to full condition, because items don't work when they are broken.
                    turret.Item.Condition = turret.Item.MaxCondition;
                    foreach (MapEntity linkedEntity in turret.Item.linkedTo)
                    {
                        if (linkedEntity is Item linkedItem)
                        {
                            linkedItem.Condition = linkedItem.MaxCondition;
                        }
                    }
                }
            }
            LoadAllTurrets();
        }

        public virtual void Update(float deltaTime)
        {
            if (Submarine == null || Submarine.Removed) { return; }
            OperateTurrets(deltaTime, FriendlyTag);
        }

        protected virtual void LoadAllTurrets()
        {
            foreach (var turret in turrets)
            {
                LoadTurret(turret);
            }
        }

        protected void LoadTurret(Turret turret, Func<ItemPrefab, bool> ammoFilter = null)
        {
            foreach (var linkedItem in turret.Item.GetLinkedEntities<Item>())
            {
                var container = linkedItem.GetComponent<ItemContainer>();
                if (container == null) { continue; }
                for (int i = 0; i < container.Inventory.Capacity; i++)
                {
                    if (container.Inventory.GetItemAt(i) != null) { continue; }
                    if (MapEntityPrefab.List.GetRandom(e => e is ItemPrefab ip && container.CanBeContained(ip, i) && (ammoFilter == null || ammoFilter(ip)), Rand.RandSync.ServerAndClient) is ItemPrefab ammoPrefab)
                    {
                        Item ammo = new Item(ammoPrefab, container.Item.WorldPosition, Submarine);
                        if (!container.Inventory.TryPutItem(ammo, i, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: false))
                        {
                            turret.Item.Remove();
                        }
                    }
                }
            }
        }

        protected void OperateTurrets(float deltaTime, Identifier friendlyTag)
        {
            foreach (var turret in turrets)
            {
                turret.UpdateAutoOperate(deltaTime, ignorePower: true, friendlyTag);
            }
        }
    }

    partial class WreckAI : SubmarineTurretAI, IServerSerializable
    {
        public bool IsAlive { get; private set; }

        private readonly List<Item> thalamusItems;
        private readonly List<Structure> thalamusStructures;
        private readonly List<WayPoint> wayPoints = new List<WayPoint>();
        private readonly List<Hull> hulls = new List<Hull>();
        private readonly List<Item> spawnOrgans = new List<Item>();
        private readonly List<Door> jammedDoors = new List<Door>();
        private readonly Item brain;

        private bool initialCellsSpawned;

        public WreckAIConfig Config { get; private set; }

        private bool IsClient => GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

        private bool IsThalamus(MapEntityPrefab entityPrefab) => IsThalamus(entityPrefab, Config.Entity);

        private static IEnumerable<T> GetThalamusEntities<T>(Submarine wreck, Identifier tag) where T : MapEntity => GetThalamusEntities(wreck, tag).OfType<T>();

        private static IEnumerable<MapEntity> GetThalamusEntities(Submarine wreck, Identifier tag) => MapEntity.MapEntityList.Where(e => e.Submarine == wreck && e.Prefab != null && IsThalamus(e.Prefab, tag));

        public static bool IsThalamus(MapEntityPrefab entityPrefab, Identifier tag) => entityPrefab.HasSubCategory("thalamus") || entityPrefab.Tags.Contains(tag);

        public static WreckAI Create(Submarine wreck)
        {
            var wreckAI = new WreckAI(wreck);
            if (wreckAI.Config == null) { return null; }
            return wreckAI;
        }

        private WreckAI(Submarine wreck) : base(wreck)
        {
            GetConfig();
            if (Config == null) { return; }
            var thalamusPrefabs = ItemPrefab.Prefabs.Where(IsThalamus);
            var brainPrefab = thalamusPrefabs.GetRandom(i => i.Tags.Contains(Config.Brain), Rand.RandSync.ServerAndClient);
            if (brainPrefab == null)
            {
                DebugConsole.ThrowError($"WreckAI {wreck.Info.Name}: Could not find any brain prefab with the tag {Config.Brain}! Cannot continue. Failed to create wreck AI.", contentPackage: Config.ContentPackage);
                return;
            }
            thalamusItems = GetThalamusEntities<Item>(wreck, Config.Entity).ToList();
            hulls.AddRange(wreck.GetHulls(alsoFromConnectedSubs: false));
            brain = new Item(brainPrefab, Vector2.Zero, wreck);
            thalamusItems.Add(brain);
            Point minSize = brain.Rect.Size.Multiply(brain.Scale);
            var potentialBrainHulls = GetPotentialBrainRooms(wreck, Config, minSize, thalamusItems);
            Hull brainHull = ToolBox.SelectWeightedRandom(potentialBrainHulls.Select(pbh => pbh.hull).ToList(), potentialBrainHulls.Select(pbh => pbh.weight).ToList(), Rand.RandSync.ServerAndClient);
            var thalamusStructurePrefabs = StructurePrefab.Prefabs.Where(IsThalamus);
            if (brainHull == null)
            {
                DebugConsole.ThrowError($"Wreck AI {wreck.Info.Name}: Cannot find a suitable room for the Thalamus brain. Using a random room. " +
                                        $"The wreck should be fixed so that there's at least one room where the following conditions are met: No linked hulls, no open gaps in the floor or to outside the sub, and no other Thalamus items present in the hull.",
                    contentPackage: Config.ContentPackage);
                
                brainHull = hulls.GetRandom(Rand.RandSync.ServerAndClient);
            }
            if (brainHull == null)
            {
                DebugConsole.ThrowError($"Wreck AI {wreck.Info.Name}: Cannot find any room for the brain! Failed to create the Thalamus.", contentPackage: Config.ContentPackage);
                return;
            }
            Debug.WriteLine($"Wreck AI {wreck.Info.Name}: Selected brain room: {brainHull.DisplayName}");
            brainHull.WaterVolume = brainHull.Volume;
            brain.SetTransform(brainHull.SimPosition, rotation: 0, findNewHull: false);
            brain.CurrentHull = brainHull;
            
            // Jam the doors, mainly to prevent any mechanisms from opening them. Also makes it a little bit more difficult for the player to breach into the brain room, because they now have to break the door.
            foreach (Door door in brainHull.ConnectedGaps.Select(g => g.ConnectedDoor))
            {
                if (door == null) { continue; }
                door.IsJammed = true;
                jammedDoors.Add(door);
            }
            
            var backgroundPrefab = thalamusStructurePrefabs.GetRandom(i => i.Tags.Contains(Config.BrainRoomBackground), Rand.RandSync.ServerAndClient);
            if (backgroundPrefab != null)
            {
                var background = new Structure(brainHull.Rect, backgroundPrefab, wreck);
                background.SpriteDepth -= 0.01f;
            }
            foreach (Item item in thalamusItems)
            {
                // Ensure that thalamus items are visible
                item.IsLayerHidden = false;
                if (item.HasTag(Config.Spawner))
                {
                    if (!spawnOrgans.Contains(item))
                    {
                        spawnOrgans.Add(item);
                        if (item.CurrentHull != null)
                        {
                            // Try to flood the hull so that the spawner won't die.
                            item.CurrentHull.WaterVolume = item.CurrentHull.Volume;
                        }
                    }
                }
            }
            wayPoints.AddRange(wreck.GetWaypoints(false));
            IsAlive = true;
            thalamusStructures = GetThalamusEntities<Structure>(wreck, Config.Entity).ToList();
        }

        private void GetConfig()
        {
            Config ??= WreckAIConfig.GetRandom();
            if (Config == null)
            {
                DebugConsole.ThrowError("WreckAI: No wreck AI config found!");
            }
        }

        protected override void LoadAllTurrets()
        {
            GetConfig();
            foreach (var turret in turrets)
            {
                LoadTurret(turret, ip => Config.ForbiddenAmmunition.None(id => id == ip.Identifier));
            }
        }

        private readonly List<Item> destroyedOrgans = new List<Item>();
        public override void Update(float deltaTime)
        {
            if (!IsAlive) { return; }
            if (Submarine == null || Submarine.Removed)
            {
                Remove();
                return;
            }
            if (brain == null || brain.Removed || brain.Condition <= 0)
            {
                Kill();
                return;
            }
            destroyedOrgans.Clear();
            foreach (var organ in spawnOrgans)
            {
                if (organ.Condition <= 0)
                {
                    destroyedOrgans.Add(organ);
                }
            }
            destroyedOrgans.ForEach(o => spawnOrgans.Remove(o));
            if (!IsClient)
            {
                if (!initialCellsSpawned) { SpawnInitialCells(); }
            }
            bool isSomeoneNearby = false;
            float minDist = Sonar.DefaultSonarRange * 2.0f;
#if SERVER
            foreach (var client in GameMain.Server.ConnectedClients)
            {
                var spectatePos = client.SpectatePos;
                if (spectatePos.HasValue)
                {
                    if (IsCloseEnough(spectatePos.Value, minDist))
                    {
                        isSomeoneNearby = true;
                        break;
                    }
                }
            }
#else
            if (IsCloseEnough(GameMain.GameScreen.Cam.Position, minDist))
            {
                isSomeoneNearby = true;
            }
#endif
            if (!isSomeoneNearby)
            {
                foreach (Submarine submarine in Submarine.Loaded)
                {
                    if (submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (IsCloseEnough(submarine.WorldPosition, minDist))
                    {
                        isSomeoneNearby = true;
                        break;
                    }
                }
            }
            if (!isSomeoneNearby)
            {
                foreach (Character c in Character.CharacterList)
                {
                    if (!c.IsPlayer && !c.IsOnPlayerTeam) { continue; }
                    if (IsCloseEnough(c.WorldPosition, minDist))
                    {
                        isSomeoneNearby = true;
                        break;
                    }
                }
            }
            if (!isSomeoneNearby) { return; }
            OperateTurrets(deltaTime, Config.Entity);
            if (!IsClient)
            {
                UpdateReinforcements(deltaTime);
            }
        }
        private bool IsCloseEnough(Vector2 targetPos, float minDist) => Vector2.DistanceSquared(targetPos, Submarine.WorldPosition) < minDist * minDist;

        private void SpawnInitialCells()
        {
            int brainRoomCells = Rand.Range(MinCellsPerBrainRoom, MaxCellsPerRoom + 1);
            if (brain.CurrentHull?.WaterPercentage >= MinWaterLevel)
            {
                for (int i = 0; i < brainRoomCells; i++)
                {
                    if (!TrySpawnCell(out _, brain.CurrentHull)) { break; }
                }
            }
            int cellsInside = Rand.Range(MinCellsInside, MaxCellsInside + 1);
            for (int i = 0; i < cellsInside; i++)
            {
                if (!TrySpawnCell(out _)) { break; }
            }
            int cellsOutside = Rand.Range(MinCellsOutside, MaxCellsOutside + 1);
            // If we failed to spawn some of the cells in the brainroom/inside, spawn some extra cells outside.
            cellsOutside = Math.Clamp(cellsOutside + brainRoomCells + cellsInside - protectiveCells.Count, cellsOutside, MaxCellsOutside);
            for (int i = 0; i < cellsOutside; i++)
            {
                ISpatialEntity targetEntity = wayPoints.GetRandomUnsynced(wp => wp.CurrentHull == null);
                if (targetEntity == null) { break; }
                if (!TrySpawnCell(out _, targetEntity)) { break; }
            }
            initialCellsSpawned = true;
        }

        public void Kill()
        {
            jammedDoors.ForEach(d => d.IsJammed = false);
            thalamusItems.ForEach(i => i.Condition = 0);
            foreach (var turret in turrets)
            {
                // Snap all tendons
                foreach (Item item in turret.ActiveProjectiles)
                {
                    if (item.GetComponent<Projectile>() is { IsStuckToTarget: true })
                    {
                        item.Condition = 0;
                    }
                }
            }
            FadeOutColors();
            protectiveCells.ForEach(c => c.OnDeath -= OnCellDeath);
            if (!IsClient)
            {
                if (Config is { KillAgentsWhenEntityDies: true })
                {
                    protectiveCells.ForEach(c => c.Kill(CauseOfDeathType.Unknown, null));
                    if (!string.IsNullOrWhiteSpace(Config.OffensiveAgent))
                    {
                        foreach (var character in Character.CharacterList)
                        {
                            // Kills ALL offensive agents that are near the thalamus. Not the ideal solution, 
                            // but as long as spawning is handled via status effects, I don't know if there is any better way.
                            // In practice there shouldn't be terminal cells from different thalamus organisms at the same time.
                            // And if there was, the distance check should prevent killing the agents of a different organism.
                            if (character.SpeciesName == Config.OffensiveAgent)
                            {
                                // Sonar distance is used also for wreck positioning. No wreck should be closer to each other than this.
                                float maxDistance = Sonar.DefaultSonarRange;
                                if (Vector2.DistanceSquared(character.WorldPosition, Submarine.WorldPosition) < maxDistance * maxDistance)
                                {
                                    character.Kill(CauseOfDeathType.Unknown, null);
                                }
                            }
                        }
                    }
                }
            }
            protectiveCells.Clear();
            IsAlive = false;
        }

        partial void FadeOutColors();

        public void Remove()
        {
            Kill();
            RemoveThalamusItems(Submarine);
            thalamusItems?.Clear();
            thalamusStructures?.Clear();
        }

        public static void RemoveThalamusItems(Submarine wreck)
        {
            List<MapEntity> thalamusItems = new List<MapEntity>();
            foreach (var wreckAiConfig in WreckAIConfig.Prefabs)
            {
                thalamusItems.AddRange(GetThalamusEntities(wreck, wreckAiConfig.Entity));
            }
            thalamusItems = thalamusItems.Distinct().ToList();
            foreach (MapEntity thalamusItem in thalamusItems)
            {
                thalamusItem.Remove();
                wreck.PhysicsBody.FarseerBody.FixtureList.Where(f => f.UserData == thalamusItem).ForEachMod(f => wreck.PhysicsBody.FarseerBody.Remove(f));
            }
        }

        // The client doesn't use these, so we don't have to sync them.
        private readonly List<Character> protectiveCells = new List<Character>();
        // Intentionally contains duplicates.
        private readonly List<Hull> populatedHulls = new List<Hull>();
        private float cellSpawnTimer;

        private int MinCellsPerBrainRoom => CalculateCellCount(0, Config.MinAgentsPerBrainRoom);
        private int MaxCellsPerRoom => CalculateCellCount(1, Config.MaxAgentsPerRoom);
        private int MinCellsOutside => CalculateCellCount(0, Config.MinAgentsOutside);
        private int MaxCellsOutside => CalculateCellCount(0, Config.MaxAgentsOutside);
        private int MinCellsInside => CalculateCellCount(3, Config.MinAgentsInside);
        private int MaxCellsInside => CalculateCellCount(5, Config.MaxAgentsInside);
        private int MaxCellCount => CalculateCellCount(5, Config.MaxAgentCount);
        private float MinWaterLevel => Config.MinWaterLevel;

        private int CalculateCellCount(int minValue, int maxValue)
        {
            if (maxValue == 0) { return 0; }
            float difficulty = Level.Loaded?.Difficulty ?? 0.0f;
            float t = MathUtils.InverseLerp(0, 100, difficulty * Config.AgentSpawnCountDifficultyMultiplier);
            return (int)Math.Round(MathHelper.Lerp(minValue, maxValue, t));
        }

        private float GetSpawnTime()
        {
            float randomFactor = Config.AgentSpawnDelayRandomFactor;
            float delay = Config.AgentSpawnDelay;
            float min = delay;
            float max = delay * 6;
            float difficulty = Level.Loaded?.Difficulty ?? 0.0f;
            float t = difficulty * Config.AgentSpawnDelayDifficultyMultiplier * Rand.Range(1 - randomFactor, 1 + randomFactor);
            return MathHelper.Lerp(max, min, MathUtils.InverseLerp(0, 100, t));
        }

        private void UpdateReinforcements(float deltaTime)
        {
            if (spawnOrgans.Count == 0) { return; }
            cellSpawnTimer -= deltaTime;
            if (cellSpawnTimer < 0)
            {
                TrySpawnCell(out _, spawnOrgans.GetRandomUnsynced());
                cellSpawnTimer = GetSpawnTime();
            }
        }

        private bool TrySpawnCell(out Character cell, ISpatialEntity targetEntity = null)
        {
            cell = null;
            if (protectiveCells.Count >= MaxCellCount) { return false; }
            if (targetEntity == null)
            {
                targetEntity = 
                    wayPoints.GetRandomUnsynced(wp => wp.CurrentHull != null && populatedHulls.Count(h => h == wp.CurrentHull) < MaxCellsPerRoom && wp.CurrentHull.WaterPercentage >= MinWaterLevel) ?? 
                    hulls.GetRandomUnsynced(h => populatedHulls.Count(h2 => h2 == h) < MaxCellsPerRoom && h.WaterPercentage >= MinWaterLevel) as ISpatialEntity;
            }
            if (targetEntity == null) { return false; }
            if (targetEntity is Hull h)
            {
                populatedHulls.Add(h);
            }
            else if (targetEntity is WayPoint wp && wp.CurrentHull != null)
            {
                populatedHulls.Add(wp.CurrentHull);
            }
            // Don't add items in the list, because we want to be able to ignore the restrictions for spawner organs.
            cell = Character.Create(Config.DefensiveAgent, targetEntity.WorldPosition, ToolBox.RandomSeed(8), hasAi: true, createNetworkEvent: true);
            protectiveCells.Add(cell);
            cell.OnDeath += OnCellDeath;
            cellSpawnTimer = GetSpawnTime();
            return true;
        }

        void OnCellDeath(Character character, CauseOfDeath causeOfDeath)
        {
            protectiveCells.Remove(character);
        }

#if SERVER
        public void ServerEventWrite(IWriteMessage msg, Client client, NetEntityEvent.IData extraData = null)
        {
            msg.WriteBoolean(IsAlive);
        }
#endif
        
        public static List<(Hull hull, float weight)> GetPotentialBrainRooms(Submarine wreck, WreckAIConfig wreckAI, Point minSize, IEnumerable<Item> thalamusItems = null)
        {
            var potentialBrainHulls = new List<(Hull hull, float weight)>();
            // Bigger hulls are allowed, but not preferred more than what's sufficient.
            Vector2 sufficientSize = new Vector2(minSize.X * 2, minSize.Y * 1.1f);
            Rectangle worldBounds = ToolBox.GetWorldBounds(wreck.WorldPosition.ToPoint(), new Point(wreck.Borders.Width, wreck.Borders.Height));
            thalamusItems ??= GetThalamusEntities<Item>(wreck, wreckAI.Entity);
            foreach (Hull hull in wreck.GetHulls(alsoFromConnectedSubs: false))
            {
                if (hull.GetLinkedEntities<Hull>().Any())
                {
                    // Ignore hulls that have any linked hulls to keep the calculations simple.
                    continue;
                }
                else if (hull.ConnectedGaps.Any(g => (g.Open > 0 || g.ConnectedDoor?.Item.Condition <= 0) && (!g.IsRoomToRoom || g.Position.Y < hull.Position.Y)))
                {
                    // Ignore hulls that have open gaps to outside or below the center point, because we'll want the room to be full of water and not be accessible without breaking the wall.
                    // Gaps in the broken doors are not yet open at this stage. Also Door.IsBroken is not yet up-to-date, so we'll have to check the item condition.
                    continue;
                }
                else if (thalamusItems.Any(i => i.CurrentHull == hull && !i.HasTag(Tags.WireItem)))
                {
                    // Don't create the brain in a room that already has thalamus items inside it.
                    continue;
                }
                else if (hull.Rect.Width < minSize.X || hull.Rect.Height < minSize.Y)
                {
                    // Don't select too small rooms.
                    continue;
                }
                float weight = 0;
                if (hull.IsAirlock)
                {
                    // Prefer something else than airlocks
                    weight = 0;
                }
                else
                {
                    float distanceFromCenter = Vector2.Distance(wreck.WorldPosition, hull.WorldPosition);
                    float distanceFactor = MathHelper.Lerp(1.0f, 0.5f, MathUtils.InverseLerp(0, Math.Max(worldBounds.Width, worldBounds.Height) / 2f, distanceFromCenter));
                    float horizontalSizeFactor = MathHelper.Lerp(0.5f, 1.0f, MathUtils.InverseLerp(minSize.X, sufficientSize.X, hull.Rect.Width));
                    float verticalSizeFactor = MathHelper.Lerp(0.5f, 1.0f, MathUtils.InverseLerp(minSize.Y, sufficientSize.Y, hull.Rect.Height));
                    weight = verticalSizeFactor * horizontalSizeFactor * distanceFactor;
                }
                if (weight > 0 || potentialBrainHulls.None())
                {
                    potentialBrainHulls.Add((hull, weight));
                }
            }
            Debug.WriteLine($"Wreck AI {wreck.Info.Name}: Potential brain rooms: {potentialBrainHulls.Count}");
            foreach ((Hull hull, float weight) in potentialBrainHulls)
            {
                Debug.WriteLine($"Wreck AI: Potential brain room: {hull.DisplayName}, {weight.FormatSingleDecimal()}");
            }
            return potentialBrainHulls;
        }
    }
}