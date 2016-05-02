using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma.Lights
{
    class LightSource
    {
        private static Texture2D lightTexture;

        public List<ConvexHull> hullsInRange;

        private Color color;

        private float range;

        private Texture2D texture;

        public Sprite LightSprite;

        private Sprite overrideLightTexture;

        public Entity Submarine;

        public bool CastShadows;

        //what was the range of the light when HullsInRange were last updated
        private float prevHullUpdateRange;

        private Vector2 prevHullUpdatePosition;

        private Vector2 position;
        public Vector2 Position
        {
            get { return position; }
            set
            {
                if (position == value) return;
                position = value;

                if (Vector2.Distance(prevHullUpdatePosition, position) < 5.0f) return;
                
                UpdateHullsInRange();
                prevHullUpdatePosition = position;
            }
        }

        public float Rotation
        {
            get;
            set;
        }

        public Vector2 WorldPosition
        {
            get { return (Submarine == null) ? position : position + Submarine.Position; }
        }

        public static Texture2D LightTexture
        {
            get
            {
                if (lightTexture == null)
                {
                    lightTexture = TextureLoader.FromFile("Content/Lights/light.png");
                }

                return lightTexture;
            }
        }

        public Color Color
        {
            get { return color; }
            set { color = value; }
        }

        public float Range
        {
            get { return range; }
            set
            {

                range = MathHelper.Clamp(value, 0.0f, 2048.0f);
                if (Math.Abs(prevHullUpdateRange - range)<5.0f) return;
                
                UpdateHullsInRange();
                prevHullUpdateRange = range;
            }
        }

        public LightSource (XElement element)
            : this(Vector2.Zero, 100.0f, Color.White, null)
        {
            float range = ToolBox.GetAttributeFloat(element, "range", 100.0f);
            Color color = new Color(ToolBox.GetAttributeVector4(element, "color", Vector4.One));

            CastShadows = ToolBox.GetAttributeBool(element, "castshadows", true);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        LightSprite = new Sprite(subElement);
                        break;
                    case "lighttexture":
                        overrideLightTexture = new Sprite(subElement);
                        break;
                }
            }
        }

        public LightSource(Vector2 position, float range, Color color, Submarine submarine)
        {
            hullsInRange = new List<ConvexHull>();

            this.Submarine = submarine;

            this.position = position;
            this.range = range;
            this.color = color;

            CastShadows = true;

            texture = LightTexture;

            GameMain.LightManager.AddLight(this);
        }

        public void UpdateHullsInRange()
        {
            if (!CastShadows) return;

            hullsInRange.Clear();
            if (range < 1.0f || color.A < 0.01f) return;

            foreach (ConvexHull ch in ConvexHull.list)
            {
                if (Submarine == null && ch.ParentEntity.Submarine != null)
                {
                    if (MathUtils.CircleIntersectsRectangle(position - ch.ParentEntity.Submarine.Position, range, ch.BoundingBox))
                    {
                        hullsInRange.Add(ch);
                    }
                }
                else if (MathUtils.CircleIntersectsRectangle(position, range, ch.BoundingBox))
                {
                    hullsInRange.Add(ch);
                }

            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (range > 1.0f)
            {
                if (overrideLightTexture == null)
                {
                    Vector2 center = new Vector2(LightTexture.Width / 2, LightTexture.Height / 2);
                    float scale = range / (lightTexture.Width / 2.0f);

                    spriteBatch.Draw(lightTexture, new Vector2(WorldPosition.X, -WorldPosition.Y), null, color, 0, center, scale, SpriteEffects.None, 1);
                }
                else
                {
                    overrideLightTexture.Draw(spriteBatch, 
                        new Vector2(WorldPosition.X, -WorldPosition.Y), Color, 
                        overrideLightTexture.Origin, -Rotation, 
                        new Vector2(overrideLightTexture.size.X/overrideLightTexture.SourceRect.Width, overrideLightTexture.size.Y/overrideLightTexture.SourceRect.Height));
                }
            }

            if (LightSprite != null)
            {
                LightSprite.Draw(spriteBatch, new Vector2(WorldPosition.X, -WorldPosition.Y), Color, LightSprite.Origin);
            } 
        }

        public void Remove()
        {
            if (LightSprite != null) LightSprite.Remove();

            GameMain.LightManager.RemoveLight(this);
        }
    }
}
