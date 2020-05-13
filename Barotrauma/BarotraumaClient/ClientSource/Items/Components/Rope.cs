using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent, IDrawableComponent
    {
        private Sprite sprite, startSprite, endSprite;

        [Serialize(5, false)]
        public int SpriteWidth
        {
            get;
            set;
        }

        [Serialize("255,255,255,255", false)]
        public Color SpriteColor
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool Tile
        {
            get;
            set;
        }

        public Vector2 DrawSize
        {
            get 
            {
                if (target == null || source == null) { return Vector2.Zero; }
                return new Vector2(
                    Math.Abs(target.DrawPosition.X - source.DrawPosition.X),
                    Math.Abs(target.DrawPosition.Y - source.DrawPosition.Y)) * 1.5f;
            }
        }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        sprite = new Sprite(subElement);
                        break;
                    case "startsprite":
                        startSprite = new Sprite(subElement);
                        break;
                    case "endsprite":
                        endSprite = new Sprite(subElement);
                        break;
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (target == null) { return; }

            Vector2 startPos = new Vector2(source.DrawPosition.X, -source.DrawPosition.Y);
            var turret = source?.GetComponent<Turret>();
            if (turret != null)
            {
                startPos = new Vector2(source.WorldRect.X + turret.TransformedBarrelPos.X, -(source.WorldRect.Y - turret.TransformedBarrelPos.Y));
                if (turret.BarrelSprite != null)
                {
                    startPos += new Vector2((float)Math.Cos(turret.Rotation), (float)Math.Sin(turret.Rotation)) * turret.BarrelSprite.size.Y * turret.BarrelSprite.RelativeOrigin.Y * item.Scale * 0.9f;
                }
            }
            Vector2 endPos = new Vector2(target.DrawPosition.X, -target.DrawPosition.Y);

            if (Snapped)
            {
                float snapState = 1.0f - snapTimer / SnapAnimDuration;
                Vector2 diff = target.DrawPosition - source.DrawPosition;
                diff.Y = -diff.Y;

                int width = (int)(SpriteWidth * snapState);
                if (width > 0.0f) 
                { 
                    DrawRope(spriteBatch, endPos - diff * snapState * 0.5f, endPos, width);
                    DrawRope(spriteBatch, startPos, startPos + diff * snapState * 0.5f, width);
                }
            }
            else
            {
                DrawRope(spriteBatch, startPos, endPos, SpriteWidth);
            }

            if (startSprite != null || endSprite != null)
            {
                Vector2 dir = endPos - startPos;
                float angle = (float)Math.Atan2(dir.Y, dir.X);
                if (startSprite != null)
                {
                    float depth = Math.Min(item.GetDrawDepth() + (startSprite.Depth - item.Sprite.Depth), 0.999f);
                    startSprite?.Draw(spriteBatch, startPos, SpriteColor, angle, depth: depth);
                }
                if (endSprite != null)
                {
                    float depth = Math.Min(item.GetDrawDepth() + (endSprite.Depth - item.Sprite.Depth), 0.999f);
                    endSprite?.Draw(spriteBatch, endPos, SpriteColor, angle, depth: depth);
                }
            }
        }

        private void DrawRope(SpriteBatch spriteBatch, Vector2 startPos, Vector2 endPos, int width)
        {
            float depth = sprite == null ?
                item.Sprite.Depth + 0.001f :
                Math.Min(item.GetDrawDepth() + (sprite.Depth - item.Sprite.Depth), 0.999f);
            
            if (sprite?.Texture == null)
            {
                GUI.DrawLine(spriteBatch,
                    startPos,
                    endPos,
                    SpriteColor, depth: depth, width: width);
                return;
            }

            if (Tile)
            {
                float length = Vector2.Distance(startPos, endPos);
                Vector2 dir = (endPos - startPos) / length;
                float x;
                for (x = 0.0f; x <= length - sprite.size.X; x += sprite.size.X)
                {
                    GUI.DrawLine(spriteBatch, sprite,
                        startPos + dir * (x - 5.0f),
                        startPos + dir * (x + sprite.size.X),
                        SpriteColor, depth: depth, width: width);
                }
                float leftOver = length - x;
                if (leftOver > 0.0f)
                {
                    GUI.DrawLine(spriteBatch, sprite,
                        startPos + dir * (x - 5.0f),
                        endPos,
                        SpriteColor, depth: depth, width: width);
                }
            }
            else
            {
                GUI.DrawLine(spriteBatch, sprite,
                    startPos,
                    endPos,
                    SpriteColor, depth: depth, width: width);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            snapped = msg.ReadBoolean();
        }

        protected override void RemoveComponentSpecific()
        {
            sprite?.Remove(); sprite = null;
            startSprite?.Remove(); startSprite = null;
            endSprite?.Remove(); endSprite = null;
        }
    }
}
