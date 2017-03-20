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
        
        private Sprite overrideLightTexture;
        private Texture2D texture;

        public SpriteEffects SpriteEffect = SpriteEffects.None;
        public Sprite LightSprite;

        public Submarine ParentSub;

        public bool CastShadows;

        //what was the range of the light when lightvolumes were last calculated
        private float prevCalculatedRange;
        private Vector2 prevCalculatedPosition;

        //do we need to recheck which convex hulls are within range 
        //(e.g. position or range of the lightsource has changed)
        public bool NeedsHullCheck = true;
        //do we need to recalculate the vertices of the light volume
        public bool NeedsRecalculation = true;

        //when were the vertices of the light volume last calculated
        private float lastRecalculationTime;

        private Dictionary<Submarine, Vector2> diffToSub;

        private DynamicVertexBuffer lightVolumeBuffer;
        private DynamicIndexBuffer lightVolumeIndexBuffer;
        private int vertexCount;
        private int indexCount;

        private Vector2 position;
        public Vector2 Position
        {
            get { return position; }
            set
            {
                if (position == value) return;
                position = value;

                if (Vector2.Distance(prevCalculatedPosition, position) < 5.0f) return;

                NeedsHullCheck = true;
                NeedsRecalculation = true;
                prevCalculatedPosition = position;
            }
        }

        private float rotation;
        public float Rotation
        {
            get { return rotation; }
            set
            {
                if (rotation == value) return;
                rotation = value;

                NeedsHullCheck = true;
                NeedsRecalculation = true;
            }
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
                if (Math.Abs(prevCalculatedRange - range) < 10.0f) return;

                NeedsHullCheck = true;
                NeedsRecalculation = true;
                prevCalculatedRange = range;
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

            diffToSub = new Dictionary<Submarine, Vector2>();

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
        }*/
        
        /// <summary>
        /// Update the contents of ConvexHullList and check if we need to recalculate vertices
        /// </summary>
        private void RefreshConvexHullList(ConvexHullList chList, Vector2 lightPos, Submarine sub)
        {
            var fullChList = ConvexHull.HullLists.Find(x => x.Submarine == sub);
            if (fullChList == null) return;

            chList.List = fullChList.List.FindAll(ch => ch.Enabled && MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox));

            NeedsHullCheck = true;
        }

        /// <summary>
        /// Recheck which convex hulls are in range (if needed), 
        /// and check if we need to recalculate vertices due to changes in the convex hulls
        /// </summary>
        private void CheckHullsInRange()
        {
            List<Submarine> subs = new List<Submarine>(Submarine.Loaded);
            subs.Add(null);

            foreach (Submarine sub in subs)
            {
                //find the list of convexhulls that belong to the sub
                var chList = hullsInRange.Find(x => x.Submarine == sub);

                //not found -> create one
                if (chList == null)
                {
                    chList = new ConvexHullList(sub);
                    hullsInRange.Add(chList);
                    NeedsRecalculation = true;
                }

                if (chList.List.Any(ch => ch.LastVertexChangeTime > lastRecalculationTime))
                {
                    NeedsRecalculation = true;
                }

                Vector2 lightPos = position;
                if (ParentSub == null)
                {
                    //light and the convexhulls are both outside
                    if (sub == null)
                    {
                        if (NeedsHullCheck)
                        {
                            RefreshConvexHullList(chList, lightPos, null);
                        }
                    }
                    //light is outside, convexhulls inside a sub
                    else
                    {
                        lightPos -= sub.Position;

                        Rectangle subBorders = sub.Borders;
                        subBorders.Location += sub.HiddenSubPosition.ToPoint() - new Point(0, sub.Borders.Height);

                        //only draw if the light overlaps with the sub
                        if (!MathUtils.CircleIntersectsRectangle(lightPos, range, subBorders))
                        {
                            if (chList.List.Count > 0) NeedsRecalculation = true;
                            chList.List.Clear();
                            continue;
                        }
                        
                        RefreshConvexHullList(chList, lightPos, sub);
                    }
                }
                else 
                {
                    //light is inside, convexhull outside
                    if (sub == null) continue;
                
                    //light and convexhull are both inside the same sub
                    if (sub == ParentSub)
                    {
                        if (NeedsHullCheck)
                        {                            
                            RefreshConvexHullList(chList, lightPos, sub);
                        }
                    }
                    //light and convexhull are inside different subs
                    else
                    {
                        if (sub.DockedTo.Contains(ParentSub) && !NeedsHullCheck) continue;
                        
                        lightPos -= (sub.Position - ParentSub.Position);

                        Rectangle subBorders = sub.Borders;
                        subBorders.Location += sub.HiddenSubPosition.ToPoint() - new Point(0, sub.Borders.Height);

                        //don't draw any shadows if the light doesn't overlap with the borders of the sub
                        if (!MathUtils.CircleIntersectsRectangle(lightPos, range, subBorders))
                        {
                            if (chList.List.Count > 0) NeedsRecalculation = true;
                            chList.List.Clear();
                            continue;
                        }

                        //recalculate vertices if the subs have moved > 5 px relative to each other
                        Vector2 diff = ParentSub.WorldPosition - sub.WorldPosition;
                        Vector2 prevDiff;
                        if (!diffToSub.TryGetValue(sub, out prevDiff))
                        {
                            diffToSub.Add(sub, diff);
                            NeedsRecalculation = true;
                        }
                        else if (Vector2.DistanceSquared(diff, prevDiff) > 5.0f*5.0f)
                        {
                            diffToSub[sub] = diff;
                            NeedsRecalculation = true;
                        }

                        RefreshConvexHullList(chList, lightPos, sub);
                    }
                }
            }
        }

        private List<Vector2> FindRaycastHits()
        {
            if (!CastShadows) return null;
            if (range < 1.0f || color.A < 0.01f) return null;

            Vector2 drawPos = position;
            if (ParentSub != null) drawPos += ParentSub.DrawPosition;

            var hulls = new List<ConvexHull>();// ConvexHull.GetHullsInRange(position, range, ParentSub);
            foreach (ConvexHullList chList in hullsInRange)
            {
                hulls.AddRange(chList.List);
            }

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
                Vector2? intersection = MathUtils.GetAxisAlignedLineIntersection(rayStart, rayEnd, s.Start.WorldPos, s.End.WorldPos, s.IsHorizontal);

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

        private void CalculateLightVertices(List<Vector2> rayCastHits)
        {
            List<VertexPositionTexture> vertices = new List<VertexPositionTexture>();

            Vector2 drawPos = position;
            if (ParentSub != null) drawPos += ParentSub.DrawPosition;

            float cosAngle = (float)Math.Cos(Rotation);
            float sinAngle = -(float)Math.Sin(Rotation);
            
            Vector2 uvOffset = Vector2.Zero;
            Vector2 overrideTextureDims = Vector2.One;
            if (overrideLightTexture != null)
            {
                overrideTextureDims = new Vector2(overrideLightTexture.SourceRect.Width, overrideLightTexture.SourceRect.Height);
                uvOffset = (overrideLightTexture.Origin / overrideTextureDims) - new Vector2(0.5f, 0.5f);
            }

            // Add a vertex for the center of the mesh
            vertices.Add(new VertexPositionTexture(new Vector3(position.X, position.Y, 0),
                new Vector2(0.5f, 0.5f) + uvOffset));

            // Add all the other encounter points as vertices
            // storing their world position as UV coordinates
            foreach (Vector2 vertex in rayCastHits)
            {
                Vector2 rawDiff = vertex - drawPos;
                Vector2 diff = rawDiff;
                diff /= range*2.0f;
                if (overrideLightTexture != null)
                {
                    Vector2 originDiff = diff;

                    diff.X = originDiff.X * cosAngle - originDiff.Y * sinAngle;
                    diff.Y = originDiff.X * sinAngle + originDiff.Y * cosAngle;
                    diff *= (overrideTextureDims / overrideLightTexture.size) * 2.0f;

                    diff += uvOffset;
                }

                vertices.Add(new VertexPositionTexture(new Vector3(position.X + rawDiff.X, position.Y + rawDiff.Y, 0),
                   new Vector2(0.5f, 0.5f) + diff));
            }

            // Compute the indices to form triangles
            List<short> indices = new List<short>();
            for (int i = 0; i < rayCastHits.Count - 1; i++)
            {
                indices.Add(0);
                indices.Add((short)((i + 2) % vertices.Count));
                indices.Add((short)((i + 1) % vertices.Count));
            }

            indices.Add(0);
            indices.Add((short)(1));
            indices.Add((short)(vertices.Count - 1));

            vertexCount = vertices.Count;
            indexCount = indices.Count;

            //TODO: a better way to determine the size of the vertex buffer and handle changes in size?
            //now we just create a buffer for 64 verts and make it larger if needed
            if (lightVolumeBuffer == null)
            {
                lightVolumeBuffer = new DynamicVertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionTexture.VertexDeclaration, Math.Max(64, (int)(vertexCount*1.5)), BufferUsage.None);
                lightVolumeIndexBuffer = new DynamicIndexBuffer(GameMain.CurrGraphicsDevice, typeof(short), Math.Max(64*3, (int)(indexCount * 1.5)), BufferUsage.None);
            }
            else if (vertexCount > lightVolumeBuffer.VertexCount)
            {
                lightVolumeBuffer.Dispose();
                lightVolumeIndexBuffer.Dispose();

                lightVolumeBuffer = new DynamicVertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionTexture.VertexDeclaration, (int)(vertexCount*1.5), BufferUsage.None);
                lightVolumeIndexBuffer = new DynamicIndexBuffer(GameMain.CurrGraphicsDevice, typeof(short), (int)(indexCount * 1.5), BufferUsage.None);
            }
                        
            lightVolumeBuffer.SetData<VertexPositionTexture>(vertices.ToArray());
            lightVolumeIndexBuffer.SetData<short>(indices.ToArray());
        }     

        public void Draw(SpriteBatch spriteBatch, BasicEffect lightEffect, Matrix transform)
        {
            CheckHullsInRange();

            Vector3 offset = ParentSub == null ? Vector3.Zero :
            new Vector3(ParentSub.DrawPosition.X, ParentSub.DrawPosition.Y, 0.0f);

            lightEffect.World = Matrix.CreateTranslation(offset) * transform;

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

            if (NeedsRecalculation)
            {
                var verts = FindRaycastHits();
                CalculateLightVertices(verts);

                lastRecalculationTime = (float)GameMain.Instance.TotalElapsedTime;
                NeedsRecalculation = false;
            }
            
            if (vertexCount == 0) return;

            lightEffect.DiffuseColor = (new Vector3(color.R, color.G, color.B) * (color.A / 255.0f)) / 255.0f;// color.ToVector3();
            if (overrideLightTexture != null)
            {
                lightEffect.Texture = overrideLightTexture.Texture;
            }
            else
            {
                lightEffect.Texture = LightTexture;
            }
            lightEffect.CurrentTechnique.Passes[0].Apply();

            GameMain.CurrGraphicsDevice.SetVertexBuffer(lightVolumeBuffer);
            GameMain.CurrGraphicsDevice.Indices = lightVolumeIndexBuffer;
            
            GameMain.CurrGraphicsDevice.DrawIndexedPrimitives
            (
                PrimitiveType.TriangleList, 0, 0, indexCount / 3
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

            if (lightVolumeBuffer != null)
            {
                lightVolumeBuffer.Dispose();
                lightVolumeBuffer = null;
            }

            if (lightVolumeIndexBuffer != null)
            {
                lightVolumeIndexBuffer.Dispose();
                lightVolumeIndexBuffer = null;
            }

            GameMain.LightManager.RemoveLight(this);
        }
    }
}
