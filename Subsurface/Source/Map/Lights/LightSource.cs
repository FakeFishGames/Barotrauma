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

        public SpriteEffects SpriteEffect = SpriteEffects.None;

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

        /*public void DrawShadows(GraphicsDevice graphics, Camera cam, Matrix shadowTransform)
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
                        if (fullChList != null)
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
        }*/

        private List<Vector2> FindRaycastHits()
        {
            if (!CastShadows) return null;
            if (range < 1.0f || color.A < 0.01f) return null;

            Vector2 drawPos = position;
            if (ParentSub != null) drawPos += ParentSub.DrawPosition;

            var hulls = ConvexHull.GetHullsInRange(position, range, ParentSub);

            //find convexhull segments that are close enough and facing towards the light source
            List<Segment> visibleSegments = new List<Segment>();
            List<SegmentPoint> points = new List<SegmentPoint>();
            foreach (ConvexHull hull in hulls)
            {
                hull.RefreshWorldPositions();

                var visibleHullSegments = hull.GetVisibleSegments(drawPos);
                visibleSegments.AddRange(visibleHullSegments);

                foreach (Segment s in visibleHullSegments)
                {
                    points.Add(s.Start);
                    points.Add(s.End);
                }
            }

            //add a square-shaped boundary to make sure we've got something to construct the triangles from
            //even if there aren't enough hull segments around the light source

            //(might be more effective to calculate if we actually need these extra points)
            var boundaryCorners = new List<SegmentPoint> {
                new SegmentPoint(new Vector2(drawPos.X + range*2, drawPos.Y + range*2)),
                new SegmentPoint(new Vector2(drawPos.X + range*2, drawPos.Y - range*2)),
                new SegmentPoint(new Vector2(drawPos.X - range*2, drawPos.Y - range*2)),
                new SegmentPoint(new Vector2(drawPos.X - range*2, drawPos.Y + range*2))
            };

            points.AddRange(boundaryCorners);

            var compareCCW = new CompareSegmentPointCW(drawPos);
            points.Sort(compareCCW);
            
            List<Vector2> output = new List<Vector2>();

            //remove points that are very close to each other
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (Math.Abs(points[i].WorldPos.X - points[i + 1].WorldPos.X) < 3 &&
                    Math.Abs(points[i].WorldPos.Y - points[i + 1].WorldPos.Y) < 3)
                {
                    points.RemoveAt(i + 1);
                }
            }

            foreach (SegmentPoint p in points)
            {
                Vector2 dir = Vector2.Normalize(p.WorldPos - drawPos);
                Vector2 dirNormal = new Vector2(-dir.Y, dir.X)*3;
                
                //do two slightly offset raycasts to hit the segment itself and whatever's behind it
                Vector2 intersection1 = RayCast(drawPos, drawPos + dir * range * 2 - dirNormal, visibleSegments);
                Vector2 intersection2 = RayCast(drawPos, drawPos + dir * range * 2 + dirNormal, visibleSegments);

                //hit almost the same position -> only add one vertex to output
                if ((Math.Abs(intersection1.X - intersection2.X) < 5 &&
                    Math.Abs(intersection1.Y - intersection2.Y) < 5))
                {
                    output.Add(intersection1);
                }
                else
                {
                    output.Add(intersection1);
                    output.Add(intersection2);
                }
            }
            
            return output;
        }

        private Vector2 RayCast(Vector2 rayStart, Vector2 rayEnd, List<Segment> segments)
        {
            float closestDist = 0.0f;
            Vector2? closestIntersection = null;

            foreach (Segment s in segments)
            {
                Vector2? intersection = MathUtils.GetAxisAlignedLineIntersection(rayStart, rayEnd, s.Start.WorldPos, s.End.WorldPos);

                if (intersection != null)
                {
                    float dist = Vector2.Distance((Vector2)intersection, rayStart);
                    if (closestIntersection == null || dist < closestDist)
                    {
                        closestDist = dist;
                        closestIntersection = intersection;
                    }
                }
            }            

            return closestIntersection == null ? rayEnd : (Vector2)closestIntersection;
        }

        private void CalculateVertices(List<Vector2> encounters,
            out VertexPositionTexture[] vertexArray, out short[] indexArray)
        {
            List<VertexPositionTexture> vertices = new List<VertexPositionTexture>();

            Vector2 drawPos = position;
            if (ParentSub != null) drawPos += ParentSub.DrawPosition;

            // Add a vertex for the center of the mesh
            vertices.Add(new VertexPositionTexture(new Vector3(drawPos.X, drawPos.Y, 0),
                new Vector2(0.5f, 0.5f)));

            // Add all the other encounter points as vertices
            // storing their world position as UV coordinates
            foreach (Vector2 vertex in encounters)
            {
                Vector2 diff = vertex - drawPos;

                vertices.Add(new VertexPositionTexture(new Vector3(vertex.X, vertex.Y, 0),
                   new Vector2(0.5f, 0.5f) + diff / range / 2));
            }

            // Compute the indices to form triangles
            List<short> indices = new List<short>();
            for (int i = 0; i < encounters.Count - 1; i++)
            {
                indices.Add(0);
                indices.Add((short)((i + 2) % vertices.Count));
                indices.Add((short)((i + 1) % vertices.Count));
            }

            indices.Add(0);
            indices.Add((short)(1));
            indices.Add((short)(vertices.Count - 1));

            vertexArray = vertices.ToArray<VertexPositionTexture>();
            indexArray = indices.ToArray<short>();
        }     

        public void Draw(SpriteBatch spriteBatch, BasicEffect lightEffect)
        {
            Vector2 drawPos = position;
            if (ParentSub != null) drawPos += ParentSub.DrawPosition;

            drawPos.Y = -drawPos.Y;
            
            if (range > 1.0f && false)
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
                        new Vector2(overrideLightTexture.size.X / overrideLightTexture.SourceRect.Width, overrideLightTexture.size.Y / overrideLightTexture.SourceRect.Height),
                        SpriteEffect);
                }
            }

            if (LightSprite != null)
            {
                //LightSprite.Draw(spriteBatch, drawPos, Color, LightSprite.Origin, -Rotation, 1, SpriteEffect);
            }

            var verts = FindRaycastHits();

            /*for (int i = 0; i < verts.Count; i++)
            {
                Color[] clrs = new Color[] { Color.Green, Color.Cyan, Color.Red, Color.White, Color.Magenta };

                Color clr = clrs[i % clrs.Length];

                // GUI.DrawString(spriteBatch, new Vector2(verts[i].X, -verts[i].Y), verts[i].ToString(), clr); 
                GUI.DrawString(spriteBatch, new Vector2(verts[i].X, -verts[i].Y), i.ToString(), clr);
                GUI.DrawLine(spriteBatch, drawPos, new Vector2(verts[i].X, -verts[i].Y), clr, 0,3);
            }*/

            // Generate a triangle list from the encounter points
            VertexPositionTexture[] vertices;
            short[] indices;
            CalculateVertices(verts, out vertices, out indices);
            
            if (vertices.Length == 0) return;

            lightEffect.DiffuseColor = (new Vector3(color.R, color.G, color.B) * (color.A / 255.0f)) / 255.0f;// color.ToVector3();
            lightEffect.CurrentTechnique.Passes[0].Apply();
            
            GameMain.CurrGraphicsDevice.DrawUserIndexedPrimitives<VertexPositionTexture>
            (
                PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, indices.Length / 3
            );
        }

        public void FlipX()
        {
            SpriteEffect = SpriteEffect == SpriteEffects.None ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            if (LightSprite != null)
            {
                Vector2 lightOrigin = LightSprite.Origin;
                lightOrigin.X = LightSprite.SourceRect.Width - lightOrigin.X;
                LightSprite.Origin = lightOrigin;
            }

            if (overrideLightTexture != null)
            {
                Vector2 lightOrigin = overrideLightTexture.Origin;
                lightOrigin.X = overrideLightTexture.SourceRect.Width - lightOrigin.X;
                overrideLightTexture.Origin = lightOrigin;
            }
        }

        public void Remove()
        {
            if (LightSprite != null) LightSprite.Remove();

            GameMain.LightManager.RemoveLight(this);
        }
    }
}
