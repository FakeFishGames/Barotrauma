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

        private static IEnumerable<T> GetThalamusEntities<T>(Submarine wreck, string tag) where T : MapEntity => GetThalamusEntities(wreck, tag).Where(e => e is T).Select(e => e as T);

        private static IEnumerable<MapEntity> GetThalamusEntities(Submarine wreck, string tag) => MapEntity.mapEntityList.Where(e => e.Submarine == wreck && e.prefab != null && IsThalamus(e.prefab, tag));

        private static bool IsThalamus(MapEntityPrefab entityPrefab, string tag) => entityPrefab.HasSubCategory("thalamus") || entityPrefab.Tags.Contains(tag);

        public WreckAI(Submarine wreck)
        {
            Wreck = wreck;
            Config = WreckAIConfig.GetRandom();
            if (Config == null)
            {
                DebugConsole.ThrowError("WreckAI: No wreck AI config found!");
                return;
            }
            var thalamusPrefabs = ItemPrefab.Prefabs.Where(p => IsThalamus(p));
            var brainPrefab = thalamusPrefabs.GetRandom(i => i.Tags.Contains(Config.Brain), Rand.RandSync.Server);
            if (brainPrefab == null)
            {
                DebugConsole.ThrowError($"WreckAI: Could not find any brain prefab with the tag {Config.Brain}! Cannot continue. Failed to create wreck AI.");
                return;
            }
            allItems = Wreck.GetItems(false);
            thalamusItems = allItems.FindAll(i => IsThalamus(i.prefab));
            var hulls = Wreck.GetHulls(false);
            brain = new Item(brainPrefab, Vector2.Zero, Wreck);
            thalamusItems.Add(brain);
            Vector2 negativeMargin = new Vector2(40, 20);
            Vector2 minSize = brain.Rect.Size.ToVector2() - negativeMargin;
            Vector2 maxSize = new Vector2(brain.Rect.Width * 3, brain.Rect.Height * 3);
            // First try to get a room that is not too big and not in the edges of the sub.
            // Also try not to create the brain in a room that already have carrier items inside.
            // Ignore hulls that have any linked hulls to keep the calculations simple.
            // Shrink the horizontal axis so that the brain is not placed in the left or right side, where we often have curved walls.
            // Also ignore hulls that have open gaps, because we'll want the room to be full of water. The room will be filled with water when the brain is inserted in the room.
            Rectangle shrinkedBounds = ToolBox.GetWorldBounds(Wreck.WorldPosition.ToPoint(), new Point(Wreck.Borders.Width - 500, Wreck.Borders.Height));
            bool BaseCondition(Hull h) => h.RectWidth > minSize.X && h.RectHeight > minSize.Y && h.GetLinkedEntities<Hull>().None() && h.ConnectedGaps.None(g => g.Open > 0);
            bool IsNotTooBig(Hull h) => h.RectWidth < maxSize.X && h.RectHeight < maxSize.Y;
            bool IsNotInFringes(Hull h) => shrinkedBounds.ContainsWorld(h.WorldRect);
            bool DoesNotContainOtherItems(Hull h) => thalamusItems.None(i => i.CurrentHull == h);
            Hull brainHull = hulls.GetRandom(h => BaseCondition(h) && IsNotTooBig(h) && IsNotInFringes(h) && DoesNotContainOtherItems(h), Rand.RandSync.Server);
            if (brainHull == null)
            {
                brainHull = hulls.GetRandom(h => BaseCondition(h) && IsNotInFringes(h) && DoesNotContainOtherItems(h), Rand.RandSync.Server);
            }
            if (brainHull == null)
            {
                brainHull = hulls.GetRandom(h => BaseCondition(h) && (IsNotInFringes(h) || DoesNotContainOtherItems(h)), Rand.RandSync.Server);
            }
            if (brainHull == null)
            {
                brainHull = hulls.GetRandom(BaseCondition, Rand.RandSync.Server);
            }
            var thalamusStructurePrefabs = StructurePrefab.Prefabs.Where(p => IsThalamus(p));
            if (brainHull == null) { return; }
            brainHull.WaterVolume = brainHull.Volume;
            brain.SetTransform(brainHull.SimPosition, rotation: 0, findNewHull: false);
            brain.CurrentHull = brainHull;
            var backgroundPrefab = thalamusStructurePrefabs.GetRandom(i => i.Tags.Contains(Config.BrainRoomBackground), Rand.RandSync.Server);
            if (backgroundPrefab != null)
            {
                new Structure(brainHull.Rect, backgroundPrefab, Wreck);
            }
            var horizontalWallPrefab = thalamusStructurePrefabs.GetRandom(p => p.Tags.Contains(Config.BrainRoomHorizontalWall), Rand.RandSync.Server);
            if (horizontalWallPrefab != null)
            {
                int height = (int)horizontalWallPrefab.Size.Y;
                int halfHeight = height / 2;
                int quarterHeight = halfHeight / 2;
                new Structure(new Rectangle(brainHull.Rect.Left, brainHull.Rect.Top + quarterHeight, brainHull.Rect.Width, height), horizontalWallPrefab, Wreck);
                new Structure(new Rectangle(brainHull.Rect.Left, brainHull.Rect.Top - brainHull.Rect.Height + halfHeight + quarterHeight, brainHull.Rect.Width, height), horizontalWallPrefab, Wreck);
            }
            var verticalWallPrefab = thalamusStructurePrefabs.GetRandom(p => p.Tags.Contains(Config.BrainRoomVerticalWall), Rand.RandSync.Server);
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
                                if (MapEntityPrefab.List.GetRandom(e => e is ItemPrefab i && container.CanBeContained(i) && 
                                        Config.ForbiddenAmmunition.None(id => id.Equals(i.Identifier, StringComparison.OrdinalIgnoreCase)), Rand.RandSync.Server) is ItemPrefab ammoPrefab)
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
                    }
                }
            }
            wayPoints.AddRange(Wreck.GetWaypoints(false));
            hulls.AddRange(Wreck.GetHulls(false));
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
            int brainRoomCells = Rand.Range(MinCellsPerBrainRoom, MaxCellsPerRoom);
            if (brain.CurrentHull?.WaterPercentage >= MinWaterLevel)
            {
                for (int i = 0; i < brainRoomCells; i++)
                {
                    if (!TrySpawnCell(out _, brain.CurrentHull)) { break; }
                }
            }
            int cellsInside = Rand.Range(MinCellsInside, MaxCellsInside);
            for (int i = 0; i < cellsInside; i++)
            {
                if (!TrySpawnCell(out _)) { break; }
            }
            int cellsOutside = Rand.Range(MinCellsOutside, MaxCellsOutside);
            // If we failed to spawn some of the cells in the brainroom/inside, spawn some extra cells outside.
            cellsOutside = Math.Clamp(cellsOutside + brainRoomCells + cellsInside - protectiveCells.Count, cellsOutside, MaxCellsOutside);
            for (int i = 0; i < cellsOutside; i++)
            {
                ISpatialEntity targetEntity = wayPoints.GetRandom(wp => wp.CurrentHull == null);
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
                                if (character.SpeciesName.Equals(Config.OffensiveAgent, StringComparison.OrdinalIgnoreCase))
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
            foreach (var wreckAiConfig in WreckAIConfig.List)
            {
                GetThalamusEntities(wreck, wreckAiConfig.Entity).ForEachMod(e => e.Remove());
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
        private int MinCellsInside => CalculateCellCount(2, Config.MinAgentsInside);
        private int MaxCellsInside => CalculateCellCount(3, Config.MaxAgentsInside);
        private int MaxCellCount => CalculateCellCount(5, Config.MaxAgentCount);
        private float MinWaterLevel => Config.MinWaterLevel;

        private int CalculateCellCount(int minValue, int maxValue)
        {
            if (maxValue == 0) { return 0; }
            return (int)Math.Round(MathHelper.Lerp(minValue, maxValue, Level.Loaded.Difficulty * 0.01f * Config.AgentSpawnCountDifficultyMultiplier));
        }

        private float GetSpawnTime() => 
            Math.Max(Config.AgentSpawnDelay * Rand.Range(Config.AgentSpawnDelayRandomFactor, 1 + Config.AgentSpawnDelayRandomFactor) 
            / (Math.Max(Level.Loaded.Difficulty, 1) * 0.01f * Config.AgentSpawnDelayDifficultyMultiplier), Config.AgentSpawnDelay);

        void UpdateReinforcements(float deltaTime)
        {
            if (spawnOrgans.Count == 0) { return; }
            cellSpawnTimer -= deltaTime;
            if (cellSpawnTimer < 0)
            {
                TrySpawnCell(out _, spawnOrgans.GetRandom());
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
                    wayPoints.GetRandom(wp => wp.CurrentHull != null && populatedHulls.Count(h => h == wp.CurrentHull) < MaxCellsPerRoom && wp.CurrentHull.WaterPercentage >= MinWaterLevel) ?? 
                    hulls.GetRandom(h => populatedHulls.Count(h2 => h2 == h) < MaxCellsPerRoom && h.WaterPercentage >= MinWaterLevel) as ISpatialEntity;
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
        public void ServerWrite(IWriteMessage msg, Client client, object[] extraData = null)
        {
            msg.Write(IsAlive);
        }
#endif
    }
}