#if CLIENT
using Barotrauma.Particles;
#endif
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Voronoi2;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class LevelObjectManager : Entity, IServerSerializable
    {
        const int GridSize = 2000;

        private List<LevelObject> objects;
        private List<LevelObject> updateableObjects;
        private List<LevelObject>[,] objectGrid;

        public float GlobalForceDecreaseTimer
        {
            get;
            private set;
        }

        public LevelObjectManager() : base(null, Entity.NullEntityID)
        {
        }

        class SpawnPosition
        {
            public readonly GraphEdge GraphEdge;
            public readonly Vector2 Normal;
            public readonly List<LevelObjectPrefab.SpawnPosType> SpawnPosTypes = new List<LevelObjectPrefab.SpawnPosType>();
            public readonly Alignment Alignment;
            public readonly float Length;

            private readonly float noiseVal;


            public SpawnPosition(GraphEdge graphEdge, Vector2 normal, LevelObjectPrefab.SpawnPosType spawnPosType, Alignment alignment)
                : this(graphEdge, normal, spawnPosType.ToEnumerable(), alignment)
            { }

            public SpawnPosition(GraphEdge graphEdge, Vector2 normal, IEnumerable<LevelObjectPrefab.SpawnPosType> spawnPosTypes, Alignment alignment)
            {
                GraphEdge = graphEdge;
                Normal = normal.NearlyEquals(Vector2.Zero) ? Vector2.UnitY : Vector2.Normalize(normal);
                SpawnPosTypes.AddRange(spawnPosTypes);

                if (spawnPosTypes.Contains(LevelObjectPrefab.SpawnPosType.MainPath) || 
                    spawnPosTypes.Contains(LevelObjectPrefab.SpawnPosType.LevelStart) ||
                    spawnPosTypes.Contains(LevelObjectPrefab.SpawnPosType.LevelEnd))
                {
                    Length = 1000.0f;
                    Normal = Vector2.Zero;
                    Alignment = Alignment.Any;
                }
                else
                {
                    Alignment = alignment;                
                    Length = Vector2.Distance(graphEdge.Point1, graphEdge.Point2);
                }

                noiseVal =  
                    (float)(PerlinNoise.CalculatePerlin(GraphEdge.Point1.X / 10000.0f, GraphEdge.Point1.Y / 10000.0f, 0.5f) +
                            PerlinNoise.CalculatePerlin(GraphEdge.Point1.X / 20000.0f, GraphEdge.Point1.Y / 20000.0f, 0.5f));
            }

            public float GetSpawnProbability(LevelObjectPrefab prefab)
            {
                if (prefab.ClusteringAmount <= 0.0f) { return Length; }
                float noise = (noiseVal + PerlinNoise.GetPerlin(prefab.ClusteringGroup, prefab.ClusteringGroup * 0.3f)) % 1.0f;
                return Length * (float)Math.Pow(noise, prefab.ClusteringAmount);
            }
        }

        public void PlaceObjects(Level level, int amount)
        {
            objectGrid = new List<LevelObject>[
                level.Size.X / GridSize,
                (level.Size.Y - level.BottomPos) / GridSize];

            List<SpawnPosition> availableSpawnPositions = new List<SpawnPosition>();
            var levelCells = level.GetAllCells();
            availableSpawnPositions.AddRange(GetAvailableSpawnPositions(levelCells, LevelObjectPrefab.SpawnPosType.Wall));
            availableSpawnPositions.AddRange(GetAvailableSpawnPositions(level.SeaFloor.Cells, LevelObjectPrefab.SpawnPosType.SeaFloor));

            foreach (Structure structure in Structure.WallList)
            {
                if (!structure.HasBody || structure.HiddenInGame) { continue; }
                if (level.Ruins.Any(r => r.Submarine == structure.Submarine))
                {
                    if (structure.IsHorizontal)
                    {
                        bool topHull = Hull.FindHull(structure.WorldPosition + Vector2.UnitY * 64) != null;
                        bool bottomHull = Hull.FindHull(structure.WorldPosition - Vector2.UnitY * 64) != null;
                        if (topHull && bottomHull ) { continue; }

                        availableSpawnPositions.Add(new SpawnPosition(
                            new GraphEdge(new Vector2(structure.WorldRect.X, structure.WorldPosition.Y), new Vector2(structure.WorldRect.Right, structure.WorldPosition.Y)),
                            bottomHull ? Vector2.UnitY : -Vector2.UnitY,
                            LevelObjectPrefab.SpawnPosType.RuinWall,
                            bottomHull ? Alignment.Bottom : Alignment.Top));
                    }
                    else
                    {
                        bool rightHull = Hull.FindHull(structure.WorldPosition + Vector2.UnitX * 64) != null;
                        bool leftHull = Hull.FindHull(structure.WorldPosition - Vector2.UnitX * 64) != null;
                        if (rightHull && leftHull) { continue; }

                        availableSpawnPositions.Add(new SpawnPosition(
                            new GraphEdge(new Vector2(structure.WorldPosition.X, structure.WorldRect.Y), new Vector2(structure.WorldPosition.X, structure.WorldRect.Y - structure.WorldRect.Height)),
                            leftHull ? Vector2.UnitX : -Vector2.UnitX,
                            LevelObjectPrefab.SpawnPosType.RuinWall,
                            leftHull ? Alignment.Left : Alignment.Right));
                    }
                }
            }

            foreach (var posOfInterest in level.PositionsOfInterest)
            {
                if (posOfInterest.PositionType != Level.PositionType.MainPath && posOfInterest.PositionType != Level.PositionType.SidePath) { continue; }

                availableSpawnPositions.Add(new SpawnPosition(
                    new GraphEdge(posOfInterest.Position.ToVector2(), posOfInterest.Position.ToVector2() + Vector2.UnitX), 
                    Vector2.UnitY, 
                    LevelObjectPrefab.SpawnPosType.MainPath, 
                    Alignment.Top));
            }

            availableSpawnPositions.Add(new SpawnPosition(
                new GraphEdge(level.StartPosition - Vector2.UnitX, level.StartPosition + Vector2.UnitX),
                -Vector2.UnitY, LevelObjectPrefab.SpawnPosType.LevelStart, Alignment.Top));
            availableSpawnPositions.Add(new SpawnPosition(
                new GraphEdge(level.EndPosition - Vector2.UnitX, level.EndPosition + Vector2.UnitX),
                -Vector2.UnitY, LevelObjectPrefab.SpawnPosType.LevelEnd, Alignment.Top));

            var availablePrefabs =LevelObjectPrefab.Prefabs.OrderBy(p => p.UintIdentifier).ToList();
            objects = new List<LevelObject>();
            updateableObjects = new List<LevelObject>();

            Dictionary<LevelObjectPrefab, List<SpawnPosition>> suitableSpawnPositions = new Dictionary<LevelObjectPrefab, List<SpawnPosition>>();
            Dictionary<LevelObjectPrefab, List<float>> spawnPositionWeights = new Dictionary<LevelObjectPrefab, List<float>>();
            for (int i = 0; i < amount; i++)
            {
                //get a random prefab and find a place to spawn it
                LevelObjectPrefab prefab = GetRandomPrefab(level.GenerationParams, availablePrefabs);
                if (prefab == null) { continue; }
                if (!suitableSpawnPositions.ContainsKey(prefab))
                {
                    float minDistance = level.Size.X * 0.2f;

                    suitableSpawnPositions.Add(prefab, 
                        availableSpawnPositions.Where(sp =>
                            sp.SpawnPosTypes.Any(type => prefab.SpawnPos.HasFlag(type)) && 
                            sp.Length >= prefab.MinSurfaceWidth &&
                            (prefab.AllowAtStart || !level.IsCloseToStart(sp.GraphEdge.Center, minDistance)) &&
                            (prefab.AllowAtEnd || !level.IsCloseToEnd(sp.GraphEdge.Center, minDistance)) &&
                            (sp.Alignment == Alignment.Any || prefab.Alignment.HasFlag(sp.Alignment))).ToList());

                    spawnPositionWeights.Add(prefab,
                        suitableSpawnPositions[prefab].Select(sp => sp.GetSpawnProbability(prefab)).ToList());
                }

                SpawnPosition spawnPosition = ToolBox.SelectWeightedRandom(suitableSpawnPositions[prefab], spawnPositionWeights[prefab], Rand.RandSync.ServerAndClient);
                if (spawnPosition == null && prefab.SpawnPos != LevelObjectPrefab.SpawnPosType.None) { continue; }
                PlaceObject(prefab, spawnPosition, level);
                if (prefab.MaxCount < amount)
                {
                    if (objects.Count(o => o.Prefab == prefab) >= prefab.MaxCount)
                    {
                        availablePrefabs.Remove(prefab);
                    }
                }
            }

            foreach (Level.Cave cave in level.Caves)
            {
                availablePrefabs = LevelObjectPrefab.Prefabs.Where(p => p.SpawnPos.HasFlag(LevelObjectPrefab.SpawnPosType.CaveWall))
                    .OrderBy(p => p.UintIdentifier).ToList();
                availableSpawnPositions.Clear();
                suitableSpawnPositions.Clear(); 
                spawnPositionWeights.Clear();

                var caveCells = cave.Tunnels.SelectMany(t => t.Cells);
                List<VoronoiCell> caveWallCells = new List<VoronoiCell>();
                foreach (var edge in caveCells.SelectMany(c => c.Edges))
                {
                    if (!edge.NextToCave) { continue; }
                    if (edge.Cell1?.CellType == CellType.Solid) { caveWallCells.Add(edge.Cell1); }
                    if (edge.Cell2?.CellType == CellType.Solid) { caveWallCells.Add(edge.Cell2); }
                }
                availableSpawnPositions.AddRange(GetAvailableSpawnPositions(caveWallCells.Distinct(), LevelObjectPrefab.SpawnPosType.CaveWall));

                for (int i = 0; i < cave.CaveGenerationParams.LevelObjectAmount; i++)
                {
                    //get a random prefab and find a place to spawn it
                    LevelObjectPrefab prefab = GetRandomPrefab(cave.CaveGenerationParams, availablePrefabs, requireCaveSpecificOverride: true);
                    if (prefab == null) { continue; }
                    if (!suitableSpawnPositions.ContainsKey(prefab))
                    {
                        suitableSpawnPositions.Add(prefab,
                            availableSpawnPositions.Where(sp =>
                                sp.Length >= prefab.MinSurfaceWidth &&
                                (sp.Alignment == Alignment.Any || prefab.Alignment.HasFlag(sp.Alignment))).ToList());
                        spawnPositionWeights.Add(prefab,
                            suitableSpawnPositions[prefab].Select(sp => sp.GetSpawnProbability(prefab)).ToList());
                    }
                    SpawnPosition spawnPosition = ToolBox.SelectWeightedRandom(suitableSpawnPositions[prefab], spawnPositionWeights[prefab], Rand.RandSync.ServerAndClient);
                    if (spawnPosition == null && prefab.SpawnPos != LevelObjectPrefab.SpawnPosType.None) { continue; }
                    PlaceObject(prefab, spawnPosition, level, cave);
                    if (prefab.MaxCount < amount)
                    {
                        if (objects.Count(o => o.Prefab == prefab && o.ParentCave == cave) >= prefab.MaxCount)
                        {
                            availablePrefabs.Remove(prefab);
                        }
                    }
                }             
            }
        }

        public void PlaceNestObjects(Level level, Level.Cave cave, Vector2 nestPosition, float nestRadius, int objectAmount)
        {
            Rand.SetSyncedSeed(ToolBox.StringToInt(level.Seed));

            var availablePrefabs = LevelObjectPrefab.Prefabs.Where(p => p.SpawnPos.HasFlag(LevelObjectPrefab.SpawnPosType.NestWall))
                .OrderBy(p => p.UintIdentifier).ToList();
            Dictionary<LevelObjectPrefab, List<SpawnPosition>> suitableSpawnPositions = new Dictionary<LevelObjectPrefab, List<SpawnPosition>>();
            Dictionary<LevelObjectPrefab, List<float>> spawnPositionWeights = new Dictionary<LevelObjectPrefab, List<float>>();

            List<SpawnPosition> availableSpawnPositions = new List<SpawnPosition>();
            var caveCells = cave.Tunnels.SelectMany(t => t.Cells);
            List<VoronoiCell> caveWallCells = new List<VoronoiCell>();
            foreach (var edge in caveCells.SelectMany(c => c.Edges))
            {
                if (!edge.NextToCave) { continue; }
                if (MathUtils.LineSegmentToPointDistanceSquared(edge.Point1.ToPoint(), edge.Point2.ToPoint(), nestPosition.ToPoint()) > nestRadius * nestRadius) { continue; }
                if (edge.Cell1?.CellType == CellType.Solid) { caveWallCells.Add(edge.Cell1); }
                if (edge.Cell2?.CellType == CellType.Solid) { caveWallCells.Add(edge.Cell2); }
            }
            availableSpawnPositions.AddRange(GetAvailableSpawnPositions(caveWallCells.Distinct(), LevelObjectPrefab.SpawnPosType.CaveWall));

            for (int i = 0; i < objectAmount; i++)
            {
                //get a random prefab and find a place to spawn it
                LevelObjectPrefab prefab = GetRandomPrefab(cave.CaveGenerationParams, availablePrefabs, requireCaveSpecificOverride: false);
                if (prefab == null) { continue; }
                if (!suitableSpawnPositions.ContainsKey(prefab))
                {
                    suitableSpawnPositions.Add(prefab,
                        availableSpawnPositions.Where(sp =>
                            sp.Length >= prefab.MinSurfaceWidth &&
                            (sp.Alignment == Alignment.Any || prefab.Alignment.HasFlag(sp.Alignment))).ToList());
                    spawnPositionWeights.Add(prefab,
                        suitableSpawnPositions[prefab].Select(sp => sp.GetSpawnProbability(prefab)).ToList());
                }
                SpawnPosition spawnPosition = ToolBox.SelectWeightedRandom(suitableSpawnPositions[prefab], spawnPositionWeights[prefab], Rand.RandSync.ServerAndClient);
                if (spawnPosition == null && prefab.SpawnPos != LevelObjectPrefab.SpawnPosType.None) { continue; }
                PlaceObject(prefab, spawnPosition, level);
                if (objects.Count(o => o.Prefab == prefab) >= prefab.MaxCount)
                {
                    availablePrefabs.Remove(prefab);
                }                    
            }            
        }

        private void PlaceObject(LevelObjectPrefab prefab, SpawnPosition spawnPosition, Level level, Level.Cave parentCave = null)
        {
            float rotation = 0.0f;
            if (prefab.AlignWithSurface && spawnPosition.Normal.LengthSquared() > 0.001f && spawnPosition != null)
            {
                rotation = MathUtils.VectorToAngle(new Vector2(spawnPosition.Normal.Y, spawnPosition.Normal.X));
            }
            rotation += Rand.Range(prefab.RandomRotationRad.X, prefab.RandomRotationRad.Y, Rand.RandSync.ServerAndClient);

            Vector2 position = Vector2.Zero;
            Vector2 edgeDir = Vector2.UnitX;
            if (spawnPosition == null)
            {
                position = new Vector2(
                    Rand.Range(0.0f, level.Size.X, Rand.RandSync.ServerAndClient),
                    Rand.Range(0.0f, level.Size.Y, Rand.RandSync.ServerAndClient));
            }
            else
            {
                edgeDir = (spawnPosition.GraphEdge.Point1 - spawnPosition.GraphEdge.Point2) / spawnPosition.Length;
                position = spawnPosition.GraphEdge.Point2 + edgeDir * Rand.Range(prefab.MinSurfaceWidth / 2.0f, spawnPosition.Length - prefab.MinSurfaceWidth / 2.0f, Rand.RandSync.ServerAndClient);
            }

            if (!MathUtils.NearlyEqual(prefab.RandomOffset.X, 0.0f) || !MathUtils.NearlyEqual(prefab.RandomOffset.Y, 0.0f))
            {
                Vector2 offsetDir = spawnPosition.Normal.LengthSquared() > 0.001f ? spawnPosition.Normal : Rand.Vector(1.0f, Rand.RandSync.ServerAndClient);
                position += offsetDir * Rand.Range(prefab.RandomOffset.X, prefab.RandomOffset.Y, Rand.RandSync.ServerAndClient);
            }

            var newObject = new LevelObject(prefab,
                new Vector3(position, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, Rand.RandSync.ServerAndClient)), Rand.Range(prefab.MinSize, prefab.MaxSize, Rand.RandSync.ServerAndClient), rotation);
            AddObject(newObject, level);
            newObject.ParentCave = parentCave;

            foreach (LevelObjectPrefab.ChildObject child in prefab.ChildObjects)
            {
                int childCount = Rand.Range(child.MinCount, child.MaxCount + 1, Rand.RandSync.ServerAndClient);
                for (int j = 0; j < childCount; j++)
                {
                    var matchingPrefabs = LevelObjectPrefab.Prefabs.Where(p => child.AllowedNames.Contains(p.Name));
                    int prefabCount = matchingPrefabs.Count();
                    var childPrefab = prefabCount == 0 ? null : matchingPrefabs.ElementAt(Rand.Range(0, prefabCount, Rand.RandSync.ServerAndClient));
                    if (childPrefab == null) { continue; }

                    Vector2 childPos = position + edgeDir * Rand.Range(-0.5f, 0.5f, Rand.RandSync.ServerAndClient) * prefab.MinSurfaceWidth;

                    var childObject = new LevelObject(childPrefab,
                        new Vector3(childPos, Rand.Range(childPrefab.DepthRange.X, childPrefab.DepthRange.Y, Rand.RandSync.ServerAndClient)),
                        Rand.Range(childPrefab.MinSize, childPrefab.MaxSize, Rand.RandSync.ServerAndClient),
                        rotation + Rand.Range(childPrefab.RandomRotationRad.X, childPrefab.RandomRotationRad.Y, Rand.RandSync.ServerAndClient));

                    AddObject(childObject, level);
                    childObject.ParentCave = parentCave;
                }
            }
        }

        private void AddObject(LevelObject newObject, Level level)
        {
            if (newObject.Triggers != null) 
            { 
                foreach (LevelTrigger trigger in newObject.Triggers)
                {
                    trigger.OnTriggered += (levelTrigger, obj) =>
                    {
                        OnObjectTriggered(newObject, levelTrigger, obj);
                    };
                }
            }
            
            var spriteCorners = new List<Vector2>
            {
                Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero
            };

            Sprite sprite = newObject.Sprite ?? newObject.Prefab.DeformableSprite?.Sprite;

            //calculate the positions of the corners of the rotated sprite
            if (sprite != null)
            {
                Vector2 halfSize = sprite.size * newObject.Scale / 2;
                spriteCorners[0] = -halfSize;
                spriteCorners[1] = new Vector2(-halfSize.X, halfSize.Y);
                spriteCorners[2] = halfSize;
                spriteCorners[3] = new Vector2(halfSize.X, -halfSize.Y);

                Vector2 pivotOffset = sprite.Origin * newObject.Scale - halfSize;
                pivotOffset.X = -pivotOffset.X;
                pivotOffset = new Vector2(
                    (float)(pivotOffset.X * Math.Cos(-newObject.Rotation) - pivotOffset.Y * Math.Sin(-newObject.Rotation)),
                    (float)(pivotOffset.X * Math.Sin(-newObject.Rotation) + pivotOffset.Y * Math.Cos(-newObject.Rotation)));

                for (int j = 0; j < 4; j++)
                {
                    spriteCorners[j] = new Vector2(
                        (float)(spriteCorners[j].X * Math.Cos(-newObject.Rotation) - spriteCorners[j].Y * Math.Sin(-newObject.Rotation)),
                        (float)(spriteCorners[j].X * Math.Sin(-newObject.Rotation) + spriteCorners[j].Y * Math.Cos(-newObject.Rotation)));

                    spriteCorners[j] += new Vector2(newObject.Position.X, newObject.Position.Y) + pivotOffset;
                }
            }

            float minX = spriteCorners.Min(c => c.X) - newObject.Position.Z;
            float maxX = spriteCorners.Max(c => c.X) + newObject.Position.Z;

            float minY = spriteCorners.Min(c => c.Y) - newObject.Position.Z - level.BottomPos;
            float maxY = spriteCorners.Max(c => c.Y) + newObject.Position.Z - level.BottomPos;

            if (newObject.Triggers != null)
            {
                foreach (LevelTrigger trigger in newObject.Triggers)
                {
                    if (trigger.PhysicsBody == null) { continue; }
                    for (int i = 0; i < trigger.PhysicsBody.FarseerBody.FixtureList.Count; i++)
                    {
                        trigger.PhysicsBody.FarseerBody.GetTransform(out FarseerPhysics.Common.Transform transform);
                        trigger.PhysicsBody.FarseerBody.FixtureList[i].Shape.ComputeAABB(out FarseerPhysics.Collision.AABB aabb, ref transform, i);

                        minX = Math.Min(minX, ConvertUnits.ToDisplayUnits(aabb.LowerBound.X));
                        maxX = Math.Max(maxX, ConvertUnits.ToDisplayUnits(aabb.UpperBound.X));
                        minY = Math.Min(minY, ConvertUnits.ToDisplayUnits(aabb.LowerBound.Y) - level.BottomPos);
                        maxY = Math.Max(maxY, ConvertUnits.ToDisplayUnits(aabb.UpperBound.Y) - level.BottomPos);
                    }
                }
            }

#if CLIENT
            if (newObject.ParticleEmitters != null)
            {
                foreach (ParticleEmitter emitter in newObject.ParticleEmitters)
                {
                    Rectangle particleBounds = emitter.CalculateParticleBounds(new Vector2(newObject.Position.X, newObject.Position.Y));
                    minX = Math.Min(minX, particleBounds.X);
                    maxX = Math.Max(maxX, particleBounds.Right);
                    minY = Math.Min(minY, particleBounds.Y - level.BottomPos);
                    maxY = Math.Max(maxY, particleBounds.Bottom - level.BottomPos);
                }
            }
#endif
            objects.Add(newObject);
            if (newObject.NeedsUpdate) { updateableObjects.Add(newObject); }
            newObject.Position.Z += (minX + minY) % 100.0f * 0.00001f;

            int xStart = (int)Math.Floor(minX / GridSize);
            int xEnd = (int)Math.Floor(maxX / GridSize);
            if (xEnd < 0 || xStart >= objectGrid.GetLength(0)) return;

            int yStart = (int)Math.Floor(minY / GridSize);
            int yEnd = (int)Math.Floor(maxY / GridSize);
            if (yEnd < 0 || yStart >= objectGrid.GetLength(1)) return;

            xStart = Math.Max(xStart, 0);
            xEnd = Math.Min(xEnd, objectGrid.GetLength(0) - 1);
            yStart = Math.Max(yStart, 0);
            yEnd = Math.Min(yEnd, objectGrid.GetLength(1) - 1);

            for (int x = xStart; x <= xEnd; x++)
            {
                for (int y = yStart; y <= yEnd; y++)
                {
                    if (objectGrid[x, y] == null) objectGrid[x, y] = new List<LevelObject>();
                    objectGrid[x, y].Add(newObject);
                }
            }            
        }

        public Microsoft.Xna.Framework.Point GetGridIndices(Vector2 worldPosition)        
        {
            return new Microsoft.Xna.Framework.Point(
                (int)Math.Floor(worldPosition.X / GridSize),
                (int)Math.Floor((worldPosition.Y - Level.Loaded.BottomPos) / GridSize));
        }

        public IEnumerable<LevelObject> GetAllObjects()
        {
            return objects;
        }

        private readonly static HashSet<LevelObject> objectsInRange = new HashSet<LevelObject>();
        public IEnumerable<LevelObject> GetAllObjects(Vector2 worldPosition, float radius)
        {
            var minIndices = GetGridIndices(worldPosition - Vector2.One * radius);
            if (minIndices.X >= objectGrid.GetLength(0) || minIndices.Y >= objectGrid.GetLength(1)) { return Enumerable.Empty<LevelObject>(); }

            var maxIndices = GetGridIndices(worldPosition + Vector2.One * radius);
            if (maxIndices.X < 0 || maxIndices.Y < 0) { return Enumerable.Empty<LevelObject>(); }

            minIndices.X = Math.Max(0, minIndices.X);
            minIndices.Y = Math.Max(0, minIndices.Y);
            maxIndices.X = Math.Min(objectGrid.GetLength(0) - 1, maxIndices.X);
            maxIndices.Y = Math.Min(objectGrid.GetLength(1) - 1, maxIndices.Y);

            objectsInRange.Clear();
            for (int x = minIndices.X; x <= maxIndices.X; x++)
            {
                for (int y = minIndices.Y; y <= maxIndices.Y; y++)
                {
                    if (objectGrid[x, y] == null) { continue; }
                    foreach (LevelObject obj in objectGrid[x, y])
                    {
                        if (obj.Prefab.HideWhenBroken && obj.Health <= 0.0f) { continue; }
                        objectsInRange.Add(obj);
                    }
                }
            }

            return objectsInRange;
        }

        private List<SpawnPosition> GetAvailableSpawnPositions(IEnumerable<VoronoiCell> cells, LevelObjectPrefab.SpawnPosType spawnPosType, bool checkFlags = true)
        {
            List<LevelObjectPrefab.SpawnPosType> spawnPosTypes = new List<LevelObjectPrefab.SpawnPosType>(4);
            List<SpawnPosition> availableSpawnPositions = new List<SpawnPosition>();
            foreach (var cell in cells)
            {
                foreach (var edge in cell.Edges)
                {
                    if (!edge.IsSolid || edge.OutsideLevel) { continue; }
                    if (spawnPosType != LevelObjectPrefab.SpawnPosType.CaveWall && edge.NextToCave) { continue; } 
                    Vector2 normal = edge.GetNormal(cell);

                    Alignment edgeAlignment = 0;
                    if (normal.Y < -0.5f)
                        edgeAlignment |= Alignment.Bottom;
                    else if (normal.Y > 0.5f)
                        edgeAlignment |= Alignment.Top;
                    else if (normal.X < -0.5f)
                        edgeAlignment |= Alignment.Left;
                    else if(normal.X > 0.5f)
                        edgeAlignment |= Alignment.Right;

                    spawnPosTypes.Clear();
                    spawnPosTypes.Add(spawnPosType);
                    if (spawnPosType.HasFlag(LevelObjectPrefab.SpawnPosType.MainPathWall) && edge.NextToMainPath) { spawnPosTypes.Add(LevelObjectPrefab.SpawnPosType.MainPathWall); }
                    if (spawnPosType.HasFlag(LevelObjectPrefab.SpawnPosType.SidePathWall) && edge.NextToSidePath) { spawnPosTypes.Add(LevelObjectPrefab.SpawnPosType.SidePathWall); }
                    if (spawnPosType.HasFlag(LevelObjectPrefab.SpawnPosType.CaveWall) && edge.NextToCave) { spawnPosTypes.Add(LevelObjectPrefab.SpawnPosType.CaveWall); }                   

                    availableSpawnPositions.Add(new SpawnPosition(edge, normal, spawnPosTypes, edgeAlignment));
                }
            }
            return availableSpawnPositions;
        }

        public void Update(float deltaTime)
        {
            GlobalForceDecreaseTimer += deltaTime;
            if (GlobalForceDecreaseTimer > 1000000.0f)
            {
                GlobalForceDecreaseTimer = 0.0f;
            }

            foreach (LevelObject obj in updateableObjects)
            {
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    obj.NetworkUpdateTimer -= deltaTime;
                    if (obj.NeedsNetworkSyncing && obj.NetworkUpdateTimer <= 0.0f)
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this, new object[] { obj });
                        obj.NeedsNetworkSyncing = false;
                        obj.NetworkUpdateTimer = NetConfig.LevelObjectUpdateInterval;
                    }
                }
                if (obj.Prefab.HideWhenBroken && obj.Health <= 0.0f) { continue; }

                if (obj.Triggers != null)
                {
                    obj.ActivePrefab = obj.Prefab;
                    for (int i = 0; i < obj.Triggers.Count; i++)
                    {
                        obj.Triggers[i].Update(deltaTime);
                        if (obj.Triggers[i].IsTriggered && obj.Prefab.OverrideProperties[i] != null)
                        {
                            obj.ActivePrefab = obj.Prefab.OverrideProperties[i];
                        }
                    }
                }

                if (obj.PhysicsBody != null)
                {
                    if (obj.Prefab.PhysicsBodyTriggerIndex > -1) { obj.PhysicsBody.Enabled = obj.Triggers[obj.Prefab.PhysicsBodyTriggerIndex].IsTriggered; }
                    /*obj.Position = new Vector3(obj.PhysicsBody.Position, obj.Position.Z);
                    obj.Rotation = -obj.PhysicsBody.Rotation;*/
                }
            }

            UpdateProjSpecific(deltaTime);            
        }

        partial void UpdateProjSpecific(float deltaTime);

        private void OnObjectTriggered(LevelObject triggeredObject, LevelTrigger trigger, Entity triggerer)
        {
            if (trigger.TriggerOthersDistance <= 0.0f) { return; }
            foreach (LevelObject obj in objects)
            {
                if (obj == triggeredObject || obj.Triggers == null) { continue; }
                foreach (LevelTrigger otherTrigger in obj.Triggers)
                {
                    otherTrigger.OtherTriggered(triggeredObject, trigger);
                }
            }
        }

        private LevelObjectPrefab GetRandomPrefab(LevelGenerationParams generationParams, IList<LevelObjectPrefab> availablePrefabs)
        {
            if (availablePrefabs.Sum(p => p.GetCommonness(generationParams)) <= 0.0f) { return null; }
            return ToolBox.SelectWeightedRandom(
                availablePrefabs,
                availablePrefabs.Select(p => p.GetCommonness(generationParams)).ToList(), Rand.RandSync.ServerAndClient);
        }

        private LevelObjectPrefab GetRandomPrefab(CaveGenerationParams caveParams, IList<LevelObjectPrefab> availablePrefabs, bool requireCaveSpecificOverride)
        {
            if (availablePrefabs.Sum(p => p.GetCommonness(caveParams, requireCaveSpecificOverride)) <= 0.0f) { return null; }
            return ToolBox.SelectWeightedRandom(
                availablePrefabs,
                availablePrefabs.Select(p => p.GetCommonness(caveParams, requireCaveSpecificOverride)).ToList(), Rand.RandSync.ServerAndClient);
        }

        public override void Remove()
        {
            if (objects != null)
            {
                foreach (LevelObject obj in objects)
                {
                    obj.Remove();
                }
                objects.Clear();
                updateableObjects.Clear();
            }
            RemoveProjSpecific();

            base.Remove();
        }

        partial void RemoveProjSpecific();

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            LevelObject obj = extraData[0] as LevelObject;
            msg.WriteRangedInteger(objects.IndexOf(obj), 0, objects.Count);
            obj.ServerWrite(msg, c);
        }
    }
}
