using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma.Lights
{
    class ConvexHullList
    {
        private List<ConvexHull> list;
        public HashSet<ConvexHull> IsHidden;

        public readonly Submarine Submarine;
        public List<ConvexHull> List
        {
            get { return list; }
            set
            {
                Debug.Assert(value != null);
                Debug.Assert(!list.Contains(null));
                list = value;
                IsHidden.RemoveWhere(ch => !list.Contains(ch));
            }
        }
        

        public ConvexHullList(Submarine submarine)
        {
            Submarine = submarine;
            list = new List<ConvexHull>();
            IsHidden = new HashSet<ConvexHull>();
        }
    }

    class Segment
    {
        public SegmentPoint Start;
        public SegmentPoint End;

        public ConvexHull ConvexHull;

        public bool IsHorizontal;
        public bool IsAxisAligned;

        public Segment(SegmentPoint start, SegmentPoint end, ConvexHull convexHull)
        {
            if (start.Pos.Y > end.Pos.Y)
            {
                var temp = start;
                start = end;
                end = temp;
            }

            Start = start;
            End = end;
            ConvexHull = convexHull;

            start.ConvexHull = convexHull;
            end.ConvexHull = convexHull;

            IsHorizontal = Math.Abs(start.Pos.X - end.Pos.X) > Math.Abs(start.Pos.Y - end.Pos.Y);
            IsAxisAligned = Math.Abs(start.Pos.X - end.Pos.X) < 0.1f || Math.Abs(start.Pos.Y - end.Pos.Y) < 0.001f;
        }
    }

    struct SegmentPoint
    {
        public Vector2 Pos;        
        public Vector2 WorldPos;

        public ConvexHull ConvexHull;

        public SegmentPoint(Vector2 pos, ConvexHull convexHull)
        {
            Pos = pos;
            WorldPos = pos;
            ConvexHull = convexHull;
        }

        public override string ToString()
        {
            return Pos.ToString();
        }
    }

    class ConvexHull
    {
        public static List<ConvexHullList> HullLists = new List<ConvexHullList>();
        public static BasicEffect shadowEffect;
        public static BasicEffect penumbraEffect;

        private readonly Segment[] segments = new Segment[4];
        private readonly SegmentPoint[] vertices = new SegmentPoint[4];
        private readonly SegmentPoint[] losVertices = new SegmentPoint[4];
        
        private readonly bool[] backFacing;
        private readonly bool[] ignoreEdge;

        private readonly bool isHorizontal;

        public VertexPositionColor[] ShadowVertices { get; private set; }
        public VertexPositionTexture[] PenumbraVertices { get; private set; }
        public int ShadowVertexCount { get; private set; }

        private readonly HashSet<ConvexHull> overlappingHulls = new HashSet<ConvexHull>();

        public MapEntity ParentEntity { get; private set; }

        private bool enabled;
        public bool Enabled
        {
            get
            {
                return enabled;
            }
            set
            {
                if (enabled == value) return;
                enabled = value;
                LastVertexChangeTime = (float)Timing.TotalTime;
            }
        }

        /// <summary>
        /// The elapsed gametime when the vertices of this hull last changed
        /// </summary>
        public float LastVertexChangeTime
        {
            get;
            private set;
        }

        public Rectangle BoundingBox { get; private set; }

        public ConvexHull(Vector2[] points, Color color, MapEntity parent)
        {
            if (shadowEffect == null)
            {
                shadowEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    VertexColorEnabled = true
                };
            }
            if (penumbraEffect == null)
            {
                penumbraEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    TextureEnabled = true,
                    LightingEnabled = false,
                    Texture = TextureLoader.FromFile("Content/Lights/penumbra.png")
                };
            }

            ParentEntity = parent;

            ShadowVertices = new VertexPositionColor[6 * 2];
            PenumbraVertices = new VertexPositionTexture[6];
            
            backFacing = new bool[4];
            ignoreEdge = new bool[4];

            SetVertices(points);          
                        
            Enabled = true;

            isHorizontal = BoundingBox.Width > BoundingBox.Height;
            if (ParentEntity is Structure structure)
            {
                isHorizontal = structure.IsHorizontal;
            }
            else if (ParentEntity is Item item)
            {
                var door = item.GetComponent<Door>();
                if (door != null) { isHorizontal = door.IsHorizontal; }
            }

            var chList = HullLists.Find(h => h.Submarine == parent.Submarine);
            if (chList == null)
            {
                chList = new ConvexHullList(parent.Submarine);
                HullLists.Add(chList);
            }                       
            
            foreach (ConvexHull ch in chList.List)
            {
                MergeOverlappingSegments(ch);
                ch.MergeOverlappingSegments(this);
            }

            chList.List.Add(this);
        }

        private void MergeOverlappingSegments(ConvexHull ch)
        {
            if (ch == this) { return; }

            if (isHorizontal == ch.isHorizontal)
            {
                if (BoundingBox == ch.BoundingBox) { return; }

                //hide segments that are roughly at the some position as some other segment (e.g. the ends of two adjacent wall pieces)
                float mergeDist = 32;
                float mergeDistSqr = mergeDist * mergeDist;
                for (int i = 0; i < segments.Length; i++)
                {
                    for (int j = 0; j < ch.segments.Length; j++)
                    {
                        if (segments[i].IsHorizontal != ch.segments[j].IsHorizontal) { continue; }
                        if (ignoreEdge[i] || ch.ignoreEdge[j]) { continue; }

                        //the segments must be at different sides of the convex hulls to be merged
                        //(e.g. the right edge of a wall piece and the left edge of another one)
                        var segment1Center = (segments[i].Start.Pos + segments[i].End.Pos) / 2.0f;
                        var segment2Center = (ch.segments[j].Start.Pos + ch.segments[j].End.Pos) / 2.0f;
                        if (Vector2.Dot(segment1Center - BoundingBox.Center.ToVector2(), segment2Center - ch.BoundingBox.Center.ToVector2()) > 0) { continue; }

                        if (Vector2.DistanceSquared(segments[i].Start.Pos, ch.segments[j].Start.Pos) < mergeDistSqr &&
                            Vector2.DistanceSquared(segments[i].End.Pos, ch.segments[j].End.Pos) < mergeDistSqr)
                        {
                            ignoreEdge[i] = true;
                            ch.ignoreEdge[j] = true;
                            MergeSegments(segments[i], ch.segments[j], true);
                        }
                        else if (Vector2.DistanceSquared(segments[i].Start.Pos, ch.segments[j].End.Pos) < mergeDistSqr &&
                                Vector2.DistanceSquared(segments[i].End.Pos, ch.segments[j].Start.Pos) < mergeDistSqr)
                        {
                            ignoreEdge[i] = true;
                            ch.ignoreEdge[j] = true;
                            MergeSegments(segments[i], ch.segments[j], false);
                        }
                    }             
                }
            }
            else
            {
                //TODO: do something to corner areas where a vertical wall meets a horizontal one
            }
            
            //ignore edges that are inside some other convex hull
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].Pos.X >= ch.BoundingBox.X && vertices[i].Pos.X <= ch.BoundingBox.Right && 
                    vertices[i].Pos.Y >= ch.BoundingBox.Y && vertices[i].Pos.Y <= ch.BoundingBox.Bottom)
                {
                    Vector2 p = vertices[(i + 1) % vertices.Length].Pos;

                    if (p.X >= ch.BoundingBox.X && p.X <= ch.BoundingBox.Right && 
                        p.Y >= ch.BoundingBox.Y && p.Y <= ch.BoundingBox.Bottom)
                    {
                        ignoreEdge[i] = true;
                        overlappingHulls.Add(ch);
                    }
                }
            }
        }

        private void MergeSegments(Segment segment1, Segment segment2, bool startPointsMatch)
        {
            int startPointIndex = -1, endPointIndex = -1;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].Pos.NearlyEquals(segment1.Start.Pos))                
                    startPointIndex = i;                
                else if (vertices[i].Pos.NearlyEquals(segment1.End.Pos))                
                    endPointIndex = i;                
            }
            if (startPointIndex == -1 || endPointIndex == -1) { return; }

            int startPoint2Index = -1, endPoint2Index = -1;
            for (int i = 0; i < segment2.ConvexHull.vertices.Length; i++)
            {
                if (segment2.ConvexHull.vertices[i].Pos.NearlyEquals(segment2.Start.Pos))                
                    startPoint2Index = i;                
                else if (segment2.ConvexHull.vertices[i].Pos.NearlyEquals(segment2.End.Pos))                
                    endPoint2Index = i;
            }
            if (startPoint2Index == -1 || endPoint2Index == -1) { return; }

            if (startPointsMatch)
            {
                losVertices[startPointIndex].Pos = segment2.ConvexHull.losVertices[startPoint2Index].Pos =
                    (segment1.Start.Pos + segment2.Start.Pos) / 2.0f;
                losVertices[endPointIndex].Pos = segment2.ConvexHull.losVertices[endPoint2Index].Pos =
                    (segment1.End.Pos + segment2.End.Pos) / 2.0f;
            }
            else
            {
                if (Vector2.DistanceSquared(losVertices[startPointIndex].Pos, segment1.Start.Pos) < 
                    Vector2.DistanceSquared(losVertices[startPointIndex].Pos, segment1.End.Pos))
                {
                    losVertices[startPointIndex].Pos = segment2.ConvexHull.losVertices[startPoint2Index].Pos =
                        (segment1.Start.Pos + segment2.End.Pos) / 2.0f;
                    losVertices[endPointIndex].Pos = segment2.ConvexHull.losVertices[endPoint2Index].Pos =
                        (segment1.End.Pos + segment2.Start.Pos) / 2.0f;
                }
                else
                {
                    losVertices[startPointIndex].Pos = segment2.ConvexHull.losVertices[startPoint2Index].Pos =
                        (segment1.End.Pos + segment2.Start.Pos) / 2.0f;
                    losVertices[endPointIndex].Pos = segment2.ConvexHull.losVertices[endPoint2Index].Pos =
                        (segment1.Start.Pos + segment2.End.Pos) / 2.0f;
                }
            }

            overlappingHulls.Add(segment2.ConvexHull);
            segment2.ConvexHull.overlappingHulls.Add(this);
        }

        public void Rotate(Vector2 origin, float amount)
        {
            Matrix rotationMatrix = 
                Matrix.CreateTranslation(-origin.X, -origin.Y, 0.0f) * 
                Matrix.CreateRotationZ(amount) *
                Matrix.CreateTranslation(origin.X, origin.Y, 0.0f);
            SetVertices(vertices.Select(v => v.Pos).ToArray(), rotationMatrix);
        }

        private void CalculateDimensions()
        {
            float minX = vertices[0].Pos.X, minY = vertices[0].Pos.Y, maxX = vertices[0].Pos.X, maxY = vertices[0].Pos.Y;

            for (int i = 1; i < vertices.Length; i++)
            {
                if (vertices[i].Pos.X < minX) minX = vertices[i].Pos.X;
                if (vertices[i].Pos.Y < minY) minY = vertices[i].Pos.Y;

                if (vertices[i].Pos.X > maxX) maxX = vertices[i].Pos.X;
                if (vertices[i].Pos.Y > minY) maxY = vertices[i].Pos.Y;
            }

            BoundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }
                
        public void Move(Vector2 amount)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].Pos         += amount;
                losVertices[i].Pos      += amount;

                segments[i].Start.Pos   += amount;
                segments[i].End.Pos     += amount;
            }

            LastVertexChangeTime = (float)Timing.TotalTime;

            overlappingHulls.Clear();
            for (int i = 0; i < 4; i++)
            {
                ignoreEdge[i] = false;
            }

            CalculateDimensions();

            if (ParentEntity == null) { return; }

            var chList = HullLists.Find(h => h.Submarine == ParentEntity.Submarine);
            if (chList != null)
            {
                overlappingHulls.Clear();
                foreach (ConvexHull ch in chList.List)
                {
                    MergeOverlappingSegments(ch);
                    ch.MergeOverlappingSegments(this);
                }
            }
        }

        public static void RecalculateAll(Submarine sub)
        {
            var chList = HullLists.Find(h => h.Submarine == sub);
            if (chList != null)
            {
                foreach (ConvexHull ch in chList.List)
                {
                    ch.overlappingHulls.Clear();
                    for (int i = 0; i < 4; i++)
                    {
                        ch.ignoreEdge[i] = false;
                    }
                }
                for (int i = 0; i < chList.List.Count; i++)
                {
                    for (int j = i + 1; j < chList.List.Count; j++)
                    {
                        chList.List[i].MergeOverlappingSegments(chList.List[j]);
                        chList.List[j].MergeOverlappingSegments(chList.List[i]);
                    }
                }
            }
        }

        public void SetVertices(Vector2[] points, Matrix? rotationMatrix = null)
        {
            Debug.Assert(points.Length == 4, "Only rectangular convex hulls are supported");

            LastVertexChangeTime = (float)Timing.TotalTime;

            for (int i = 0; i < 4; i++)
            {
                vertices[i]     = new SegmentPoint(points[i], this);
                losVertices[i]  = new SegmentPoint(points[i], this);
            }

            for (int i = 0; i < 4; i++)
            {
                ignoreEdge[i] = false;
            }

            overlappingHulls.Clear();

            int margin = 0;
            if (Math.Abs(points[0].X - points[2].X) < Math.Abs(points[0].Y - points[2].Y))
            {
                losVertices[0].Pos = new Vector2(points[0].X + margin, points[0].Y);
                losVertices[1].Pos = new Vector2(points[1].X + margin, points[1].Y);
                losVertices[2].Pos = new Vector2(points[2].X - margin, points[2].Y);
                losVertices[3].Pos = new Vector2(points[3].X - margin, points[3].Y);
            }
            else
            {
                losVertices[0].Pos = new Vector2(points[0].X, points[0].Y + margin);
                losVertices[1].Pos = new Vector2(points[1].X, points[1].Y - margin);
                losVertices[2].Pos = new Vector2(points[2].X, points[2].Y - margin);
                losVertices[3].Pos = new Vector2(points[3].X, points[3].Y + margin);
            }

            if (rotationMatrix.HasValue)
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i].Pos = Vector2.Transform(vertices[i].Pos, rotationMatrix.Value);
                    losVertices[i].Pos = Vector2.Transform(losVertices[i].Pos, rotationMatrix.Value);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                segments[i] = new Segment(vertices[i], vertices[(i + 1) % 4], this);
            }

            CalculateDimensions();

            if (ParentEntity == null) return;

            var chList = HullLists.Find(h => h.Submarine == ParentEntity.Submarine);
            if (chList != null)
            {
                overlappingHulls.Clear();
                foreach (ConvexHull ch in chList.List)
                {
                    MergeOverlappingSegments(ch);
                }
            }
        }

        public bool Intersects(Rectangle rect)
        {
            if (!Enabled) return false;

            Rectangle transformedBounds = BoundingBox;
            if (ParentEntity != null && ParentEntity.Submarine != null)
            {
                transformedBounds.X += (int)ParentEntity.Submarine.Position.X;
                transformedBounds.Y += (int)ParentEntity.Submarine.Position.Y;
            }
            return transformedBounds.Intersects(rect);
        }
        
        /// <summary>
        /// Returns the segments that are facing towards viewPosition
        /// </summary>
        public void GetVisibleSegments(Vector2 viewPosition, List<Segment> visibleSegments, bool ignoreEdges)
        {            
            for (int i = 0; i < 4; i++)
            {
                if (ignoreEdge[i] && ignoreEdges) continue;

                Vector2 pos1 = vertices[i].WorldPos;
                Vector2 pos2 = vertices[(i + 1) % 4].WorldPos;

                Vector2 middle = (pos1 + pos2) / 2;

                Vector2 L = viewPosition - middle;

                Vector2 N = new Vector2(
                    -(pos2.Y - pos1.Y),
                    pos2.X - pos1.X);

                if (Vector2.Dot(N, L) > 0)
                {
                    visibleSegments.Add(segments[i]);
                }
            }
        }


        public void RefreshWorldPositions()
        {
            for (int i = 0; i < 4; i++)
            {
                vertices[i].WorldPos = vertices[i].Pos;
                segments[i].Start.WorldPos = segments[i].Start.Pos;
                segments[i].End.WorldPos = segments[i].End.Pos;
            }
            if (ParentEntity == null || ParentEntity.Submarine == null) { return; }
            for (int i = 0; i < 4; i++)
            {
                vertices[i].WorldPos += ParentEntity.Submarine.DrawPosition;
                segments[i].Start.WorldPos += ParentEntity.Submarine.DrawPosition;
                segments[i].End.WorldPos += ParentEntity.Submarine.DrawPosition;
            }
        }

        public void CalculateShadowVertices(Vector2 lightSourcePos, bool los = true)
        {
            Vector3 offset = Vector3.Zero;
            if (ParentEntity != null && ParentEntity.Submarine != null)
            {
                offset = new Vector3(ParentEntity.Submarine.DrawPosition.X, ParentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            ShadowVertexCount = 0;

            var vertices = los ? losVertices : this.vertices;
            
            //compute facing of each edge, using N*L
            for (int i = 0; i < 4; i++)
            {
                if (ignoreEdge[i])
                {
                    backFacing[i] = false;
                    continue;
                }

                Vector2 firstVertex = vertices[i].Pos;
                Vector2 secondVertex = vertices[(i+1) % 4].Pos;

                Vector2 L = lightSourcePos - ((firstVertex + secondVertex) / 2.0f);

                Vector2 N = new Vector2(
                    -(secondVertex.Y - firstVertex.Y),
                    secondVertex.X - firstVertex.X);

                backFacing[i] = (Vector2.Dot(N, L) < 0) == los;
            }

            //find beginning and ending vertices which
            //belong to the shadow
            int startingIndex = -1;
            int endingIndex = -1;
            for (int i = 0; i < 4; i++)
            {
                int currentEdge = i;
                int nextEdge = (i + 1) % 4;

                if (backFacing[currentEdge] && !backFacing[nextEdge])
                    endingIndex = nextEdge;

                if (!backFacing[currentEdge] && backFacing[nextEdge])
                    startingIndex = nextEdge;
            }

            if (startingIndex == -1 || endingIndex == -1) { return; }

            //nr of vertices that are in the shadow
            if (endingIndex > startingIndex)
                ShadowVertexCount = endingIndex - startingIndex + 1;
            else
                ShadowVertexCount = 4 + 1 - startingIndex + endingIndex;

            //shadowVertices = new VertexPositionColor[shadowVertexCount * 2];

            //create a triangle strip that has the shape of the shadow
            int currentIndex = startingIndex;
            int svCount = 0;
            while (svCount != ShadowVertexCount * 2)
            {
                Vector3 vertexPos = new Vector3(vertices[currentIndex].Pos, 0.0f);

                int i = los ? svCount : svCount + 1;
                int j = los ? svCount + 1 : svCount;

                //one vertex on the hull
                ShadowVertices[i] = new VertexPositionColor
                {
                    Color = los ? Color.Black : Color.Transparent,
                    Position = vertexPos + offset
                };

                //one extruded by the light direction
                ShadowVertices[j] = new VertexPositionColor
                {
                    Color = ShadowVertices[i].Color
                };

                Vector3 L2P = vertexPos - new Vector3(lightSourcePos, 0);
                L2P.Normalize();
                
                ShadowVertices[j].Position = new Vector3(lightSourcePos, 0) + L2P * 9000 + offset;

                svCount += 2;
                currentIndex = (currentIndex + 1) % 4;
            }

            if (los)
            {
                CalculatePenumbraVertices(startingIndex, endingIndex, lightSourcePos, los);
            }
        }

        private void CalculatePenumbraVertices(int startingIndex, int endingIndex, Vector2 lightSourcePos, bool los)
        {
            var vertices = los ? losVertices : this.vertices;

            Vector3 offset = Vector3.Zero;
            if (ParentEntity != null && ParentEntity.Submarine != null)
            {
                offset = new Vector3(ParentEntity.Submarine.DrawPosition.X, ParentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            for (int n = 0; n < 4; n += 3)
            {
                Vector3 penumbraStart = new Vector3((n == 0) ? vertices[startingIndex].Pos : vertices[endingIndex].Pos, 0.0f);

                PenumbraVertices[n] = new VertexPositionTexture
                {
                    Position = penumbraStart + offset,
                    TextureCoordinate = new Vector2(0.0f, 1.0f)
                };

                for (int i = 0; i < 2; i++)
                {
                    PenumbraVertices[n + i + 1] = new VertexPositionTexture();
                    Vector3 vertexDir = penumbraStart - new Vector3(lightSourcePos, 0);
                    vertexDir.Normalize();

                    Vector3 normal = (i == 0) ? new Vector3(-vertexDir.Y, vertexDir.X, 0.0f) : new Vector3(vertexDir.Y, -vertexDir.X, 0.0f) * 0.05f;
                    if (n > 0) normal = -normal;

                    vertexDir = penumbraStart - (new Vector3(lightSourcePos, 0) - normal * 20.0f);
                    vertexDir.Normalize();
                    PenumbraVertices[n + i + 1].Position = new Vector3(lightSourcePos, 0) + vertexDir * 9000 + offset;

                    if (los)
                    {
                        PenumbraVertices[n + i + 1].TextureCoordinate = (i == 0) ? new Vector2(0.05f, 0.0f) : new Vector2(1.0f, 0.0f);
                    }
                    else
                    {
                        PenumbraVertices[n + i + 1].TextureCoordinate = (i == 0) ? new Vector2(1.0f, 0.0f) : Vector2.Zero;
                    }
                }

                if (n > 0)
                {
                    var temp = PenumbraVertices[4];
                    PenumbraVertices[4] = PenumbraVertices[5];
                    PenumbraVertices[5] = temp;
                }
            }
        }

        public static List<ConvexHull> GetHullsInRange(Vector2 position, float range, Submarine ParentSub)
        {
            List<ConvexHull> list = new List<ConvexHull>();

            foreach (ConvexHullList chList in HullLists)
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
                        Rectangle subBorders = chList.Submarine.Borders;
                        subBorders.Y -= chList.Submarine.Borders.Height;
                        if (!MathUtils.CircleIntersectsRectangle(lightPos - chList.Submarine.WorldPosition, range, subBorders)) continue;

                        lightPos -= (chList.Submarine.WorldPosition - chList.Submarine.HiddenSubPosition);

                        list.AddRange(chList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox)));
                    }
                }
                else
                {
                    //light is inside, convexhull outside
                    if (chList.Submarine == null)
                    {
                        lightPos += (ParentSub.WorldPosition - ParentSub.HiddenSubPosition);
                        HashSet<RuinGeneration.Ruin> visibleRuins = new HashSet<RuinGeneration.Ruin>();
                        foreach (RuinGeneration.Ruin ruin in Level.Loaded.Ruins)
                        {
                            if (!MathUtils.CircleIntersectsRectangle(lightPos, range, ruin.Area)) { continue; }
                            visibleRuins.Add(ruin);
                        }
                        list.AddRange(chList.List.FindAll(ch => ch.ParentEntity?.ParentRuin != null && visibleRuins.Contains(ch.ParentEntity.ParentRuin)));
                        continue;
                    }
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
        
        public void Remove()
        {
            var chList = HullLists.Find(h => h.Submarine == ParentEntity.Submarine);

            if (chList != null)
            {
                chList.List.Remove(this);
                if (chList.List.Count == 0)
                {
                    HullLists.Remove(chList);
                }
                foreach (ConvexHull ch2 in overlappingHulls)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        ch2.ignoreEdge[i] = false;
                    }
                    ch2.overlappingHulls.Remove(this);
                    foreach (ConvexHull ch in chList.List)
                    {
                        ch.MergeOverlappingSegments(ch2);
                    }
                }
            }
        }
    }
}
