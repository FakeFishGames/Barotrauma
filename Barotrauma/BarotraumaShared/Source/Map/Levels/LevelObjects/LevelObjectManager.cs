#if CLIENT
using Barotrauma.Particles;
#endif
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class LevelObjectManager
    {
        const int GridSize = 2000;

        private List<LevelObjectPrefab> prefabs = new List<LevelObjectPrefab>();

        private List<LevelObject> objects;
        private List<LevelObject>[,] objectGrid;
        
        public LevelObjectManager(string configPath)
        {
            LoadConfig(configPath);
        }
        public LevelObjectManager(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                LoadConfig(file);
            }
        }
        private void LoadConfig(string configPath)
        {
            try
            {
                XDocument doc = XMLExtensions.TryLoadXml(configPath);
                if (doc == null || doc.Root == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    prefabs.Add(new LevelObjectPrefab(element));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(String.Format("Failed to load LevelObject prefabs from {0}", configPath), e);
            }
        }


        public void PlaceObjects(Level level, int amount)
        {
            objectGrid = new List<LevelObject>[
                (int)Math.Ceiling(level.Size.X / GridSize),
                (int)Math.Ceiling((level.Size.Y - level.BottomPos) / GridSize)];

            objects = new List<LevelObject>();            
            for (int i = 0 ; i < amount; i++)
            {
                LevelObjectPrefab prefab = GetRandomPrefab(level.GenerationParams.Name);
                Vector2 edgeNormal = Vector2.One;
                Vector2? pos = FindObjectPosition(level, prefab, out GraphEdge selectedEdge, out edgeNormal);

                if (pos == null) continue;

                float rotation = 0.0f;
                if (prefab.AlignWithSurface)
                {
                    rotation = MathUtils.VectorToAngle(new Vector2(edgeNormal.Y, edgeNormal.X));
                }

                rotation += Rand.Range(prefab.RandomRotation.X, prefab.RandomRotation.Y, Rand.RandSync.Server);

                var newObject = new LevelObject(prefab,
                    new Vector3((Vector2)pos, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, Rand.RandSync.Server)), Rand.Range(prefab.Scale.X, prefab.Scale.Y, Rand.RandSync.Server), rotation);
                foreach (LevelTrigger trigger in newObject.Triggers)
                {
                    trigger.OnTriggered += (levelTrigger, obj) =>
                    {
                        OnObjectTriggered(newObject, levelTrigger, obj);
                    };
                }

                //calculate the positions of the corners of the rotated sprite
                Vector2 halfSize = newObject.Prefab.Sprite.size * newObject.Scale / 2;
                var spriteCorners = new List<Vector2>
                {
                    -halfSize, new Vector2(-halfSize.X, halfSize.Y),
                    halfSize, new Vector2(halfSize.X, -halfSize.Y)
                };

                Vector2 pivotOffset = newObject.Prefab.Sprite.Origin * newObject.Scale - halfSize;
                pivotOffset.X = -pivotOffset.X;
                pivotOffset = new Vector2(
                    (float)(pivotOffset.X * Math.Cos(-rotation) - pivotOffset.Y * Math.Sin(-rotation)),
                    (float)(pivotOffset.X * Math.Sin(-rotation) + pivotOffset.Y * Math.Cos(-rotation)));                

                for (int j = 0; j < 4; j++)
                {
                    spriteCorners[j] = new Vector2(
                        (float)(spriteCorners[j].X * Math.Cos(-rotation) - spriteCorners[j].Y * Math.Sin(-rotation)),
                        (float)(spriteCorners[j].X * Math.Sin(-rotation) + spriteCorners[j].Y * Math.Cos(-rotation)));

                    spriteCorners[j] += pos.Value + pivotOffset;
                }

                float minX = spriteCorners.Min(c => c.X) - newObject.Position.Z;
                float maxX = spriteCorners.Max(c => c.X) + newObject.Position.Z;
                
                float minY = spriteCorners.Min(c => c.Y) - newObject.Position.Z - level.BottomPos;
                float maxY = spriteCorners.Max(c => c.Y) + newObject.Position.Z - level.BottomPos;

#if CLIENT
                if (newObject.ParticleEmitters != null)
                {
                    foreach (ParticleEmitter emitter in newObject.ParticleEmitters)
                    {
                        Rectangle particleBounds = emitter.CalculateParticleBounds(pos.Value);
                        minX = Math.Min(minX, particleBounds.X);
                        maxX = Math.Max(maxX, particleBounds.Right);
                        minY = Math.Min(minY, particleBounds.Y - level.BottomPos);
                        maxY = Math.Max(maxY, particleBounds.Bottom - level.BottomPos);
                    }
                }
#endif

                objects.Add(newObject);

                int xStart  = (int)Math.Floor(minX / GridSize);
                int xEnd    = (int)Math.Floor(maxX / GridSize);
                if (xEnd < 0 || xStart >= objectGrid.GetLength(0)) continue;

                int yStart  = (int)Math.Floor(minY / GridSize);
                int yEnd    = (int)Math.Floor(maxY / GridSize);
                if (yEnd < 0 || yStart >= objectGrid.GetLength(1)) continue;

                xStart  = Math.Max(xStart, 0);
                xEnd    = Math.Min(xEnd, objectGrid.GetLength(0) - 1);
                yStart  = Math.Max(yStart, 0);
                yEnd    = Math.Min(yEnd, objectGrid.GetLength(1) - 1);

                for (int x = xStart; x <= xEnd; x++)
                {
                    for (int y = yStart; y <= yEnd; y++)
                    {
                        if (objectGrid[x, y] == null) objectGrid[x, y] = new List<LevelObject>();
                        objectGrid[x, y].Add(newObject);
                    }
                }
            }
        }

        private Vector2? FindObjectPosition(Level level, LevelObjectPrefab prefab, out GraphEdge closestEdge, out Vector2 edgeNormal)
        {
            closestEdge = null;
            edgeNormal = Vector2.One;

            Vector2 randomPos = new Vector2(
                Rand.Range(0.0f, level.Size.X, Rand.RandSync.Server), 
                Rand.Range(0.0f, level.Size.Y, Rand.RandSync.Server));

            if (prefab.SpawnPos == LevelObjectPrefab.SpawnPosType.None) return randomPos;

            List<GraphEdge> edges = new List<GraphEdge>();
            List<Vector2> normals = new List<Vector2>();

            System.Diagnostics.Debug.Assert(level.ExtraWalls.Length == 1);
            List<VoronoiCell> cells = new List<VoronoiCell>();

            if (prefab.SpawnPos.HasFlag(LevelObjectPrefab.SpawnPosType.Wall)) cells.AddRange(level.GetCells(randomPos));
            if (prefab.SpawnPos.HasFlag(LevelObjectPrefab.SpawnPosType.SeaFloor)) cells.AddRange(level.ExtraWalls[0].Cells);
            
            if (cells.Any())
            {
                VoronoiCell cell = cells[Rand.Int(cells.Count, Rand.RandSync.Server)];

                foreach (GraphEdge edge in cell.edges)
                {
                    if (!edge.isSolid || edge.OutsideLevel) continue;

                    Vector2 normal = edge.GetNormal(cell);
                
                    if (prefab.Alignment.HasFlag(Alignment.Bottom) && normal.Y < -0.5f)
                    {
                        edges.Add(edge);
                    }
                    else if (prefab.Alignment.HasFlag(Alignment.Top) && normal.Y > 0.5f)
                    {
                        edges.Add(edge);
                    }
                    else if (prefab.Alignment.HasFlag(Alignment.Left) && normal.X < -0.5f)
                    {
                        edges.Add(edge);
                    }
                    else if (prefab.Alignment.HasFlag(Alignment.Right) && normal.X > 0.5f)
                    {
                        edges.Add(edge);
                    }
                    else
                    {
                        continue;
                    }

                    normals.Add(normal);
                }
            }
            
            if (prefab.SpawnPos.HasFlag(LevelObjectPrefab.SpawnPosType.RuinWall))
            {
                foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
                {
                    Rectangle expandedArea = ruin.Area;
                    expandedArea.Inflate(ruin.Area.Width, ruin.Area.Height);
                    if (!expandedArea.Contains(randomPos)) continue;

                    foreach (var ruinShape in ruin.RuinShapes)
                    {
                        foreach (var wall in ruinShape.Walls)
                        {
                            if (!prefab.Alignment.HasFlag(ruinShape.GetLineAlignment(wall))) continue;

                            edges.Add(new GraphEdge(wall.A, wall.B));
                            normals.Add((wall.A + wall.B) / 2.0f - ruinShape.Center);
                        }
                    }
                }
            }

            if (!edges.Any()) return null;

            int index = Rand.Int(edges.Count, Rand.RandSync.Server);
            closestEdge = edges[index];
            edgeNormal = normals[index];

            float length = Vector2.Distance(closestEdge.point1, closestEdge.point2);
            Vector2 dir = (closestEdge.point1 - closestEdge.point2) / length;
            Vector2 pos = closestEdge.point2 + dir * Rand.Range(prefab.Sprite.size.X / 2.0f, length - prefab.Sprite.size.X / 2.0f, Rand.RandSync.Server);

            return pos;
        }

        public void Update(float deltaTime)
        {
            foreach (LevelObject obj in objects)
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

                if (obj.ActivePrefab.SonarDisruption > 0.0f)
                {
                    Level.Loaded?.SetSonarDisruptionStrength(new Vector2(obj.Position.X, obj.Position.Y), obj.ActivePrefab.SonarDisruption);
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
            return ToolBox.SelectWeightedRandom(prefabs, prefabs.Select(p => p.GetCommonness(levelType)).ToList(), Rand.RandSync.Server);
        }
    }
}
