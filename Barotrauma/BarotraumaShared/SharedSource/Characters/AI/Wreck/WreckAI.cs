using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.Networking;
using System.Linq;
using System;

namespace Barotrauma
{
    partial class WreckAI : IServerSerializable
    {
        public Submarine Wreck { get; private set; }

        public bool IsAlive { get; private set; }

        private readonly List<Item> allItems;
        private readonly List<Item> thalamusItems;
        private readonly List<Structure> thalamusStructures;
        private readonly List<Turret> turrets = new List<Turret>();
        private readonly List<WayPoint> wayPoints = new List<WayPoint>();
        private readonly List<Hull> hulls = new List<Hull>();
        private readonly List<Item> spawnOrgans = new List<Item>();
        private readonly Item brain;

        private bool initialCellsSpawned;

        public readonly WreckAIConfig Config;

        private bool IsClient => GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

        private bool IsThalamus(MapEntityPrefab entityPrefab) => IsThalamus(entityPrefab, Config.Entity);

        private static IEnumerable<T> GetThalamusEntities<T>(Submarine wreck, Identifier tag) where T : MapEntity => GetThalamusEntities(wreck, tag).Where(e => e is T).Select(e => e as T);

        private static IEnumerable<MapEntity> GetThalamusEntities(Submarine wreck, Identifier tag) => MapEntity.mapEntityList.Where(e => e.Submarine == wreck && e.Prefab != null && IsThalamus(e.Prefab, tag));

        private static bool IsThalamus(MapEntityPrefab entityPrefab, Identifier tag) => entityPrefab.HasSubCategory("thalamus") || entityPrefab.Tags.Contains(tag);

        public static WreckAI Create(Submarine wreck)
        {
            var wreckAI = new WreckAI(wreck);
            if (wreckAI.Config == null) { return null; }
            return wreckAI;
        }

        private WreckAI(Submarine wreck)
        {
            Wreck = wreck;
            Config = WreckAIConfig.GetRandom();
            if (Config == null)
            {
                DebugConsole.ThrowError("WreckAI: No wreck AI config found!");
                return;
            }
            var thalamusPrefabs = ItemPrefab.Prefabs.Where(p => IsThalamus(p));
            var brainPrefab = thalamusPrefabs.GetRandom(i => i.Tags.Contains(Config.Brain), Rand.RandSync.ServerAndClient);
            if (brainPrefab == null)
            {
                DebugConsole.ThrowError($"WreckAI: Could not find any brain prefab with the tag {Config.Brain}! Cannot continue. Failed to create wreck AI.");
                return;
            }
            allItems = Wreck.GetItems(false);
            thalamusItems = allItems.FindAll(i => IsThalamus(((MapEntity)i).Prefab));
            hulls.AddRange(Wreck.GetHulls(false));
            var potentialBrainHulls = new List<(Hull hull, float weight)>();
            brain = new Item(brainPrefab, Vector2.Zero, Wreck);
            thalamusItems.Add(brain);
            Point minSize = brain.Rect.Size.Multiply(brain.Scale);
            // Bigger hulls are allowed, but not preferred more than what's sufficent.
            Vector2 sufficentSize = new Vector2(minSize.X * 2, minSize.Y * 1.1f);
            // Shrink the horizontal axis so that the brain is not placed in the left or right side, where we often have curved walls.
            Rectangle shrinkedBounds = ToolBox.GetWorldBounds(Wreck.WorldPosition.ToPoint(), new Point(Wreck.Borders.Width - 500, Wreck.Borders.Height));
            foreach (Hull hull in hulls)
            {
                float distanceFromCenter = Vector2.Distance(Wreck.WorldPosition, hull.WorldPosition);
                float distanceFactor = MathHelper.Lerp(1.0f, 0.5f, MathUtils.InverseLerp(0, Math.Max(shrinkedBounds.Width, shrinkedBounds.Height) / 2, distanceFromCenter));
                float horizontalSizeFactor = MathHelper.Lerp(0.5f, 1.0f, MathUtils.InverseLerp(minSize.X, sufficentSize.X, hull.Rect.Width));
                float verticalSizeFactor = MathHelper.Lerp(0.5f, 1.0f, MathUtils.InverseLerp(minSize.Y, sufficentSize.Y, hull.Rect.Height));
                float weight = verticalSizeFactor * horizontalSizeFactor * distanceFactor;
                if (hull.GetLinkedEntities<Hull>().Any())
                {
                    // Ignore hulls that have any linked hulls to keep the calculations simple.
                    continue;
                }
                else if (hull.ConnectedGaps.Any(g => g.Open > 0 && (!g.IsRoomToRoom || g.Position.Y < hull.Position.Y)))
                {
                    // Ignore hulls that have open gaps to outside or below the center point, because we'll want the room to be full of water and not be accessible without breaking the wall.
                    continue;
                }
                else if (thalamusItems.Any(i => i.CurrentHull == hull))
                {
                    // Don't create the brain in a room that already has thalamus items inside it.
                    continue;
                }
                else if (hull.Rect.Width < minSize.X || hull.Rect.Height < minSize.Y)
                {
                    // Don't select too small rooms.
                    continue;
                }
                if (weight > 0)
                {
                    potentialBrainHulls.Add((hull, weight));
                }
            }
            Hull brainHull = ToolBox.SelectWeightedRandom(potentialBrainHulls.Select(pbh => pbh.hull).ToList(), potentialBrainHulls.Select(pbh => pbh.weight).ToList(), Rand.RandSync.ServerAndClient);
            var thalamusStructurePrefabs = StructurePrefab.Prefabs.Where(IsThalamus);
            if (brainHull == null)
            {
                DebugConsole.AddWarning("Wreck AI: Cannot find a proper room for the brain. Using a random room.");
                brainHull = hulls.GetRandom(Rand.RandSync.ServerAndClient);
            }
            if (brainHull == null)
            {
                DebugConsole.ThrowError("Wreck AI: Cannot find any room for the brain! Failed to create the Thalamus.");
                return;
            }
            brainHull.WaterVolume = brainHull.Volume;
            brain.SetTransform(brainHull.SimPosition, rotation: 0, findNewHull: false);
            brain.CurrentHull = brainHull;
            var backgroundPrefab = thalamusStructurePrefabs.GetRandom(i => i.Tags.Contains(Config.BrainRoomBackground), Rand.RandSync.ServerAndClient);
            if (backgroundPrefab != null)
            {
                new Structure(brainHull.Rect, backgroundPrefab, Wreck);
            }
            var horizontalWallPrefab = thalamusStructurePrefabs.GetRandom(p => p.Tags.Contains(Config.BrainRoomHorizontalWall), Rand.RandSync.ServerAndClient);
            if (horizontalWallPrefab != null)
            {
                int height = (int)horizontalWallPrefab.Size.Y;
                int halfHeight = height / 2;
                int quarterHeight = halfHeight / 2;
                new Structure(new Rectangle(brainHull.Rect.Left, brainHull.Rect.Top + quarterHeight, brainHull.Rect.Width, height), horizontalWallPrefab, Wreck);
                new Structure(new Rectangle(brainHull.Rect.Left, brainHull.Rect.Top - brainHull.Rect.Height + halfHeight + quarterHeight, brainHull.Rect.Width, height), horizontalWallPrefab, Wreck);
            }
            var verticalWallPrefab = thalamusStructurePrefabs.GetRandom(p => p.Tags.Contains(Config.BrainRoomVerticalWall), Rand.RandSync.ServerAndClient);
            if (verticalWallPrefab != null)
            {
                int width = (int)verticalWallPrefab.Size.X;
                int halfWidth = width / 2;
                int quarterWidth = halfWidth / 2;
                new Structure(new Rectangle(brainHull.Rect.Left - quarterWidth, brainHull.Rect.Top, width, brainHull.Rect.Height), verticalWallPrefab, Wreck);
                new Structure(new Rectangle(brainHull.Rect.Right - halfWidth - quarterWidth, brainHull.Rect.Top, width, brainHull.Rect.Height), verticalWallPrefab, Wreck);
            }
            foreach (Item item in allItems)
            {
                if (thalamusItems.Contains(item))
                {
                    // Ensure that thalamus items are visible
                    item.HiddenInGame = false;
                }
                else
                {
                    // Load regular turrets
                    var turret = item.GetComponent<Turret>();
                    if (turret != null)
                    {
                        foreach (var linkedItem in item.GetLinkedEntities<Item>())
                        {
                            var container = linkedItem.GetComponent<ItemContainer>();
                            if (container == null) { continue; }
                            for (int i = 0; i < container.Inventory.Capacity; i++)
                            {
                                if (container.Inventory.GetItemAt(i) != null) { continue; }
                                if (MapEntityPrefab.List.GetRandom(e => e is ItemPrefab ip && container.CanBeContained(ip, i) && 
                                        Config.ForbiddenAmmunition.None(id => id == ip.Identifier), Rand.RandSync.ServerAndClient) is ItemPrefab ammoPrefab)
                                {
                                    Item ammo = new Item(ammoPrefab, container.Item.WorldPosition, Wreck);
                                    if (!container.Inventory.TryPutItem(ammo, i, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: false))
                                    {
                                        item.Remove();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            foreach (var item in allItems)
            {
                var turret = item.GetComponent<Turret>();
                if (turret != null)
                {
                    turrets.Add(turret);
                }
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
            wayPoints.AddRange(Wreck.GetWaypoints(false));
            IsAlive = true;
            thalamusStructures = GetThalamusEntities<Structure>(Wreck, Config.Entity).ToList();
        }

        private readonly List<Item> destroyedOrgans = new List<Item>();
        public void Update(float deltaTime)
        {
            if (!IsAlive) { return; }
            if (Wreck == null || Wreck.Removed)
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
            bool someoneNearby = false;
            float minDist = Sonar.DefaultSonarRange * 2.0f;
            foreach (Submarine submarine in Submarine.Loaded)
            {
                if (submarine.Info.Type != SubmarineType.Player) { continue; }
                if (Vector2.DistanceSquared(submarine.WorldPosition, Wreck.WorldPosition) < minDist * minDist)
                {
                    someoneNearby = true;
                    break;
                }
            }
            foreach (Character c in Character.CharacterList)
            {
                if (c != Character.Controlled && !c.IsRemotePlayer) { continue; }
                if (Vector2.DistanceSquared(c.WorldPosition, Wreck.WorldPosition) < minDist * minDist)
                {
                    someoneNearby = true;
                    break;
                }
            }
            if (!someoneNearby) { return; }
            OperateTurrets(deltaTime);
            if (!IsClient)
            {
                if (!initialCellsSpawned) { SpawnInitialCells(); }
                UpdateReinforcements(deltaTime);
            }
        }

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
            thalamusItems.ForEach(i => i.Condition = 0);
            foreach (var turret in turrets)
            {
                // Snap all tendons
                foreach (Item item in turret.ActiveProjectiles)
                {
                    if (item.GetComponent<Projectile>()?.IsStuckToTarget ?? false)
                    {
                        item.Condition = 0;
                    }
                }
            }
            FadeOutColors();
            protectiveCells.ForEach(c => c.OnDeath -= OnCellDeath);
            if (!IsClient)
            {
                if (Config != null)
                {
                    if (Config.KillAgentsWhenEntityDies)
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
                                    if (Vector2.DistanceSquared(character.WorldPosition, Wreck.WorldPosition) < maxDistance * maxDistance)
                                    {
                                        character.Kill(CauseOfDeathType.Unknown, null);
                                    }
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
            RemoveThalamusItems(Wreck);
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
            float t = MathUtils.InverseLerp(0, 100, Level.Loaded.Difficulty * Config.AgentSpawnCountDifficultyMultiplier);
            return (int)Math.Round(MathHelper.Lerp(minValue, maxValue, t));
        }

        private float GetSpawnTime()
        {
            float randomFactor = Config.AgentSpawnDelayRandomFactor;
            float delay = Config.AgentSpawnDelay;
            float min = delay;
            float max = delay * 6;
            float t = Level.Loaded.Difficulty * Config.AgentSpawnDelayDifficultyMultiplier * Rand.Range(1 - randomFactor, 1 + randomFactor);
            return MathHelper.Lerp(max, min, MathUtils.InverseLerp(0, 100, t));
        }

        void UpdateReinforcements(float deltaTime)
        {
            if (spawnOrgans.Count == 0) { return; }
            cellSpawnTimer -= deltaTime;
            if (cellSpawnTimer < 0)
            {
                TrySpawnCell(out _, spawnOrgans.GetRandomUnsynced());
                cellSpawnTimer = GetSpawnTime();
            }
        }

        bool TrySpawnCell(out Character cell, ISpatialEntity targetEntity = null)
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
        
        void OperateTurrets(float deltaTime)
        {
            foreach (var turret in turrets)
            {
                // Never target other creatures than humans with the turrets.
                turret.ThalamusOperate(this, deltaTime, 
                    !turret.Item.HasTag("ignorecharacters"), 
                    targetOtherCreatures: false, 
                    !turret.Item.HasTag("ignoresubmarines"), 
                    turret.Item.HasTag("ignoreaimdelay"));
            }
        }

        void OnCellDeath(Character character, CauseOfDeath causeOfDeath)
        {
            protectiveCells.Remove(character);
        }

#if SERVER
        public void ServerEventWrite(IWriteMessage msg, Client client, NetEntityEvent.IData extraData = null)
        {
            msg.Write(IsAlive);
        }
#endif
    }
}