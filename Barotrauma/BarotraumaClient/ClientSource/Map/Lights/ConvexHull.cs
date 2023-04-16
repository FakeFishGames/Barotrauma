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

        public readonly Submarine Submarine;
        public HashSet<ConvexHull> IsHidden = new HashSet<ConvexHull>();
        public readonly List<ConvexHull> List = new List<ConvexHull>();

        public ConvexHullList(Submarine submarine)
        {
            Submarine = submarine;
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

    class VectorPair
    {
        public Vector2? A = null;
        public Vector2? B = null;
    }

    class ConvexHull
    {
        public static List<ConvexHullList> HullLists = new List<ConvexHullList>();
        public static BasicEffect shadowEffect;
        public static BasicEffect penumbraEffect;

        private readonly Segment[] segments = new Segment[4];
        private readonly SegmentPoint[] vertices = new SegmentPoint[4];
        private readonly SegmentPoint[] losVertices = new SegmentPoint[4];
        private readonly VectorPair[] losOffsets = new VectorPair[4];
        
        private readonly bool[] backFacing;
        private readonly bool[] ignoreEdge;

        private readonly bool isHorizontal;

        public VertexPositionColor[] ShadowVertices { get; private set; }
        public VertexPositionTexture[] PenumbraVertices { get; private set; }
        public int ShadowVertexCount { get; private set; }
        public int PenumbraVertexCount { get; private set; }

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

            ShadowVertices = new VertexPositionColor[6 * 4];
            PenumbraVertices = new VertexPositionTexture[6 * 4];
            
            backFacing = new bool[4];
            ignoreEdge = new bool[4];

            float minX = points[0].X, minY = points[0].Y, maxX = points[0].X, maxY = points[0].Y;

            for (int i = 1; i < vertices.Length; i++)
            {
                if (points[i].X < minX) minX = points[i].X;
                if (points[i].Y < minY) minY = points[i].Y;

                if (points[i].X > maxX) maxX = points[i].X;
                if (points[i].Y > minY) maxY = points[i].Y;
            }

            BoundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));

            isHorizontal = BoundingBox.Width > BoundingBox.Height;
            if (ParentEntity is Structure structure)
            {
                System.Diagnostics.Debug.Assert(!structure.Removed);
                isHorizontal = structure.IsHorizontal;
            }
            else if (ParentEntity is Item item)
            {
                System.Diagnostics.Debug.Assert(!item.Removed);
                var door = item.GetComponent<Door>();
                if (door != null) { isHorizontal = door.IsHorizontal; }
            }

            SetVertices(points);          
            
            Enabled = true;

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
                //hide segments that are roughly at the some position as some other segment (e.g. the ends of two adjacent wall pieces)
                float mergeDist = 16;
                float mergeDistSqr = mergeDist * mergeDist;

                Rectangle intersection = Rectangle.Intersect(BoundingBox, ch.BoundingBox);
                int intersectionArea = intersection.Width * intersection.Height;
                int bboxArea = BoundingBox.Width * BoundingBox.Height;
                int otherBboxArea = ch.BoundingBox.Width * ch.BoundingBox.Height;
                if (Math.Abs(intersectionArea - bboxArea) < mergeDistSqr) { return; }
                if (Math.Abs(intersectionArea - otherBboxArea) < mergeDistSqr) { return; }

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

            for (int i = 0; i < segments.Length; i++)
            {
                if (ignoreEdge[i]) { continue; }
                if (Vector2.DistanceSquared(segments[i].Start.Pos, segments[i].End.Pos) < 1.0f) { continue; }
                for (int j = 0; j < ch.segments.Length; j++)
                {
                    if (ch.ignoreEdge[j]) { continue; }
                    if (Vector2.DistanceSquared(ch.segments[j].Start.Pos, ch.segments[j].End.Pos) < 1.0f) { continue; }
                    if (IsSegmentAInB(segments[i], ch.segments[j]))
                    {
                        ignoreEdge[i] = true;
                        if (Vector2.DistanceSquared(ch.segments[j].Start.Pos, segments[i].Start.Pos) < 4.0f)
                        {
                            ch.ShiftSegmentPoint(j, false, segments[i].End.Pos);
                        }
                        else if (Vector2.DistanceSquared(ch.segments[j].Start.Pos, segments[i].End.Pos) < 4.0f)
                        {
                            ch.ShiftSegmentPoint(j, false, segments[i].Start.Pos);
                        }

                        if (Vector2.DistanceSquared(ch.segments[j].End.Pos, segments[i].Start.Pos) < 4.0f)
                        {
                            ch.ShiftSegmentPoint(j, true, segments[i].End.Pos);
                        }
                        else if (Vector2.DistanceSquared(ch.segments[j].End.Pos, segments[i].End.Pos) < 4.0f)
                        {
                            ch.ShiftSegmentPoint(j, true, segments[i].Start.Pos);
                        }
                    }
                    else if (IsSegmentAInB(ch.segments[j], segments[i]))
                    {
                        ch.ignoreEdge[j] = true;

                        if (Vector2.DistanceSquared(segments[i].Start.Pos, ch.segments[j].Start.Pos) < 4.0f)
                        {
                            ShiftSegmentPoint(i, false, ch.segments[j].End.Pos);
                        }
                        else if (Vector2.DistanceSquared(segments[i].Start.Pos, ch.segments[j].End.Pos) < 4.0f)
                        {
                            ShiftSegmentPoint(i, false, ch.segments[j].Start.Pos);
                        }

                        if (Vector2.DistanceSquared(segments[i].End.Pos, ch.segments[j].Start.Pos) < 4.0f)
                        {
                            ShiftSegmentPoint(i, true, ch.segments[j].End.Pos);
                        }
                        else if (Vector2.DistanceSquared(segments[i].End.Pos, ch.segments[j].End.Pos) < 4.0f)
                        {
                            ShiftSegmentPoint(i, true, ch.segments[j].Start.Pos);
                        }
                    }
                }
            }
            
            //ignore edges that are inside some other convex hull
            for (int i = 0; i < vertices.Length; i++)
            {
                if (ch.IsPointInside(vertices[i].Pos))
                {
                    if (ch.IsPointInside(vertices[(i + 1) % vertices.Length].Pos))
                    {
                        ignoreEdge[i] = true;
                        overlappingHulls.Add(ch);
                    }
                }
            }
        }

        private void ShiftSegmentPoint(int segmentIndex, bool end, Vector2 newPos)
        {
            var segment = segments[segmentIndex];

            losOffsets[segmentIndex] ??= new VectorPair();
            bool flipped = false;
            if (Vector2.DistanceSquared(vertices[segmentIndex].Pos, segment.Start.Pos) > Vector2.DistanceSquared(vertices[segmentIndex].Pos, segment.End.Pos))
            {
                flipped = true;
            }
            if (end == !flipped)
            {
                losOffsets[segmentIndex].B = newPos;
            }
            else
            {
                losOffsets[segmentIndex].A = newPos;
            }
        }

        public static bool IsSegmentAInB(Segment a, Segment b)
        {
            if (Vector2.DistanceSquared(a.Start.Pos, a.End.Pos) > Vector2.DistanceSquared(b.Start.Pos, b.End.Pos))
            {
                return false;
            }

            Vector2 min = new Vector2(Math.Min(b.Start.Pos.X, b.End.Pos.X), Math.Min(b.Start.Pos.Y, b.End.Pos.Y));
            min.X -= 1.0f; min.Y -= 1.0f;

            if (a.Start.Pos.X < min.X) { return false; }
            if (a.Start.Pos.Y < min.Y) { return false; }
            if (a.End.Pos.X < min.X) { return false; }
            if (a.End.Pos.Y < min.Y) { return false; }

            Vector2 max = new Vector2(Math.Max(b.Start.Pos.X, b.End.Pos.X), Math.Max(b.Start.Pos.Y, b.End.Pos.Y));
            max.X += 1.0f; max.Y += 1.0f;

            if (a.Start.Pos.X > max.X) { return false; }
            if (a.Start.Pos.Y > max.Y) { return false; }
            if (a.End.Pos.X > max.X) { return false; }
            if (a.End.Pos.Y > max.Y) { return false; }

            float startDist = MathUtils.LineToPointDistanceSquared(b.Start.Pos, b.End.Pos, a.Start.Pos);
            if (startDist > 1.0f) { return false; }
            float endDist = MathUtils.LineToPointDistanceSquared(b.Start.Pos, b.End.Pos, a.End.Pos);
            if (endDist > 1.0f) { return false; }
            return true;
        }

        public bool IsPointInside(Vector2 point)
        {
            if (!BoundingBox.Contains(point)) { return false; }

            Vector2 center = (vertices[0].Pos + vertices[1].Pos + vertices[2].Pos + vertices[3].Pos) * 0.25f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 segmentVector = vertices[(i + 1) % 4].Pos - vertices[i].Pos;
                Vector2 centerToVertex = center - vertices[i].Pos;
                Vector2 pointToVertex = point - vertices[i].Pos;

                float dotCenter = Vector2.Dot(centerToVertex, segmentVector);
                float dotPoint = Vector2.Dot(pointToVertex, segmentVector);

                if ((dotCenter > 0f && dotPoint < 0f) || (dotCenter < 0f && dotPoint > 0f)) { return false; }
            }

            return true;
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
            SetVertices(vertices.Select(v => v.Pos).ToArray(), rotationMatrix: rotationMatrix);
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

                losOffsets[i] = null;

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

        public void SetVertices(Vector2[] points, bool mergeOverlappingSegments = true, Matrix? rotationMatrix = null)
        {
            Debug.Assert(points.Length == 4, "Only rectangular convex hulls are supported");

            LastVertexChangeTime = (float)Timing.TotalTime;

            for (int i = 0; i < 4; i++)
            {
                vertices[i]     = new SegmentPoint(points[i], this);
                losVertices[i]  = new SegmentPoint(points[i], this);
                losOffsets[i] = null;
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

            if (ParentEntity == null) { return; }

            if (mergeOverlappingSegments)
            {
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
                if (ignoreEdge[i] && ignoreEdges) { continue; }

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

        public void CalculateLosVertices(Vector2 lightSourcePos)
        {
            Vector3 offset = Vector3.Zero;
            if (ParentEntity != null && ParentEntity.Submarine != null)
            {
                offset = new Vector3(ParentEntity.Submarine.DrawPosition.X, ParentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            ShadowVertexCount = 0;

            //compute facing of each edge, using N*L
            for (int i = 0; i < 4; i++)
            {
                if (ignoreEdge[i])
                {
                    backFacing[i] = false;
                    continue;
                }

                Vector2 firstVertex = losVertices[i].Pos;
                Vector2 secondVertex = losVertices[(i+1) % 4].Pos;

                Vector2 L = lightSourcePos - ((firstVertex + secondVertex) / 2.0f);

                Vector2 N = new Vector2(
                    -(secondVertex.Y - firstVertex.Y),
                    secondVertex.X - firstVertex.X);

                backFacing[i] = (Vector2.Dot(N, L) < 0);
            }

            ShadowVertexCount = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!backFacing[i]) { continue; }
                int currentIndex = i;
                Vector3 vertexPos0 = new Vector3(losOffsets[currentIndex]?.A ?? losVertices[currentIndex].Pos, 0.0f);
                Vector3 vertexPos1 = new Vector3(losOffsets[currentIndex]?.B ?? losVertices[(currentIndex + 1) % 4].Pos, 0.0f);

                if (Vector3.DistanceSquared(vertexPos0, vertexPos1) < 1.0f) { continue; }

                Vector3 L2P0 = vertexPos0 - new Vector3(lightSourcePos, 0);
                L2P0.Normalize();
                Vector3 extruded0 = new Vector3(lightSourcePos, 0) + L2P0 * 9000;

                Vector3 L2P1 = vertexPos1 - new Vector3(lightSourcePos, 0);
                L2P1.Normalize();
                Vector3 extruded1 = new Vector3(lightSourcePos, 0) + L2P1 * 9000;

                ShadowVertices[ShadowVertexCount + 0] = new VertexPositionColor
                {
                    Color = Color.Black,
                    Position = vertexPos1 + offset
                };

                ShadowVertices[ShadowVertexCount + 1] = new VertexPositionColor
                {
                    Color = Color.Black,
                    Position = vertexPos0 + offset
                };

                ShadowVertices[ShadowVertexCount + 2] = new VertexPositionColor
                {
                    Color = Color.Black,
                    Position = extruded0 + offset
                };

                ShadowVertices[ShadowVertexCount + 3] = new VertexPositionColor
                {
                    Color = Color.Black,
                    Position = vertexPos1 + offset
                };

                ShadowVertices[ShadowVertexCount + 4] = new VertexPositionColor
                {
                    Color = Color.Black,
                    Position = extruded0 + offset
                };

                ShadowVertices[ShadowVertexCount + 5] = new VertexPositionColor
                {
                    Color = Color.Black,
                    Position = extruded1 + offset
                };

                ShadowVertexCount += 6;
            }

            CalculateLosPenumbraVertices(lightSourcePos);
        }

        private void CalculateLosPenumbraVertices(Vector2 lightSourcePos)
        {
            Vector3 offset = Vector3.Zero;
            if (ParentEntity != null && ParentEntity.Submarine != null)
            {
                offset = new Vector3(ParentEntity.Submarine.DrawPosition.X, ParentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            PenumbraVertexCount = 0;
            for (int i = 0; i < 4; i++)
            {
                int currentIndex = i;
                int prevIndex = (i + 3) % 4;
                int nextIndex = (i + 1) % 4;
                bool disjointed = losOffsets[i]?.A != null;
                Vector2 vertexPos0 = losOffsets[currentIndex]?.A ?? losVertices[currentIndex].Pos;
                Vector2 vertexPos1 = losOffsets[currentIndex]?.B ?? losVertices[nextIndex].Pos;

                if (Vector2.DistanceSquared(vertexPos0, vertexPos1) < 1.0f) { continue; }

                if (backFacing[currentIndex] && (disjointed || (!backFacing[prevIndex])))
                {
                    Vector3 penumbraStart = new Vector3(vertexPos0, 0.0f);

                    PenumbraVertices[PenumbraVertexCount] = new VertexPositionTexture
                    {
                        Position = penumbraStart + offset,
                        TextureCoordinate = new Vector2(0.0f, 1.0f)
                    };

                    for (int j = 0; j < 2; j++)
                    {
                        PenumbraVertices[PenumbraVertexCount + j + 1] = new VertexPositionTexture();
                        Vector3 vertexDir = penumbraStart - new Vector3(lightSourcePos, 0);
                        vertexDir.Normalize();

                        Vector3 normal = (j == 0) ? new Vector3(-vertexDir.Y, vertexDir.X, 0.0f) : new Vector3(vertexDir.Y, -vertexDir.X, 0.0f) * 0.05f;

                        vertexDir = penumbraStart - (new Vector3(lightSourcePos, 0) - normal * 20.0f);
                        vertexDir.Normalize();
                        PenumbraVertices[PenumbraVertexCount + j + 1].Position = new Vector3(lightSourcePos, 0) + vertexDir * 9000 + offset;

                        PenumbraVertices[PenumbraVertexCount + j + 1].TextureCoordinate = (j == 0) ? new Vector2(0.05f, 0.0f) : new Vector2(1.0f, 0.0f);
                    }

                    PenumbraVertexCount += 3;
                }

                disjointed = losOffsets[i]?.B != null;
                if (backFacing[currentIndex] && (disjointed || (!backFacing[nextIndex])))
                {
                    Vector3 penumbraStart = new Vector3(vertexPos1, 0.0f);

                    PenumbraVertices[PenumbraVertexCount] = new VertexPositionTexture
                    {
                        Position = penumbraStart + offset,
                        TextureCoordinate = new Vector2(0.0f, 1.0f)
                    };

                    for (int j = 0; j < 2; j++)
                    {
                        PenumbraVertices[PenumbraVertexCount + (1 - j) + 1] = new VertexPositionTexture();
                        Vector3 vertexDir = penumbraStart - new Vector3(lightSourcePos, 0);
                        vertexDir.Normalize();

                        Vector3 normal = (j == 0) ? new Vector3(-vertexDir.Y, vertexDir.X, 0.0f) : new Vector3(vertexDir.Y, -vertexDir.X, 0.0f) * 0.05f;

                        vertexDir = penumbraStart - (new Vector3(lightSourcePos, 0) + normal * 20.0f);
                        vertexDir.Normalize();
                        PenumbraVertices[PenumbraVertexCount + (1 - j) + 1].Position = new Vector3(lightSourcePos, 0) + vertexDir * 9000 + offset;

                        PenumbraVertices[PenumbraVertexCount + (1 - j) + 1].TextureCoordinate = (j == 0) ? new Vector2(0.05f, 0.0f) : new Vector2(1.0f, 0.0f);
                    }

                    PenumbraVertexCount += 3;
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
                        if (!MathUtils.CircleIntersectsRectangle(lightPos - chList.Submarine.WorldPosition, range, subBorders)) { continue; }

                        lightPos -= chList.Submarine.WorldPosition - chList.Submarine.HiddenSubPosition;

                        list.AddRange(chList.List.FindAll(ch => MathUtils.CircleIntersectsRectangle(lightPos, range, ch.BoundingBox)));
                    }
                }
                else
                {
                    //light is inside, convexhull outside
                    if (chList.Submarine == null)
                    {
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
