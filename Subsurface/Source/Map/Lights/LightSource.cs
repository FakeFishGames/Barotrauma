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

        private List<ConvexHullList> hullsInRange;

        private Color color;

        private float range;

        private Texture2D texture;

        public Sprite LightSprite;

        private Sprite overrideLightTexture;

        public Submarine ParentSub;

        public bool CastShadows;

        //what was the range of the light when HullsInRange were last updated
        private float prevHullUpdateRange;

        private Vector2 prevHullUpdatePosition;

        public bool NeedsHullUpdate;

        private Vector2 position;
        public Vector2 Position
        {
            get { return position; }
            set
            {
                if (position == value) return;
                position = value;

                if (Vector2.Distance(prevHullUpdatePosition, position) < 5.0f) return;

                NeedsHullUpdate = true;
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
            get { return (ParentSub == null) ? position : position + ParentSub.Position; }
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
                if (Math.Abs(prevHullUpdateRange - range) < 10.0f) return;

                NeedsHullUpdate = true;
                prevHullUpdateRange = range;
            }
        }

        public LightSource (XElement element)
            : this(Vector2.Zero, 100.0f, Color.White, null)
        {
            range = ToolBox.GetAttributeFloat(element, "range", 100.0f);
            color = new Color(ToolBox.GetAttributeVector4(element, "color", Vector4.One));

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
            hullsInRange = new List<ConvexHullList>();

            this.ParentSub = submarine;

            this.position = position;
            this.range = range;
            this.color = color;

            CastShadows = true;

            texture = LightTexture;

            GameMain.LightManager.AddLight(this);
        }

        public void DrawShadows(GraphicsDevice graphics, Camera cam, Matrix shadowTransform)
        {
            if (!CastShadows) return;
            if (range < 1.0f || color.A < 0.01f) return;

            foreach (Submarine sub in Submarine.Loaded)
            {
                var hulls = GetHullsInRange(sub);

                if (hulls == null) continue;

                foreach ( ConvexHull ch in hulls)
                {
                    ch.DrawShadows(graphics, cam, this, shadowTransform, false);
                }                
            }

            var outsideHulls = GetHullsInRange(null);

            NeedsHullUpdate = false;

            if (outsideHulls == null) return;
            foreach (ConvexHull ch in outsideHulls)
            {
                ch.DrawShadows(graphics, cam, this, shadowTransform, false);
            } 
        }
        
        private List<ConvexHull> GetHullsInRange(Submarine sub)
        {
            //find the current list of hulls in range
            var chList = hullsInRange.Find(x => x.Submarine == sub);

            //not found -> create one
            if (chList == null)
            {
                chList = new ConvexHullList(sub);
                hullsInRange.Add(chList);
            }

            Vector2 lightPos = position;
            if (ParentSub == null)
            {
                //light and the convexhull are both outside
                if (sub == null)
                {
                    if (NeedsHullUpdate)
                    {
                        var fullChList = ConvexHull.HullLists.Find(x => x.Submarine == sub);
                        chList.List = fullChList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox));
                    }
                }
                //light is outside, convexhull inside a sub
                else
                {
                    lightPos -= sub.Position;

                    Rectangle subBorders = sub.Borders;
                    subBorders.Location += sub.HiddenSubPosition.ToPoint() - new Point(0, sub.Borders.Height);

                    //only draw if the light overlaps with the sub
                    if (!MathUtils.CircleIntersectsRectangle(lightPos, range, subBorders)) return null;

                    var fullChList = ConvexHull.HullLists.Find(x => x.Submarine == sub);
                    chList.List = fullChList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox));
                }
            }
            else 
            {
                //light is inside, convexhull outside
                if (sub == null) return null;
                
                //light and convexhull are both inside the same sub
                if (sub == ParentSub)
                {
                    if (NeedsHullUpdate)
                    {
                        var fullChList = ConvexHull.HullLists.Find(x => x.Submarine == sub);
                        chList.List = fullChList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox));
                    }
                }
                //light and convexhull are inside different subs
                else
                {
                    if (sub.DockedTo.Contains(ParentSub) && !NeedsHullUpdate) return chList.List;

                    lightPos -= (sub.Position - ParentSub.Position);

                    Rectangle subBorders = sub.Borders;
                    subBorders.Location += sub.HiddenSubPosition.ToPoint() - new Point(0, sub.Borders.Height);

                    //only draw if the light overlaps with the sub
                    if (!MathUtils.CircleIntersectsRectangle(lightPos, range, subBorders)) return null;

                    var fullChList = ConvexHull.HullLists.Find(x => x.Submarine == sub);
                    chList.List = fullChList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox));
                }
            }

            return chList.List;
        }

        public static List<ConvexHull> GetHullsInRange(Vector2 position, float range, Submarine ParentSub)
        {
            List<ConvexHull> list = new List<ConvexHull>();

            foreach (ConvexHullList chList in ConvexHull.HullLists)
            {
                Vector2 lightPos = position;
                if (ParentSub == null)
                {
                    //light and the convexhull are both outside
                    if (chList.Submarine == null)
                    {
                        list.AddRange(chList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox)));
                        
                    }
                    //light is outside, convexhull inside a sub
                    else
                    {
                        if (!MathUtils.CircleIntersectsRectangle(lightPos - chList.Submarine.WorldPosition, range, chList.Submarine.Borders)) continue;

                        lightPos -= (chList.Submarine.WorldPosition - chList.Submarine.HiddenSubPosition);

                        list.AddRange(chList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox)));
                    }
                }
                else
                {
                    //light is inside, convexhull outside
                    if (chList.Submarine == null) continue;

                    //light and convexhull are both inside the same sub
                    if (chList.Submarine == ParentSub)
                    {
                        list.AddRange(chList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox)));                        
                    }
                    //light and convexhull are inside different subs
                    else
                    {
                        lightPos -= (chList.Submarine.Position - ParentSub.Position);

                        Rectangle subBorders = chList.Submarine.Borders;
                        subBorders.Location += chList.Submarine.HiddenSubPosition.ToPoint() - new Point(0, chList.Submarine.Borders.Height);

                        if (!MathUtils.CircleIntersectsRectangle(lightPos, range, subBorders)) continue;

                       list.AddRange(chList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox)));
                    }
                }
            }


            return list;
        }

        
        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 drawPos = position;
            if (ParentSub != null) drawPos += ParentSub.DrawPosition;

            drawPos.Y = -drawPos.Y;

            if (range > 1.0f)
            {
                if (overrideLightTexture == null)
                {
                    Vector2 center = new Vector2(LightTexture.Width / 2, LightTexture.Height / 2);
                    float scale = range / (lightTexture.Width / 2.0f);

                    spriteBatch.Draw(lightTexture, drawPos, null, color * (color.A / 255.0f), 0, center, scale, SpriteEffects.None, 1);
                }
                else
                {
                    overrideLightTexture.Draw(spriteBatch,
                        drawPos, color * (color.A / 255.0f),
                        overrideLightTexture.Origin, -Rotation,
                        new Vector2(overrideLightTexture.size.X / overrideLightTexture.SourceRect.Width, overrideLightTexture.size.Y / overrideLightTexture.SourceRect.Height));
                }
            }

            if (LightSprite != null)
            {
                LightSprite.Draw(spriteBatch, drawPos, Color, LightSprite.Origin);

            } 
        }

        public void Remove()
        {
            if (LightSprite != null) LightSprite.Remove();

            GameMain.LightManager.RemoveLight(this);
        }
    }
}
