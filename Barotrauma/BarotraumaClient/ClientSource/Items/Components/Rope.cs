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

        [Serialize(5, IsPropertySaveable.No)]
        public int SpriteWidth
        {
            get;
            set;
        }

        [Serialize("255,255,255,255", IsPropertySaveable.No)]
        public Color SpriteColor
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool Tile
        {
            get;
            set;
        }

        [Serialize("0.5,0.5)", IsPropertySaveable.No)]
        public Vector2 Origin { get; set; } = new Vector2(0.5f, 0.5f);

        public Vector2 DrawSize
        {
            get 
            {
                if (target == null || source == null) { return Vector2.Zero; }

                Vector2 sourcePos = GetSourcePos();

                return new Vector2(
                    Math.Abs(target.DrawPosition.X - sourcePos.X),
                    Math.Abs(target.DrawPosition.Y - sourcePos.Y)) * 1.5f;
            }
        }

        private Vector2 GetSourcePos()
        {
            Vector2 sourcePos = source.WorldPosition;
            if (source is Item sourceItem)
            {
                sourcePos = sourceItem.DrawPosition;
            }
            else if (source is Limb sourceLimb && sourceLimb.body != null)
            {
                sourcePos = sourceLimb.body.DrawPosition;
            }
            return sourcePos;
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
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
            if (target == null || target.Removed) { return; }
            if (target.ParentInventory != null) { return; }

            Vector2 startPos = GetSourcePos();
            startPos.Y = -startPos.Y;
            if (source is Item sourceItem && !sourceItem.Removed)
            {
                var turret = sourceItem.GetComponent<Turret>();
                var weapon = sourceItem.GetComponent<RangedWeapon>();
                if (turret != null)
                {
                    startPos = new Vector2(sourceItem.WorldRect.X + turret.TransformedBarrelPos.X, -(sourceItem.WorldRect.Y - turret.TransformedBarrelPos.Y));
                    if (turret.BarrelSprite != null)
                    {
                        startPos += new Vector2((float)Math.Cos(turret.Rotation), (float)Math.Sin(turret.Rotation)) * turret.BarrelSprite.size.Y * turret.BarrelSprite.RelativeOrigin.Y * item.Scale * 0.9f;
                    }
                }
                else if (weapon != null)
                {
                    Vector2 barrelPos = FarseerPhysics.ConvertUnits.ToDisplayUnits(weapon.TransformedBarrelPos);
                    barrelPos.Y = -barrelPos.Y;
                    startPos += barrelPos;
                }
            }
            Vector2 endPos = new Vector2(target.DrawPosition.X, target.DrawPosition.Y);
            Vector2 flippedPos = target.Sprite.size * target.Scale * (Origin - new Vector2(0.5f));
            if (target.body.Dir < 0.0f)
            {
                flippedPos.X = -flippedPos.X;
            }
            endPos += Vector2.Transform(flippedPos, Matrix.CreateRotationZ(target.body.Rotation));
            endPos.Y = -endPos.Y;

            if (Snapped)
            {
                float snapState = 1.0f - snapTimer / SnapAnimDuration;
                Vector2 diff = target.DrawPosition - new Vector2(startPos.X, -startPos.Y);
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

            if (!snapped)
            {
                UInt16 targetId = msg.ReadUInt16();
                UInt16 sourceId = msg.ReadUInt16();
                byte limbIndex = msg.ReadByte();

                Item target = Entity.FindEntityByID(targetId) as Item;
                if (target == null) { return; }
                var source = Entity.FindEntityByID(sourceId);
                if (source is Character sourceCharacter && limbIndex >= 0 && limbIndex < sourceCharacter.AnimController.Limbs.Length)
                {
                    Limb sourceLimb = sourceCharacter.AnimController.Limbs[limbIndex];
                    Attach(sourceLimb, target);
                }
                else if (source is ISpatialEntity spatialEntity)
                {
                    Attach(spatialEntity, target);
                }
            }
        }

        protected override void RemoveComponentSpecific()
        {
            sprite?.Remove(); sprite = null;
            startSprite?.Remove(); startSprite = null;
            endSprite?.Remove(); endSprite = null;
        }
    }
}
