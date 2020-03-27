using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.Networking;
using System.Linq;
using System;

namespace Barotrauma
{
    class WreckAI : IServerSerializable
    {
        public Submarine Wreck { get; private set; }

        public bool IsAlive { get; private set; }

        private readonly List<Item> allItems;
        private readonly List<Item> thalamusItems;
        private readonly List<Turret> turrets = new List<Turret>();
        private readonly List<WayPoint> wayPoints = new List<WayPoint>();
        private readonly List<Hull> hulls = new List<Hull>();
        private readonly List<Item> spawnOrgans = new List<Item>();
        private readonly Item brain;

        private bool initialCellsSpawned;

        public readonly WreckAIConfig Config;

        private bool IsClient => GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

        public WreckAI(Submarine wreck, Item brain, List<Item> items = null)
        {
            Config = WreckAIConfig.GetRandom();
            if (Config == null)
            {
                DebugConsole.ThrowError("WreckAI: No wreck AI config found!");
                Kill();
                return;
            }
            allItems = items ?? wreck.GetItems(false);
            thalamusItems = allItems.FindAll(i => i.Prefab.Category == MapEntityCategory.Thalamus || i.HasTag("thalamus"));
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
                                if (container.Inventory.Items[i] != null) { continue; }
                                if (MapEntityPrefab.List.GetRandom(e => e is ItemPrefab i && container.CanBeContained(i) && 
                                        Config.ForbiddenAmmunition.None(id => id.Equals(i.Identifier, StringComparison.OrdinalIgnoreCase)), Rand.RandSync.Server) is ItemPrefab ammoPrefab)
                                {
                                    Item ammo = new Item(ammoPrefab, container.Item.WorldPosition, wreck);
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
            this.brain = brain;
            Wreck = wreck;
            foreach (var item in Wreck.GetItems(false))
            {
                var turret = item.GetComponent<Turret>();
                if (turret != null)
                {
                    turrets.Add(turret);
                }
                if (item.HasTag("cellspawnorgan"))
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
        }

        private readonly List<Item> destroyedOrgans = new List<Item>();
        public void Update(float deltaTime)
        {
            if (!IsAlive || Wreck == null || Wreck.Removed)
            {
                cells.ForEach(c => c.OnDeath -= OnCellDeath);
                return;
            }
            if (brain == null || brain.Removed || brain.Condition <= 0)
            {
                Kill();
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
                if (submarine.Info.Type != SubmarineInfo.SubmarineType.Player) { continue; }
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
            cellsOutside = Math.Clamp(cellsOutside + brainRoomCells + cellsInside - cells.Count, cellsOutside, MaxCellsOutside);
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
            if (!IsClient)
            {
                brain.Condition = 0;
            }
            IsAlive = false;
        }

        // The client doesn't use these, so we don't have to sync them.
        private readonly List<Character> cells = new List<Character>();
        // Intentionally contains duplicates.
        private readonly List<Hull> populatedHulls = new List<Hull>();
        private float cellSpawnTimer;

        private float CellSpawnTime => Config.CellSpawnTime;
        private float CellSpawnRandomFactor => Config.CellSpawnRandomFactor;
        private int MinCellsPerBrainRoom => Config.MinCellsPerBrainRoom;
        private int MaxCellsPerRoom => Config.MaxCellsPerRoom;
        private int MinCellsOutside => Config.MinCellsOutside;
        private int MaxCellsOutside => Config.MaxCellsOutside;
        private int MinCellsInside => Config.MinCellsInside;
        private int MaxCellsInside => Config.MaxCellsInside;
        private int MaxCellCount => Config.MaxCellCount;
        private float MinWaterLevel => Config.MinWaterLevel;

        void UpdateReinforcements(float deltaTime)
        {
            if (cells.Count >= MaxCellCount) { return; }
            cellSpawnTimer -= deltaTime;
            if (cellSpawnTimer < 0)
            {
                TrySpawnCell(out _, spawnOrgans.GetRandom());
                cellSpawnTimer = CellSpawnTime * Rand.Range(CellSpawnRandomFactor, 1 + CellSpawnRandomFactor);
            }
        }

        bool TrySpawnCell(out Character cell, ISpatialEntity targetEntity = null)
        {
            cell = null;
            if (cells.Count >= MaxCellCount) { return false; }
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
            cell = Character.Create("Leucocyte", targetEntity.WorldPosition, ToolBox.RandomSeed(8), hasAi: true, createNetworkEvent: true);
            cells.Add(cell);
            cell.OnDeath += OnCellDeath;
            cellSpawnTimer = CellSpawnTime * Rand.Range(CellSpawnRandomFactor, 1 + CellSpawnRandomFactor);
            return true;
        }
        
        void OperateTurrets(float deltaTime)
        {
            foreach (var turret in turrets)
            {
                // Never target other creatures than humans with the turrets.
                turret.ThalamusOperate(deltaTime, 
                    !turret.Item.HasTag("ignorecharacters"), 
                    targetOtherCreatures: false, 
                    !turret.Item.HasTag("ignoresubmarines"), 
                    turret.Item.HasTag("ignoreaimdelay"));
            }
        }

        void OnCellDeath(Character character, CauseOfDeath causeOfDeath)
        {
            cells.Remove(character);
        }

#if SERVER
        public void ServerWrite(IWriteMessage msg, Client client, object[] extraData = null)
        {
            msg.Write(IsAlive);
        }
#endif
#if CLIENT
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            IsAlive = msg.ReadBoolean();
        }
#endif
    }
}