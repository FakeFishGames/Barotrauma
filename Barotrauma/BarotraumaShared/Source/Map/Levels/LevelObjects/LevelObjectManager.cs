#if CLIENT
using Barotrauma.Particles;
#endif
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class LevelObjectManager : Entity, IServerSerializable
    {
        const int GridSize = 2000;

        private List<LevelObject> objects;
        private List<LevelObject>[,] objectGrid;

        public LevelObjectManager() : base(null)
        {
        }

        class SpawnPosition
        {
            public readonly GraphEdge GraphEdge;
            public readonly Vector2 Normal;
            public readonly LevelObjectPrefab.SpawnPosType SpawnPosType;
            public readonly Alignment Alignment;
            public readonly float Length;

            public SpawnPosition(GraphEdge graphEdge, Vector2 normal, LevelObjectPrefab.SpawnPosType spawnPosType, Alignment alignment)
            {
                GraphEdge = graphEdge;
                Normal = normal;
                SpawnPosType = spawnPosType;
                Alignment = alignment;
                
                Length = Vector2.Distance(graphEdge.Point1, graphEdge.Point2);
            }

            public float GetSpawnProbability(LevelObjectPrefab prefab)
            {
                if (prefab.ClusteringAmount <= 0.0f) return Length;

                float noise = (float)(
                    PerlinNoise.CalculatePerlin(GraphEdge.Point1.X / 10000.0f, GraphEdge.Point1.Y / 10000.0f, prefab.ClusteringGroup) +
                    PerlinNoise.CalculatePerlin(GraphEdge.Point1.X / 20000.0f, GraphEdge.Point1.Y / 20000.0f, prefab.ClusteringGroup));

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
            
            foreach (RuinGeneration.Ruin ruin in level.Ruins)
            {
                foreach (var ruinShape in ruin.RuinShapes)
                {
                    foreach (var wall in ruinShape.Walls)
                    {
                        availableSpawnPositions.Add(new SpawnPosition(
                            new GraphEdge(wall.A, wall.B),
                            (wall.A + wall.B) / 2.0f - ruinShape.Center,
                            LevelObjectPrefab.SpawnPosType.RuinWall,
                            ruinShape.GetLineAlignment(wall)));
                    }
                }            
            }

            foreach (var posOfInterest in level.PositionsOfInterest)
            {
                if (posOfInterest.PositionType != Level.PositionType.MainPath) continue;

                availableSpawnPositions.Add(new SpawnPosition(
                    new GraphEdge(posOfInterest.Position.ToVector2(), posOfInterest.Position.ToVector2() + Vector2.UnitX), 
                    Vector2.UnitY, 
                    LevelObjectPrefab.SpawnPosType.MainPath, 
                    Alignment.Top));
            }

            objects = new List<LevelObject>();
            for (int i = 0; i < amount; i++)
            {
                //get a random prefab and find a place to spawn it
                LevelObjectPrefab prefab = GetRandomPrefab(level.GenerationParams.Name);
                
                SpawnPosition spawnPosition = FindObjectPosition(availableSpawnPositions, level, prefab);

                if (spawnPosition == null && prefab.SpawnPos != LevelObjectPrefab.SpawnPosType.None) continue;

                float rotation = 0.0f;
                if (prefab.AlignWithSurface && spawnPosition != null)
                {
                    rotation = MathUtils.VectorToAngle(new Vector2(spawnPosition.Normal.Y, spawnPosition.Normal.X));
                }
                rotation += Rand.Range(prefab.RandomRotationRad.X, prefab.RandomRotationRad.Y, Rand.RandSync.Server);

                Vector2 position = Vector2.Zero;
                Vector2 edgeDir = Vector2.UnitX;
                if (spawnPosition == null)
                {
                    position = new Vector2(
                        Rand.Range(0.0f, level.Size.X, Rand.RandSync.Server),
                        Rand.Range(0.0f, level.Size.Y, Rand.RandSync.Server));
                }
                else
                {
                    edgeDir = (spawnPosition.GraphEdge.Point1 - spawnPosition.GraphEdge.Point2) / spawnPosition.Length;
                    position = spawnPosition.GraphEdge.Point2 + edgeDir * Rand.Range(prefab.MinSurfaceWidth / 2.0f, spawnPosition.Length - prefab.MinSurfaceWidth / 2.0f, Rand.RandSync.Server);
                }

                var newObject = new LevelObject(prefab,
                    new Vector3(position, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, Rand.RandSync.Server)), Rand.Range(prefab.MinSize, prefab.MaxSize, Rand.RandSync.Server), rotation);
                AddObject(newObject, level);

                foreach (LevelObjectPrefab.ChildObject child in prefab.ChildObjects)
                {
                    int childCount = Rand.Range(child.MinCount, child.MaxCount, Rand.RandSync.Server);
                    for (int j = 0; j < childCount; j++)
                    {
                        var matchingPrefabs = LevelObjectPrefab.List.Where(p => child.AllowedNames.Contains(p.Name));
                        int prefabCount = matchingPrefabs.Count();
                        var childPrefab = prefabCount == 0 ? null : matchingPrefabs.ElementAt(Rand.Range(0, prefabCount, Rand.RandSync.Server));
                        if (childPrefab == null) continue;

                        Vector2 childPos = position + edgeDir * Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server) * prefab.MinSurfaceWidth;

                        var childObject = new LevelObject(childPrefab,
                            new Vector3(childPos, Rand.Range(childPrefab.DepthRange.X, childPrefab.DepthRange.Y, Rand.RandSync.Server)),
                            Rand.Range(childPrefab.MinSize, childPrefab.MaxSize, Rand.RandSync.Server),
                            rotation + Rand.Range(childPrefab.RandomRotationRad.X, childPrefab.RandomRotationRad.Y, Rand.RandSync.Server));

                        AddObject(childObject, level);
                    }
                }

            }
                
        }

        private void AddObject(LevelObject newObject, Level level)
        {
            foreach (LevelTrigger trigger in newObject.Triggers)
            {
                trigger.OnTriggered += (levelTrigger, obj) =>
                {
                    OnObjectTriggered(newObject, levelTrigger, obj);
                };
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

            foreach (LevelTrigger trigger in newObject.Triggers)
            {
                if (trigger.PhysicsBody == null) continue;
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

        private readonly static List<LevelObject> objectsInRange = new List<LevelObject>();
        public IEnumerable<LevelObject> GetAllObjects(Vector2 worldPosition, float radius)
        {
            var minIndices = GetGridIndices(worldPosition - Vector2.One * radius);
            if (minIndices.X >= objectGrid.GetLength(0) || minIndices.Y >= objectGrid.GetLength(1)) return Enumerable.Empty<LevelObject>();

            var maxIndices = GetGridIndices(worldPosition + Vector2.One * radius);
            if (maxIndices.X < 0 || maxIndices.Y < 0) return Enumerable.Empty<LevelObject>();

            minIndices.X = Math.Max(0, minIndices.X);
            minIndices.Y = Math.Max(0, minIndices.Y);
            maxIndices.X = Math.Min(objectGrid.GetLength(0) - 1, maxIndices.X);
            maxIndices.Y = Math.Min(objectGrid.GetLength(1) - 1, maxIndices.Y);

            objectsInRange.Clear();
            for (int x = minIndices.X; x <= maxIndices.X; x++)
            {
                for (int y = minIndices.Y; y <= maxIndices.Y; y++)
                {
                    if (objectGrid[x, y] == null) continue;
                    foreach (LevelObject obj in objectGrid[x, y])
                    {
                        if (!objectsInRange.Contains(obj)) objectsInRange.Add(obj);
                    }
                }
            }

            return objectsInRange;
        }

        private List<SpawnPosition> GetAvailableSpawnPositions(IEnumerable<VoronoiCell> cells, LevelObjectPrefab.SpawnPosType spawnPosType)
        {
            List<SpawnPosition> availableSpawnPositions = new List<SpawnPosition>();
            foreach (var cell in cells)
            {
                foreach (var edge in cell.Edges)
                {
                    if (!edge.IsSolid || edge.OutsideLevel) continue;
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

                    availableSpawnPositions.Add(new SpawnPosition(edge, normal, spawnPosType, edgeAlignment));
                }
            }
            return availableSpawnPositions;
        }

        private SpawnPosition FindObjectPosition(List<SpawnPosition> availableSpawnPositions, Level level, LevelObjectPrefab prefab)
        {
            if (prefab.SpawnPos == LevelObjectPrefab.SpawnPosType.None) return null;

            var suitableSpawnPositions = availableSpawnPositions.Where(sp => 
                prefab.SpawnPos.HasFlag(sp.SpawnPosType) && sp.Length >= prefab.MinSurfaceWidth && prefab.Alignment.HasFlag(sp.Alignment)).ToList();

            return ToolBox.SelectWeightedRandom(suitableSpawnPositions, suitableSpawnPositions.Select(sp => sp.GetSpawnProbability(prefab)).ToList(), Rand.RandSync.Server);
        }
        
        public void Update(float deltaTime)
        {
            foreach (LevelObject obj in objects)
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

                obj.ActivePrefab = obj.Prefab;
                for (int i = 0; i < obj.Triggers.Count; i++)
                {
                    obj.Triggers[i].Update(deltaTime);
                    if (obj.Triggers[i].IsTriggered && obj.Prefab.OverrideProperties[i] != null)
                    {
                        obj.ActivePrefab = obj.Prefab.OverrideProperties[i];
                    }
                }

                if (obj.PhysicsBody != null)
                {
                    if (obj.Prefab.PhysicsBodyTriggerIndex > -1) obj.PhysicsBody.Enabled = obj.Triggers[obj.Prefab.PhysicsBodyTriggerIndex].IsTriggered;
                    obj.Position = new Vector3(obj.PhysicsBody.Position, obj.Position.Z);
                    obj.Rotation = obj.PhysicsBody.Rotation;
                }
            }

            UpdateProjSpecific(deltaTime);            
        }

        partial void UpdateProjSpecific(float deltaTime);

        private void OnObjectTriggered(LevelObject triggeredObject, LevelTrigger trigger, Entity triggerer)
        {
            if (trigger.TriggerOthersDistance <= 0.0f) return;
            foreach (LevelObject obj in objects)
            {
                if (obj == triggeredObject) continue;
                foreach (LevelTrigger otherTrigger in obj.Triggers)
                {
                    otherTrigger.OtherTriggered(triggeredObject, trigger);
                }
            }
        }

        private LevelObjectPrefab GetRandomPrefab(string levelType)
        {
            return ToolBox.SelectWeightedRandom(
                LevelObjectPrefab.List, 
                LevelObjectPrefab.List.Select(p => p.GetCommonness(levelType)).ToList(), Rand.RandSync.Server);
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
            }
            RemoveProjSpecific();

            base.Remove();
        }

        partial void RemoveProjSpecific();

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            LevelObject obj = extraData[0] as LevelObject;
            msg.WriteRangedIntegerDeprecated(0, objects.Count, objects.IndexOf(obj));
            obj.ServerWrite(msg, c);
        }
    }
}
