using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.RuinGeneration;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class ScanMission : Mission
    {
        private readonly XElement itemConfig;
        private readonly List<Item> startingItems = new List<Item>();
        private readonly List<Scanner> scanners = new List<Scanner>();
        private readonly Dictionary<Item, ushort> parentInventoryIDs = new Dictionary<Item, ushort>();
        private readonly Dictionary<Item, byte> parentItemContainerIndices = new Dictionary<Item, byte>();
        private readonly int targetsToScan;
        private readonly Dictionary<WayPoint, bool> scanTargets = new Dictionary<WayPoint, bool>();
        private readonly HashSet<WayPoint> newTargetsScanned = new HashSet<WayPoint>();
        private readonly float minTargetDistance;


        private Ruin TargetRuin { get; set; }

        private bool AllTargetsScanned
        {
            get
            {
                return scanTargets.Any() && scanTargets.All(kvp => kvp.Value);
            }
        } 

        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                if (State > 0)
                {
                    return Enumerable.Empty<Vector2>();
                }
                else if (scanTargets.Any())
                {
                    return scanTargets
                        .Where(kvp => !kvp.Value)
                        .Select(kvp => kvp.Key.WorldPosition);
                }
                else
                {
                    return Enumerable.Empty<Vector2>();
                }
                
            }
        }

        public ScanMission(MissionPrefab prefab, Location[] locations, Submarine sub) : base(prefab, locations, sub)
        {
            itemConfig = prefab.ConfigElement.Element("Items");
            targetsToScan = prefab.ConfigElement.GetAttributeInt("targets", 1);
            minTargetDistance = prefab.ConfigElement.GetAttributeFloat("mintargetdistance", 0.0f);
        }

        protected override void StartMissionSpecific(Level level)
        {
            Reset();

            if (IsClient) { return; }

            if (itemConfig == null)
            {
                DebugConsole.ThrowError("Failed to initialize a Scan mission: item config is not set");
                return;
            }

            foreach (var element in itemConfig.Elements())
            {
                LoadItem(element, null);
            }
            GetScanners();

            TargetRuin = Level.Loaded?.Ruins?.GetRandom(randSync: Rand.RandSync.Server);
            if (TargetRuin == null)
            {
                DebugConsole.ThrowError("Failed to initialize a Scan mission: level contains no alien ruins");
                return;
            }

            var ruinWaypoints = TargetRuin.Submarine.GetWaypoints(false);
            ruinWaypoints.RemoveAll(wp => wp.CurrentHull == null);
            if (ruinWaypoints.Count < targetsToScan)
            {
                DebugConsole.ThrowError($"Failed to initialize a Scan mission: target ruin has less waypoints than required as scan targets ({ruinWaypoints.Count} < {targetsToScan})");
                return;
            }
            var availableWaypoints = new List<WayPoint>();
            float minTargetDistanceSquared = minTargetDistance * minTargetDistance;
            for (int tries = 0; tries < 15; tries++)
            {
                scanTargets.Clear();
                availableWaypoints.Clear();
                availableWaypoints.AddRange(ruinWaypoints);
                for (int i = 0; i < targetsToScan; i++)
                {
                    var selectedWaypoint = availableWaypoints.GetRandom(randSync: Rand.RandSync.Server);
                    scanTargets.Add(selectedWaypoint, false);
                    availableWaypoints.Remove(selectedWaypoint);
                    if (i < (targetsToScan - 1))
                    {
                        availableWaypoints.RemoveAll(wp => wp.CurrentHull == selectedWaypoint.CurrentHull);
                        availableWaypoints.RemoveAll(wp => Vector2.DistanceSquared(wp.WorldPosition, selectedWaypoint.WorldPosition) < minTargetDistanceSquared);
                        if (availableWaypoints.None())
                        {
#if DEBUG
                            DebugConsole.ThrowError($"Error initializing a Scan mission: not enough targets available on try #{tries + 1} to reach the required scan target count (current targets: {scanTargets.Count}, required targets: {targetsToScan})");
#endif
                            break;
                        }
                    }
                }
                if (scanTargets.Count >= targetsToScan)
                {
#if DEBUG
                    DebugConsole.NewMessage($"Successfully initialized a Scan mission: targets set on try #{tries + 1}", Color.Green);
#endif
                    break;
                }
                if ((tries + 1) % 5 == 0)
                {
                    float reducedMinTargetDistance = (1.0f - (((tries + 1) / 5) * 0.1f)) * minTargetDistance;
                    minTargetDistanceSquared = reducedMinTargetDistance * reducedMinTargetDistance;
#if DEBUG
                    DebugConsole.NewMessage($"Reducing minimum distance between Scan mission targets (new min: {reducedMinTargetDistance}) to reach the required target count", Color.Yellow);
#endif
                }
            }
            if (scanTargets.Count < targetsToScan)
            {
                DebugConsole.ThrowError($"Error initializing a Scan mission: not enough targets (current targets: {scanTargets.Count}, required targets: {targetsToScan})");
            }
        }

        private void Reset()
        {
            startingItems.Clear();
            parentInventoryIDs.Clear();
            parentItemContainerIndices.Clear();
            scanners.Clear();
            TargetRuin = null;
            scanTargets.Clear();
        }

        private void LoadItem(XElement element, Item parent)
        {
            var itemPrefab = FindItemPrefab(element);
            Vector2? position = GetCargoSpawnPosition(itemPrefab, out Submarine cargoRoomSub);
            if (!position.HasValue) { return; }
            var item = new Item(itemPrefab, position.Value, cargoRoomSub);
            item.FindHull();
            startingItems.Add(item);
            if (parent?.GetComponent<ItemContainer>() is ItemContainer itemContainer)
            {
                parentInventoryIDs.Add(item, parent.ID);
                parentItemContainerIndices.Add(item, (byte)parent.GetComponentIndex(itemContainer));
                parent.Combine(item, user: null);
            }
            foreach (XElement subElement in element.Elements())
            {
                int amount = subElement.GetAttributeInt("amount", 1);
                for (int i = 0; i < amount; i++)
                {
                    LoadItem(subElement, item);
                }
            }
        }

        private void GetScanners()
        {
            foreach (var startingItem in startingItems)
            {
                if (startingItem.GetComponent<Scanner>() is Scanner scanner)
                {
                    scanner.OnScanStarted += OnScanStarted;
                    if (!IsClient)
                    {
                        scanner.OnScanCompleted += OnScanCompleted;
                    }
                    scanners.Add(scanner);
                }
            }
        }

        private void OnScanStarted(Scanner scanner)
        {
            float scanRadiusSquared = scanner.ScanRadius * scanner.ScanRadius;
            foreach (var kvp in scanTargets)
            {
                if (!IsValidScanPosition(scanner, kvp, scanRadiusSquared)) { continue; }
                scanner.DisplayProgressBar = true;
                break;
            }
        }

        private void OnScanCompleted(Scanner scanner)
        {
            if (IsClient) { return; }
            newTargetsScanned.Clear();
            float scanRadiusSquared = scanner.ScanRadius * scanner.ScanRadius;
            foreach (var kvp in scanTargets)
            {
                if (!IsValidScanPosition(scanner, kvp, scanRadiusSquared)) { continue; }
                newTargetsScanned.Add(kvp.Key);
            }
            foreach (var wp in newTargetsScanned)
            {
                scanTargets[wp] = true;
            }
#if SERVER
            // Server should make sure that the clients' scan target status is in-sync
            GameMain.Server?.UpdateMissionState(this);
#endif
        }

        private bool IsValidScanPosition(Scanner scanner, KeyValuePair<WayPoint, bool> scanStatus, float scanRadiusSquared)
        {
            if (scanStatus.Value) { return false; }
            if (scanStatus.Key.Submarine != scanner.Item.Submarine) { return false; }
            if (Vector2.DistanceSquared(scanStatus.Key.WorldPosition, scanner.Item.WorldPosition) > scanRadiusSquared) { return false; }
            return true;
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (IsClient) { return; }
            switch (State)
            {
                case 0:
                    if (!AllTargetsScanned) { return; }
                    State = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndExit && !Submarine.MainSub.AtStartExit) { return; }
                    State = 2;
                    break;
            }
        }

        public override void End()
        {
            if (State == 2 && AllScannersReturned())
            {
                GiveReward();
                completed = true;
            }
            foreach (var scanner in scanners)
            {
                if (scanner.Item != null && !scanner.Item.Removed)
                {
                    scanner.OnScanStarted -= OnScanStarted;
                    scanner.OnScanCompleted -= OnScanCompleted;
                    scanner.Item.Remove();
                }
            }
            Reset();
            failed = !completed && state > 0;

            bool AllScannersReturned()
            {
                foreach (var scanner in scanners)
                {
                    if (scanner?.Item == null || scanner.Item.Removed) { return false; }
                    var owner = scanner.Item.GetRootInventoryOwner();
                    if (owner.Submarine != null && owner.Submarine.Info.Type == SubmarineType.Player)
                    {
                        continue;
                    }
                    else if (owner is Character c && c.Info != null && GameMain.GameSession.CrewManager.CharacterInfos.Contains(c.Info))
                    {
                        continue;
                    }
                    return false;
                }
                return true;
            }
        }
    }
}