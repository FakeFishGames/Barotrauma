using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
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

        public BackgroundSprite(BackgroundSpritePrefab prefab, Vector2 position)
        {
            this.Prefab = prefab;
            this.Position = position;
        }
    }

    class BackgroundSpriteManager
    {
        private List<BackgroundSpritePrefab> prefabs;
        private List<BackgroundSprite> sprites;

        public BackgroundSpriteManager(string configPath)
        {
            sprites = new List<BackgroundSprite>();
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
            sprites.Clear();

            for (int i = 0 ; i <amount; i++)
            {
                BackgroundSpritePrefab prefab = GetRandomPrefab();
                Vector2 pos = FindSpritePosition(level, prefab);

                sprites.Add(new BackgroundSprite(prefab, pos));
            }
        }

        private Vector2 FindSpritePosition(Level level, BackgroundSpritePrefab prefab)
        {
            Vector2 randomPos = new Vector2(Rand.Range(0.0f, level.Size.X), Rand.Range(0.0f, level.Size.Y));
            var cells = level.GetCells(randomPos);

            if (!cells.Any()) return Vector2.Zero;

            VoronoiCell cell = cells[Rand.Int(cells.Count)];
            GraphEdge bestEdge = null;
            foreach (GraphEdge edge in cell.edges)
            {
                if (prefab.Alignment.HasFlag(Alignment.Bottom))
                {
                    if (bestEdge == null || edge.Center.Y > bestEdge.Center.Y) bestEdge = edge;
                }
                else if (prefab.Alignment.HasFlag(Alignment.Top))
                {
                    if (bestEdge == null || edge.Center.Y < bestEdge.Center.Y) bestEdge = edge;
                }
                else if (prefab.Alignment.HasFlag(Alignment.Left))
                {
                    if (bestEdge == null || edge.Center.X > bestEdge.Center.X) bestEdge = edge;
                }
                else if (prefab.Alignment.HasFlag(Alignment.Right))
                {
                    if (bestEdge == null || edge.Center.X < bestEdge.Center.X) bestEdge = edge;
                }
            }

            Vector2 dir = Vector2.Normalize(bestEdge.point1 - bestEdge.point2);
            Vector2 pos = bestEdge.Center;

            if (prefab.Alignment.HasFlag(Alignment.Bottom))
            {
                pos.Y -= Math.Abs(dir.Y) * prefab.Sprite.size.X/Math.Abs(dir.X);
            }
            else if (prefab.Alignment.HasFlag(Alignment.Top))
            {
                pos.Y += Math.Abs(dir.Y) * prefab.Sprite.size.X/Math.Abs(dir.X);
            }

            return pos;
        }

        public void DrawSprites(SpriteBatch spriteBatch)
        {
            foreach (BackgroundSprite sprite in sprites)
            {
                sprite.Prefab.Sprite.Draw(spriteBatch, new Vector2(sprite.Position.X, -sprite.Position.Y));
            }
        }

        private BackgroundSpritePrefab GetRandomPrefab()
        {
            int totalCommonness = 0;
            foreach (BackgroundSpritePrefab prefab in prefabs)
            {
                totalCommonness += prefab.Commonness;
            }

            float randomNumber = Rand.Int(totalCommonness+1);

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
