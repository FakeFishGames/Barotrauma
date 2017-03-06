using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

        public BackgroundSprite(BackgroundSpritePrefab prefab, Vector3 position, float scale, float rotation = 0.0f)
        {
            this.Prefab = prefab;
            this.Position = position;

            this.Scale = scale;

            this.Rotation = rotation;
        }
    }

    class BackgroundSpriteManager
    {
        const int GridSize = 1000;

        private List<BackgroundSpritePrefab> prefabs = new List<BackgroundSpritePrefab>();
        
        private List<BackgroundSprite>[,] sprites;

        private float swingTimer;

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
                (int)Math.Ceiling(level.Size.Y / GridSize)];

            for (int x = 0; x < sprites.GetLength(0); x++)
            {
                for (int y = 0; y < sprites.GetLength(1); y++)
                {
                    sprites[x, y] = new List<BackgroundSprite>();
                }
            }

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

                rotation += Rand.Range(prefab.RandomRotation.X, prefab.RandomRotation.Y, false);

                var newSprite = new BackgroundSprite(prefab,
                    new Vector3((Vector2)pos, Rand.Range(prefab.DepthRange.X, prefab.DepthRange.Y, false)), Rand.Range(prefab.Scale.X, prefab.Scale.Y, false), rotation);

                Vector2 spriteSize = newSprite.Prefab.Sprite.size * newSprite.Scale;

                int minX = (int)Math.Floor((newSprite.Position.X - spriteSize.X / 2 - newSprite.Position.Z) / GridSize);
                int maxX = (int)Math.Floor((newSprite.Position.X + spriteSize.X / 2 + newSprite.Position.Z) / GridSize);
                if (minX < 0 || maxX >= sprites.GetLength(0)) continue;

                int minY = (int)Math.Floor((newSprite.Position.Y - spriteSize.Y / 2 - newSprite.Position.Z) / GridSize);
                int maxY = (int)Math.Floor((newSprite.Position.Y + spriteSize.Y / 2 + newSprite.Position.Z) / GridSize);
                if (minY < 0 || maxY >= sprites.GetLength(1)) continue;

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
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
                Rand.Range(0.0f, level.Size.X, false), 
                Rand.Range(0.0f, level.Size.Y, false));

            if (!prefab.SpawnOnWalls) return randomPos;

            var cells = level.GetCells(randomPos);

            if (!cells.Any()) return null;

            VoronoiCell cell = cells[Rand.Int(cells.Count, false)];
            List<GraphEdge> edges = new List<GraphEdge>();
            List<Vector2> normals = new List<Vector2>();
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

            if (!edges.Any()) return null;

            int index = Rand.Int(edges.Count,false);
            closestEdge = edges[index];
            edgeNormal = normals[index];

            float length = Vector2.Distance(closestEdge.point1, closestEdge.point2);
            Vector2 dir = (closestEdge.point1 - closestEdge.point2) / length;
            Vector2 pos = closestEdge.point2 + dir * Rand.Range(prefab.Sprite.size.X / 2.0f, length - prefab.Sprite.size.X / 2.0f, false);
            
            return pos;
        }

        public void Update(float deltaTime)
        {
            swingTimer += deltaTime;
        }

        public void DrawSprites(SpriteBatch spriteBatch, Camera cam)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize);            
            if (indices.X >= sprites.GetLength(0)) return;
            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height) / (float)GridSize);
            if (indices.Y >= sprites.GetLength(1)) return;

            indices.Width = (int)Math.Floor(cam.WorldView.Right / (float)GridSize)+1;
            if (indices.Width < 0) return;
            indices.Height = (int)Math.Floor(cam.WorldView.Y / (float)GridSize)+1;
            if (indices.Height < 0) return;

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, sprites.GetLength(0)-1);
            indices.Height = Math.Min(indices.Height, sprites.GetLength(1)-1);

            float swingState = (float)Math.Sin(swingTimer * 0.1f);

            List<BackgroundSprite> visibleSprites = new List<BackgroundSprite>();

            float z = 0.0f;
            for (int x = indices.X; x <= indices.Width; x++)
            {
                for (int y = indices.Y; y <= indices.Height; y++)
                {
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

            float randomNumber = Rand.Int(totalCommonness+1, false);

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
