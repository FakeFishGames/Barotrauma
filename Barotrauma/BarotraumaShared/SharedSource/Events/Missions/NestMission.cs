using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class NestMission : Mission
    {
        private readonly XElement itemConfig;
        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<Item, StatusEffect> statusEffectOnApproach = new Dictionary<Item, StatusEffect>();

        //string = filename, point = min,max
        private readonly HashSet<Tuple<CharacterPrefab, Point>> monsterPrefabs = new HashSet<Tuple<CharacterPrefab, Point>>();

        private float itemSpawnRadius = 800.0f;
        private readonly float approachItemsRadius = 1000.0f;
        private readonly float nestObjectRadius = 1000.0f;
        private readonly float monsterSpawnRadius = 3000.0f;
        private readonly int nestObjectAmount = 10;

        private readonly bool requireDelivery;

        private readonly Level.PositionType spawnPositionType;

        private Vector2 nestPosition;


        public override IEnumerable<Vector2> SonarPositions
        {
            get
            {
                if (State > 0)
                {
                    Enumerable.Empty<Vector2>();
                }
                else
                {
                    yield return nestPosition;
                }
            }
        }

        public NestMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            itemConfig = prefab.ConfigElement.Element("Items");

            itemSpawnRadius = prefab.ConfigElement.GetAttributeFloat("itemspawnradius", 800.0f);
            approachItemsRadius = prefab.ConfigElement.GetAttributeFloat("approachitemsradius", itemSpawnRadius * 2.0f);
            monsterSpawnRadius = prefab.ConfigElement.GetAttributeFloat("monsterspawnradius", approachItemsRadius * 2.0f);

            nestObjectRadius = prefab.ConfigElement.GetAttributeFloat("nestobjectradius", itemSpawnRadius * 2.0f);
            nestObjectAmount = prefab.ConfigElement.GetAttributeInt("nestobjectamount", 10);

            requireDelivery = prefab.ConfigElement.GetAttributeBool("requiredelivery", false);

            string spawnPositionTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");
            if (string.IsNullOrWhiteSpace(spawnPositionTypeStr) ||
                !Enum.TryParse(spawnPositionTypeStr, true, out spawnPositionType))
            {
                spawnPositionType = Level.PositionType.Cave | Level.PositionType.Ruin;
            }

            foreach (var monsterElement in prefab.ConfigElement.GetChildElements("monster"))
            {
                string speciesName = monsterElement.GetAttributeString("character", string.Empty);
                int defaultCount = monsterElement.GetAttributeInt("count", -1);
                if (defaultCount < 0)
                {
                    defaultCount = monsterElement.GetAttributeInt("amount", 1);
                }
                int min = Math.Min(monsterElement.GetAttributeInt("min", defaultCount), 255);
                int max = Math.Min(Math.Max(min, monsterElement.GetAttributeInt("max", defaultCount)), 255);
                var characterPrefab = CharacterPrefab.FindBySpeciesName(speciesName);
                if (characterPrefab != null)
                {
                    monsterPrefabs.Add(new Tuple<CharacterPrefab, Point>(characterPrefab, new Point(min, max)));
                }
                else
                {
                    DebugConsole.ThrowError($"Error in monster mission \"{prefab.Identifier}\". Could not find a character prefab with the name \"{speciesName}\".");
                }
            }

        }

        protected override void StartMissionSpecific(Level level)
        {
            if (items.Any())
            {
#if DEBUG
                throw new Exception($"items.Count > 0 ({items.Count})");
#else
                DebugConsole.AddWarning("Item list was not empty at the start of a nest mission. The mission instance may not have been ended correctly on previous rounds.");
                items.Clear();
#endif
            }

            if (!IsClient)
            {
                //ruin/cave/wreck items are allowed to spawn close to the sub
                float minDistance = spawnPositionType == Level.PositionType.Ruin || spawnPositionType == Level.PositionType.Cave || spawnPositionType == Level.PositionType.Wreck ?
                    0.0f : Level.Loaded.Size.X * 0.3f;

                nestPosition = Level.Loaded.GetRandomItemPos(spawnPositionType, 100.0f, minDistance, 30.0f);
                List<GraphEdge> spawnEdges = new List<GraphEdge>();
                if (spawnPositionType == Level.PositionType.Cave)
                {
                    Level.Cave closestCave = null;
                    float closestCaveDist = float.PositiveInfinity;
                    foreach (var cave in Level.Loaded.Caves)
                    {
                        float dist = Vector2.DistanceSquared(nestPosition, cave.Area.Center.ToVector2());
                        if (dist < closestCaveDist)
                        {
                            closestCave = cave;
                            closestCaveDist = dist;
                        }
                    }
                    if (closestCave != null)
                    {
                        closestCave.DisplayOnSonar = true;
                        SpawnNestObjects(level, closestCave);
#if SERVER
                        selectedCave = closestCave;
#endif
                    }
                    var nearbyCells = Level.Loaded.GetCells(nestPosition, searchDepth: 3);
                    if (nearbyCells.Any())
                    {
                        List<GraphEdge> validEdges = new List<GraphEdge>();
                        foreach (var edge in nearbyCells.SelectMany(c => c.Edges))
                        {
                            if (!edge.NextToCave || !edge.IsSolid) { continue; }
                            if (Level.Loaded.ExtraWalls.Any(w => w.IsPointInside(edge.Center + edge.GetNormal(edge.Cell1 ?? edge.Cell2) * 100.0f))) { continue; }
                            validEdges.Add(edge);
                        }

                        if (validEdges.Any())
                        {
                            spawnEdges.AddRange(validEdges.Where(e => MathUtils.LineSegmentToPointDistanceSquared(e.Point1.ToPoint(), e.Point2.ToPoint(), nestPosition.ToPoint()) < itemSpawnRadius * itemSpawnRadius).Distinct());
                        }
                        //no valid edges found close enough to the nest position, find the closest one
                        if (!spawnEdges.Any())
                        {
                            GraphEdge closestEdge = null;
                            float closestDistSqr = float.PositiveInfinity;
                            foreach (var edge in nearbyCells.SelectMany(c => c.Edges))
                            {
                                if (!edge.NextToCave || !edge.IsSolid) { continue; }
                                float dist = Vector2.DistanceSquared(edge.Center, nestPosition);
                                if (dist < closestDistSqr)
                                {
                                    closestEdge = edge;
                                    closestDistSqr = dist;
                                }
                            }
                            if (closestEdge != null)
                            {
                                spawnEdges.Add(closestEdge);
                                itemSpawnRadius = Math.Max(itemSpawnRadius, (float)Math.Sqrt(closestDistSqr) * 1.5f);
                            }
                        }
                    }
                }

                foreach (XElement subElement in itemConfig.Elements())
                {
                    string itemIdentifier = subElement.GetAttributeString("identifier", "");
                    if (!(MapEntityPrefab.Find(null, itemIdentifier) is ItemPrefab itemPrefab))
                    {
                        DebugConsole.ThrowError("Couldn't spawn item for nest mission: item prefab \"" + itemIdentifier + "\" not found");
                        continue;
                    }

                    Vector2 spawnPos = nestPosition;
                    float rotation = 0.0f;
                    if (spawnEdges.Any())
                    {
                        var edge = spawnEdges.GetRandom(Rand.RandSync.Server);
                        spawnPos = Vector2.Lerp(edge.Point1, edge.Point2, Rand.Range(0.1f, 0.9f, Rand.RandSync.Server));
                        Vector2 normal = Vector2.UnitY;
                        if (edge.Cell1 != null && edge.Cell1.CellType == CellType.Solid)
                        {
                            normal = edge.GetNormal(edge.Cell1);
                        }
                        else if (edge.Cell2 != null && edge.Cell2.CellType == CellType.Solid)
                        {
                            normal = edge.GetNormal(edge.Cell2);
                        }
                        spawnPos += normal * 10.0f;
                        rotation = MathUtils.VectorToAngle(normal) - MathHelper.PiOver2;
                    }

                    var item = new Item(itemPrefab, spawnPos, null);
                    item.body.FarseerBody.BodyType = BodyType.Kinematic;
                    item.body.SetTransformIgnoreContacts(item.body.SimPosition, rotation);
                    item.FindHull();
                    items.Add(item);

                    var statusEffectElement = subElement.Element("StatusEffectOnApproach") ?? subElement.Element("statuseffectonapproach");
                    if (statusEffectElement != null)
                    {
                        statusEffectOnApproach.Add(item, StatusEffect.Load(statusEffectElement, Prefab.Identifier));
                    }
                }       
            }
        }

        private void SpawnNestObjects(Level level, Level.Cave cave)
        {
            level.LevelObjectManager.PlaceNestObjects(level, cave, nestPosition, nestObjectRadius, nestObjectAmount);
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (IsClient)
            {
                foreach (Item item in items)
                {
                    if (item.ParentInventory != null && item.body != null) { item.body.FarseerBody.BodyType = BodyType.Dynamic; }
                }
                return;
            }
            switch (State)
            {
                case 0:
                    foreach (Item item in items)
                    {
                        if (item.ParentInventory != null && item.body != null) { item.body.FarseerBody.BodyType = BodyType.Dynamic; }
                        if (statusEffectOnApproach.ContainsKey(item))
                        {
                            foreach (Character character in Character.CharacterList)
                            {
                                if (character.IsPlayer && Vector2.DistanceSquared(nestPosition, character.WorldPosition) < approachItemsRadius * approachItemsRadius)
                                {
                                    statusEffectOnApproach[item].Apply(statusEffectOnApproach[item].type, 1.0f, item, item);
                                    statusEffectOnApproach.Remove(item);
                                    break;
                                }
                            }
                        }
                    }
                    if (monsterPrefabs.Any())
                    {
                        foreach (Character character in Character.CharacterList)
                        {
                            if (character.IsPlayer && Vector2.DistanceSquared(nestPosition, character.WorldPosition) < monsterSpawnRadius * monsterSpawnRadius)
                            {
                                foreach (var monster in monsterPrefabs)
                                {
                                    int amount = Rand.Range(monster.Item2.X, monster.Item2.Y + 1);
                                    for (int i = 0; i < amount; i++)
                                    {
                                        Character.Create(monster.Item1.Identifier, nestPosition + Rand.Vector(100.0f), ToolBox.RandomSeed(8), createNetworkEvent: true);
                                    }
                                }
                                monsterPrefabs.Clear();
                                break;
                            }
                        }
                    }

                    //continue when all items are in the sub or destroyed
                    if (AllItemsDestroyedOrRetrieved()) { State = 1; }                   
                   
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndExit && !Submarine.MainSub.AtStartExit) { return; }
                    State = 2;
                    break;
            }
        }

        private bool AllItemsDestroyedOrRetrieved()
        {
            if (requireDelivery)
            {
                foreach (Item item in items)
                {
                    Submarine parentSub = item.CurrentHull?.Submarine ?? item.GetRootInventoryOwner()?.Submarine;
                    if (parentSub?.Info?.Type == SubmarineType.Player) { continue; }
                    return false;
                }
            }
            else
            {
                foreach (Item item in items)
                {
                    if (item.Removed || item.Condition <= 0.0f) { continue; }
                    if (Vector2.Distance(item.WorldPosition, nestPosition) > Math.Max(itemSpawnRadius * 2, 3000.0f)) { continue; }
                    Submarine parentSub = item.CurrentHull?.Submarine ?? item.GetRootInventoryOwner()?.Submarine;
                    if (parentSub?.Info?.Type == SubmarineType.Player) { continue; }
                    return false;
                }
            }
            return true;
        }

        public override void End()
        {
            if (AllItemsDestroyedOrRetrieved())
            {
                GiveReward();
                completed = true;
                if (completed)
                {
                    if (Prefab.LocationTypeChangeOnCompleted != null)
                    {
                        ChangeLocationType(Prefab.LocationTypeChangeOnCompleted);
                    }
                }
            }
            foreach (Item item in items)
            {
                if (item != null && !item.Removed)
                {
                    item.Remove();
                }
            }
            items.Clear();
            failed = !completed && state > 0;
        }
    }
}
