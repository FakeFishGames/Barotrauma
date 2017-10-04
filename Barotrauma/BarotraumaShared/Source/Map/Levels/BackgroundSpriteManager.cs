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
    partial class BackgroundSprite
    {
        public readonly BackgroundSpritePrefab Prefab;
        public Vector3 Position;

        public float Scale;

        public float Rotation;

        public LevelTrigger Trigger;
        
        public BackgroundSprite(BackgroundSpritePrefab prefab, Vector3 position, float scale, float rotation = 0.0f)
        {
            this.Prefab = prefab;
            this.Position = position;

            this.Scale = scale;

            this.Rotation = rotation;

            if (prefab.LevelTriggerElement != null)
            {
                Vector2 triggerPosition = prefab.LevelTriggerElement.GetAttributeVector2("position", Vector2.Zero) * scale;

                if (rotation != 0.0f)
                {
                    var ca = (float)Math.Cos(rotation);
                    var sa = (float)Math.Sin(rotation);

                    triggerPosition = new Vector2(
                        ca * triggerPosition.X + sa * triggerPosition.Y,
                        -sa * triggerPosition.X + ca * triggerPosition.Y);
                }

                this.Trigger = new LevelTrigger(prefab.LevelTriggerElement, new Vector2(position.X, position.Y) + triggerPosition, -rotation, scale);
            }

#if CLIENT
            if (prefab.ParticleEmitterPrefabs != null)
            {
                ParticleEmitters = new List<ParticleEmitter>();
                foreach (ParticleEmitterPrefab emitterPrefab in prefab.ParticleEmitterPrefabs)
                {
                    ParticleEmitters.Add(new ParticleEmitter(emitterPrefab));
                }
            }

            if (prefab.SoundElement != null)
            {
                Sound = Sound.Load(prefab.SoundElement, true);
            }
#endif
        }

        public Vector2 LocalToWorld(Vector2 localPosition, float swingState = 0.0f)
        {
            Vector2 emitterPos = localPosition * Scale;

            if (Rotation != 0.0f || Prefab.SwingAmount != 0.0f)
            {
                float rot = Rotation + swingState * Prefab.SwingAmount;

                var ca = (float)Math.Cos(rot);
                var sa = (float)Math.Sin(rot);

                emitterPos = new Vector2(
                    ca * emitterPos.X + sa * emitterPos.Y,
                    -sa * emitterPos.X + ca * emitterPos.Y);
            }
            return new Vector2(Position.X, Position.Y) + emitterPos;
        }
    }

    partial class BackgroundSpriteManager
    {
        const int GridSize = 2000;

        private List<BackgroundSpritePrefab> prefabs = new List<BackgroundSpritePrefab>();

        private List<BackgroundSprite> sprites;
        private List<BackgroundSprite>[,] spriteGrid;
        
        private float swingTimer, swingState;

        public BackgroundSpriteManager(string configPath)
        {
            LoadConfig(configPath);
        }
        public BackgroundSpriteManager(List<string> files)
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
                    prefabs.Add(new BackgroundSpritePrefab(element));
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError(String.Format("Failed to load BackgroundSprites from {0}", configPath), e);
            }
        }


        public void PlaceSprites(Level level, int amount)
        {
            spriteGrid = new List<BackgroundSprite>[
                (int)Math.Ceiling(level.Size.X / GridSize),
                (int)Math.Ceiling((level.Size.Y - level.BottomPos) / GridSize)];

            sprites = new List<BackgroundSprite>();
            
            for (int i = 0 ; i < amount; i++)
            {
                BackgroundSpritePrefab prefab = GetRandomPrefab(level.GenerationParams.Name);
                GraphEdge selectedEdge = null;
                Vector2 edgeNormal = Vector2.One;
                Vector2? pos = FindSpritePosition(level, prefab, out selectedEdge, out edgeNormal);

                if (pos == null) continue;

                float rotation = 0.0f;
                if (prefab.AlignWithSurface)
                {
                    rotation = MathUtils.VectorToAngle(new Vector2(edgeNormal.Y, edgeNormal.X));
                }

                rotation += Rand.Range(prefab.RandomRotation.X, prefab.RandomRotation.Y, Rand.RandSync.Server);

                var newSprite = new BackgroundSprite(prefab,
                    new Vector3((Vector2)pos, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, Rand.RandSync.Server)), Rand.Range(prefab.Scale.X, prefab.Scale.Y, Rand.RandSync.Server), rotation);
                
                //calculate the positions of the corners of the rotated sprite
                Vector2 halfSize = newSprite.Prefab.Sprite.size * newSprite.Scale / 2;
                var spriteCorners = new List<Vector2>
                {
                    -halfSize, new Vector2(-halfSize.X, halfSize.Y),
                    halfSize, new Vector2(halfSize.X, -halfSize.Y)
                };

                Vector2 pivotOffset = newSprite.Prefab.Sprite.Origin * newSprite.Scale - halfSize;
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

                float minX = spriteCorners.Min(c => c.X) - newSprite.Position.Z;
                float maxX = spriteCorners.Max(c => c.X) + newSprite.Position.Z;
                
                float minY = spriteCorners.Min(c => c.Y) - newSprite.Position.Z - level.BottomPos;
                float maxY = spriteCorners.Max(c => c.Y) + newSprite.Position.Z - level.BottomPos;

#if CLIENT
                if (newSprite.ParticleEmitters != null)
                {
                    foreach (ParticleEmitter emitter in newSprite.ParticleEmitters)
                    {
                        Rectangle particleBounds = emitter.CalculateParticleBounds(pos.Value);
                        minX = Math.Min(minX, particleBounds.X);
                        maxX = Math.Max(maxX, particleBounds.Right);
                        minY = Math.Min(minY, particleBounds.Y - level.BottomPos);
                        maxY = Math.Max(maxY, particleBounds.Bottom - level.BottomPos);
                    }
                }
#endif

                sprites.Add(newSprite);

                int xStart  = (int)Math.Floor(minX / GridSize);
                int xEnd    = (int)Math.Floor(maxX / GridSize);
                if (xEnd < 0 || xStart >= spriteGrid.GetLength(0)) continue;

                int yStart  = (int)Math.Floor(minY / GridSize);
                int yEnd    = (int)Math.Floor(maxY / GridSize);
                if (yEnd < 0 || yStart >= spriteGrid.GetLength(1)) continue;

                xStart  = Math.Max(xStart, 0);
                xEnd    = Math.Min(xEnd, spriteGrid.GetLength(0) - 1);
                yStart  = Math.Max(yStart, 0);
                yEnd    = Math.Min(yEnd, spriteGrid.GetLength(1) - 1);

                for (int x = xStart; x <= xEnd; x++)
                {
                    for (int y = yStart; y <= yEnd; y++)
                    {
                        if (spriteGrid[x, y] == null) spriteGrid[x, y] = new List<BackgroundSprite>();
                        spriteGrid[x, y].Add(newSprite);
                    }
                }
            }
        }

        private Vector2? FindSpritePosition(Level level, BackgroundSpritePrefab prefab, out GraphEdge closestEdge, out Vector2 edgeNormal)
        {
            closestEdge = null;
            edgeNormal = Vector2.One;

            Vector2 randomPos = new Vector2(
                Rand.Range(0.0f, level.Size.X, Rand.RandSync.Server), 
                Rand.Range(0.0f, level.Size.Y, Rand.RandSync.Server));

            if (prefab.SpawnPos == BackgroundSpritePrefab.SpawnPosType.None) return randomPos;

            List<GraphEdge> edges = new List<GraphEdge>();
            List<Vector2> normals = new List<Vector2>();

            System.Diagnostics.Debug.Assert(level.ExtraWalls.Length == 1);
            List<VoronoiCell> cells = new List<VoronoiCell>();

            if (prefab.SpawnPos.HasFlag(BackgroundSpritePrefab.SpawnPosType.Wall)) cells.AddRange(level.GetCells(randomPos));
            if (prefab.SpawnPos.HasFlag(BackgroundSpritePrefab.SpawnPosType.SeaFloor)) cells.AddRange(level.ExtraWalls[0].Cells);
            
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
            
            if (prefab.SpawnPos.HasFlag(BackgroundSpritePrefab.SpawnPosType.RuinWall))
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
            swingTimer += deltaTime;
            swingState = (float)Math.Sin(swingTimer * 0.1f);

            foreach (BackgroundSprite sprite in sprites)
            {
                sprite.Trigger?.Update(deltaTime);
            }
            
            UpdateProjSpecific(deltaTime);            
        }

        partial void UpdateProjSpecific(float deltaTime);

        private BackgroundSpritePrefab GetRandomPrefab(string levelType)
        {
            int totalCommonness = 0;
            foreach (BackgroundSpritePrefab prefab in prefabs)
            {
                totalCommonness += prefab.GetCommonness(levelType);
            }

            float randomNumber = Rand.Int(totalCommonness+1, Rand.RandSync.Server);

            foreach (BackgroundSpritePrefab prefab in prefabs)
            {
                if (randomNumber <= prefab.GetCommonness(levelType))
                {
                    return prefab;
                }

                randomNumber -= prefab.GetCommonness(levelType);
            }

            return null;
        }
    }
}
