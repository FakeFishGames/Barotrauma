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
        public Vector2 Position;

        public float Scale;

        public float Rotation;

        public BackgroundSprite(BackgroundSpritePrefab prefab, Vector2 position, float scale, float rotation = 0.0f)
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

        private List<BackgroundSpritePrefab> prefabs;
        //private List<BackgroundSprite> sprites;

        private List<BackgroundSprite>[,] sprites;

        public BackgroundSpriteManager(string configPath)
        {
            //sprites = new List<BackgroundSprite>[2,2]();
            prefabs = new List<BackgroundSpritePrefab>();

            XDocument doc = ToolBox.TryLoadXml(configPath);
            if (doc == null || doc.Root == null) return;

            foreach (XElement element in doc.Root.Elements())
            {
                prefabs.Add(new BackgroundSpritePrefab(element));
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
                BackgroundSpritePrefab prefab = GetRandomPrefab();
                GraphEdge selectedEdge = null;
                Vector2? pos = FindSpritePosition(level, prefab, out selectedEdge);

                if (pos == null) continue;

                float rotation = 0.0f;
                if (prefab.AlignWithSurface)
                {
                    Vector2 leftPoint = selectedEdge.point2;
                    Vector2 rightPoint = selectedEdge.point1;

                    //if (leftPoint.X > rightPoint.X)
                    //{
                    //    leftPoint = selectedEdge.point2;
                    //    rightPoint = selectedEdge.point1;
                    //}

                    rotation = -MathUtils.VectorToAngle(rightPoint - leftPoint);
                }

                rotation += Rand.Range(prefab.RandomRotation.X, prefab.RandomRotation.Y, false);

                var newSprite = new BackgroundSprite(prefab, (Vector2)pos, Rand.Range(prefab.Scale.X, prefab.Scale.Y), rotation);

                int n = 0;

                int x = (int)Math.Floor(((Vector2)pos).X / GridSize);
                if (x<0 || x >= sprites.GetLength(0)) continue;
                int y = (int)Math.Floor(((Vector2)pos).Y / GridSize);
                if (y<0 || 1 >= sprites.GetLength(1)) continue;

                sprites[x,y].Add(newSprite);
                
                //while (n < sprites.Count)
                //{
                //    n++;

                //    Sprite existingSprite = sprites[n - 1].Prefab.Sprite;
                //    if (existingSprite == null) continue;
                //    if (existingSprite.Texture == newSprite.Prefab.Sprite.Texture) break;
                //}

                //sprites.Insert(n, newSprite);
            }
        }

        private Vector2? FindSpritePosition(Level level, BackgroundSpritePrefab prefab, out GraphEdge closestEdge)
        {
            closestEdge = null;

            Vector2 randomPos = new Vector2(Rand.Range(0.0f, level.Size.X, false), Rand.Range(0.0f, level.Size.Y, false));
            var cells = level.GetCells(randomPos);

            if (!cells.Any()) return null;

            VoronoiCell cell = cells[Rand.Int(cells.Count, false)];
            List<GraphEdge> edges = new List<GraphEdge>();
            foreach (GraphEdge edge in cell.edges)
            {
                if (!edge.isSolid) continue;
                
                if (prefab.Alignment.HasFlag(Alignment.Bottom))
                {
                    if (Math.Abs(edge.point1.X - edge.point2.X) < Math.Abs(edge.point1.Y - edge.point2.Y)) continue;
                    if (edge.Center.Y < cell.Center.Y) edges.Add(edge);
                }
                else if (prefab.Alignment.HasFlag(Alignment.Top))
                {
                    if (Math.Abs(edge.point1.X - edge.point2.X) < Math.Abs(edge.point1.Y - edge.point2.Y)) continue;
                    if (edge.Center.Y > cell.Center.Y) edges.Add(edge);
                }
                else if (prefab.Alignment.HasFlag(Alignment.Left))
                {
                    if (edge.Center.X < cell.Center.X) edges.Add(edge);
                }
                else if (prefab.Alignment.HasFlag(Alignment.Right))
                {
                    if (edge.Center.X > cell.Center.X) edges.Add(edge);
                }
                else
                {
                    edges.Add(edge);
                }
            }

            if (!edges.Any()) return null;

            closestEdge = edges[Rand.Int(edges.Count,false)];

            float length = Vector2.Distance(closestEdge.point1, closestEdge.point2);
            Vector2 dir = (closestEdge.point1 - closestEdge.point2) / length;
            Vector2 pos = closestEdge.Center;

            pos = closestEdge.point2 + dir * Rand.Range(prefab.Sprite.size.X / 2.0f, length - prefab.Sprite.size.X / 2.0f);

            if (prefab.Alignment.HasFlag(Alignment.Top))
            {
                pos.Y -= Math.Abs(dir.Y) * prefab.Sprite.size.X / Math.Abs(dir.X);
            }
            else if (prefab.Alignment.HasFlag(Alignment.Bottom))
            {
                pos.Y += Math.Abs(dir.Y) * prefab.Sprite.size.X / Math.Abs(dir.X);
            }

            return pos;
        }

        public void DrawSprites(SpriteBatch spriteBatch, Camera cam)
        {
            Rectangle indices = Rectangle.Empty;
            indices.X = (int)Math.Floor(cam.WorldView.X / (float)GridSize) - 1;            
            if (indices.X >= sprites.GetLength(0)) return;

            indices.Y = (int)Math.Floor((cam.WorldView.Y - cam.WorldView.Height) / (float)GridSize) - 1;
            if (indices.Y >= sprites.GetLength(1)) return;

            indices.Width = (int)Math.Ceiling(cam.WorldView.Right / (float)GridSize) + 1;
            if (indices.Width < 0) return;
            indices.Height = (int)Math.Ceiling(cam.WorldView.Y / (float)GridSize) + 1;
            if (indices.Height < 0) return;

            indices.X = Math.Max(indices.X, 0);
            indices.Y = Math.Max(indices.Y, 0);
            indices.Width = Math.Min(indices.Width, sprites.GetLength(0));
            indices.Height = Math.Min(indices.Height, sprites.GetLength(1));

            float z = 0.0f;
            for (int x = indices.X; x < indices.Width; x++)
            {
                for (int y = indices.Y; y < indices.Height; y++)
                {
                    foreach (BackgroundSprite sprite in sprites[x, y])
                    {
                        sprite.Prefab.Sprite.Draw(spriteBatch, new Vector2(sprite.Position.X, -sprite.Position.Y), Color.White, sprite.Rotation, sprite.Scale, SpriteEffects.None, z);
                        z += 0.0001f;
                    }
                }
            }
        }

        private BackgroundSpritePrefab GetRandomPrefab()
        {
            int totalCommonness = 0;
            foreach (BackgroundSpritePrefab prefab in prefabs)
            {
                totalCommonness += prefab.Commonness;
            }

            float randomNumber = Rand.Int(totalCommonness+1, false);

            foreach (BackgroundSpritePrefab prefab in prefabs)
            {
                if (randomNumber <= prefab.Commonness)
                {
                    return prefab;
                }

                randomNumber -= prefab.Commonness;
            }

            return null;
        }
    }
}
