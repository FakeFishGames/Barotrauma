using Barotrauma.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    class BackgroundSprite
    {
        public readonly BackgroundSpritePrefab Prefab;
        public Vector3 Position;

        public float Scale;

        public float Rotation;

        public Particles.ParticleEmitter ParticleEmitter;
        
        //public Vector2[] spriteCorners;

        public BackgroundSprite(BackgroundSpritePrefab prefab, Vector3 position, float scale, float rotation = 0.0f)
        {
            this.Prefab = prefab;
            this.Position = position;

            this.Scale = scale;

            this.Rotation = rotation;

            if (prefab.ParticleEmitterPrefab != null)
            {
                this.ParticleEmitter = new ParticleEmitter(prefab.ParticleEmitterPrefab);
            }
        }
    }

    class BackgroundSpriteManager
    {
        const int GridSize = 2000;

        private List<BackgroundSpritePrefab> prefabs = new List<BackgroundSpritePrefab>();
        
        private List<BackgroundSprite>[,] sprites;

        private List<BackgroundSprite> visibleSprites = new List<BackgroundSprite>();

        private Rectangle currentGridIndices;

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
                XDocument doc = ToolBox.TryLoadXml(configPath);
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
            sprites = new List<BackgroundSprite>[
                (int)Math.Ceiling(level.Size.X / GridSize),
                (int)Math.Ceiling((level.Size.Y - level.BottomPos) / GridSize)];
            
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

                rotation += Rand.Range(prefab.RandomRotation.X, prefab.RandomRotation.Y, Rand.RandSync.ClientOnly);

                var newSprite = new BackgroundSprite(prefab,
                    new Vector3((Vector2)pos, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, Rand.RandSync.ClientOnly)), Rand.Range(prefab.Scale.X, prefab.Scale.Y, Rand.RandSync.ClientOnly), rotation);
                
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

                if (newSprite.ParticleEmitter != null)
                {
                    Rectangle particleBounds = newSprite.ParticleEmitter.CalculateParticleBounds(pos.Value);
                    spriteCorners.Add(particleBounds.Location.ToVector2());
                    spriteCorners.Add(new Vector2(particleBounds.Right, particleBounds.Bottom));
                }
                                
                int minX = (int)Math.Floor((spriteCorners.Min(c => c.X) - newSprite.Position.Z) / GridSize);
                int maxX = (int)Math.Floor((spriteCorners.Max(c => c.X) + newSprite.Position.Z) / GridSize);
                if (minX < 0 || maxX >= sprites.GetLength(0)) continue;

                int minY = (int)Math.Floor((spriteCorners.Min(c => c.Y) - newSprite.Position.Z - level.BottomPos) / GridSize);
                int maxY = (int)Math.Floor((spriteCorners.Max(c => c.Y) + newSprite.Position.Z - level.BottomPos) / GridSize);
                if (minY < 0 || maxY >= sprites.GetLength(1)) continue;

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        if (sprites[x, y] == null) sprites[x, y] = new List<BackgroundSprite>();
                        sprites[x, y].Add(newSprite);
                    }
                }
            }
        }

        private Vector2? FindSpritePosition(Level level, BackgroundSpritePrefab prefab, out GraphEdge closestEdge, out Vector2 edgeNormal)
        {
            closestEdge = null;
            edgeNormal = Vector2.One;

            Vector2 randomPos = new Vector2(
                Rand.Range(0.0f, level.Size.X, Rand.RandSync.ClientOnly), 
                Rand.Range(0.0f, level.Size.Y, Rand.RandSync.ClientOnly));

            if (prefab.SpawnPos == BackgroundSpritePrefab.SpawnPosType.None) return randomPos;

            List<GraphEdge> edges = new List<GraphEdge>();
            List<Vector2> normals = new List<Vector2>();

            System.Diagnostics.Debug.Assert(level.ExtraWalls.Length == 1);
            List<VoronoiCell> cells = new List<VoronoiCell>();

            if (prefab.SpawnPos.HasFlag(BackgroundSpritePrefab.SpawnPosType.Wall)) cells.AddRange(level.GetCells(randomPos));
            if (prefab.SpawnPos.HasFlag(BackgroundSpritePrefab.SpawnPosType.SeaFloor)) cells.AddRange(level.ExtraWalls[0].Cells);
            
            if (cells.Any())
            {
                VoronoiCell cell = cells[Rand.Int(cells.Count, Rand.RandSync.ClientOnly)];

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

            int index = Rand.Int(edges.Count, Rand.RandSync.ClientOnly);
            closestEdge = edges[index];
            edgeNormal = normals[index];

            float length = Vector2.Distance(closestEdge.point1, closestEdge.point2);
            Vector2 dir = (closestEdge.point1 - closestEdge.point2) / length;
            Vector2 pos = closestEdge.point2 + dir * Rand.Range(prefab.Sprite.size.X / 2.0f, length - prefab.Sprite.size.X / 2.0f, Rand.RandSync.ClientOnly);

            return pos;
        }

        public void Update(float deltaTime)
        {
            swingTimer += deltaTime;
            swingState = (float)Math.Sin(swingTimer * 0.1f);

            foreach (BackgroundSprite s in visibleSprites)
            {
                if (s.Prefab.ParticleEmitterPrefab != null)
                {
                    Vector2 emitterPos = new Vector2(s.Prefab.EmitterPosition.X, s.Prefab.EmitterPosition.Y) * s.Scale;

                    if (s.Rotation != 0.0f || s.Prefab.SwingAmount != 0.0f)
                    {
                        var ca = (float)Math.Cos(s.Rotation + swingState * s.Prefab.SwingAmount);
                        var sa = (float)Math.Sin(s.Rotation + swingState * s.Prefab.SwingAmount);
                    
                        emitterPos = new Vector2(
                            ca * emitterPos.X + sa * emitterPos.Y, 
                            -sa * emitterPos.X + ca * emitterPos.Y);
                    }

                    s.ParticleEmitter.Emit(deltaTime, new Vector2(s.Position.X, s.Position.Y) + emitterPos);
                }
            }
            
        }

        public void DrawSprites(SpriteBatch spriteBatch, Camera cam)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize);            
            if (indices.X >= sprites.GetLength(0)) return;
            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height - Level.Loaded.BottomPos) / (float)GridSize);
            if (indices.Y >= sprites.GetLength(1)) return;

            indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)GridSize)+1;
            if (indices.Width < 0) return;
            indices.Height = (int)Math.Floor((cam.WorldView.Y - Level.Loaded.BottomPos) / (float)GridSize)+1;
            if (indices.Height < 0) return;

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, sprites.GetLength(0)-1);
            indices.Height = Math.Min(indices.Height, sprites.GetLength(1)-1);

            float z = 0.0f;
            if (currentGridIndices != indices)
            {
                visibleSprites.Clear();

                for (int x = indices.X; x <= indices.Width; x++)
                {
                    for (int y = indices.Y; y <= indices.Height; y++)
                    {
                        if (sprites[x, y] == null) continue;
                        foreach (BackgroundSprite sprite in sprites[x, y])
                        {
                            int drawOrderIndex = 0;
                            for (int i = 0; i < visibleSprites.Count; i++)
                            {
                                if (visibleSprites[i] == sprite)
                                {
                                    drawOrderIndex = -1;
                                    break;
                                }

                                if (visibleSprites[i].Position.Z > sprite.Position.Z)
                                {
                                    break;
                                }
                                else
                                {
                                    drawOrderIndex = i + 1;
                                }
                            }

                            if (drawOrderIndex >= 0)
                            {
                                visibleSprites.Insert(drawOrderIndex, sprite);
                            }
                        }
                    }
                }

                currentGridIndices = indices;
            }

            foreach (BackgroundSprite sprite in visibleSprites)
            {              
                Vector2 camDiff = new Vector2(sprite.Position.X, sprite.Position.Y) - cam.WorldViewCenter;
                camDiff.Y = -camDiff.Y;

                sprite.Prefab.Sprite.Draw(
                    spriteBatch,
                    new Vector2(sprite.Position.X, -sprite.Position.Y) - camDiff * sprite.Position.Z / 10000.0f,
                    Color.Lerp(Color.White, Level.Loaded.BackgroundColor, sprite.Position.Z / 5000.0f),
                    sprite.Rotation + swingState * sprite.Prefab.SwingAmount,
                    sprite.Scale,
                    SpriteEffects.None,
                    z);

                /*for (int i = 0; i < 4; i++)
                {
                    GUI.DrawLine(spriteBatch,
                        new Vector2(sprite.spriteCorners[i].X, -sprite.spriteCorners[i].Y),
                        new Vector2(sprite.spriteCorners[(i + 1) % 4].X, -sprite.spriteCorners[(i + 1) % 4].Y),
                        Color.White, 0, 5);
                }*/

                if (GameMain.DebugDraw)
                {
                    GUI.DrawRectangle(spriteBatch, new Vector2(sprite.Position.X, -sprite.Position.Y), new Vector2(10.0f, 10.0f), Color.Red, true);
                }

                z += 0.0001f;
            }
        }

        private BackgroundSpritePrefab GetRandomPrefab(string levelType)
        {
            int totalCommonness = 0;
            foreach (BackgroundSpritePrefab prefab in prefabs)
            {
                totalCommonness += prefab.GetCommonness(levelType);
            }

            float randomNumber = Rand.Int(totalCommonness+1, Rand.RandSync.ClientOnly);

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
