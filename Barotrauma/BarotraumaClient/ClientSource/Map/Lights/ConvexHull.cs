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

        public Vector2 SubmarineDrawPos;

        public Segment(SegmentPoint start, SegmentPoint end, ConvexHull convexHull)
        {
            if (start.Pos.Y > end.Pos.Y)
            {
                (end, start) = (start, end);
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
        private readonly SegmentPoint[] losVertices = new SegmentPoint[2];
        private readonly Vector2[] losOffsets = new Vector2[2];

        private readonly bool isHorizontal;

        private readonly int thickness;

        public VertexPositionColor[] ShadowVertices { get; private set; }
        public VertexPositionTexture[] PenumbraVertices { get; private set; }
        public int ShadowVertexCount { get; private set; }
        public int PenumbraVertexCount { get; private set; }

        /// <summary>
        /// Overrides the maximum distance a LOS vertex can be moved to make it align with a nearby LOS segment
        /// </summary>
        public float? MaxMergeLosVerticesDist;

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

        public ConvexHull(Rectangle rect, bool isHorizontal, MapEntity parent)
        {
            shadowEffect ??= new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    VertexColorEnabled = true
                };
            penumbraEffect ??= new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    TextureEnabled = true,
                    LightingEnabled = false,
                    Texture = TextureLoader.FromFile("Content/Lights/penumbra.png")
                };

            ParentEntity = parent;

            ShadowVertices = new VertexPositionColor[6 * 4];
            PenumbraVertices = new VertexPositionTexture[6 * 4];
            
            BoundingBox = rect;

            this.isHorizontal = isHorizontal;
            if (ParentEntity is Structure structure)
            {
                Debug.Assert(!structure.Removed);
                isHorizontal = structure.IsHorizontal;
            }
            else if (ParentEntity is Item item)
            {
                Debug.Assert(!item.Removed);
                var door = item.GetComponent<Door>();
                if (door != null) { isHorizontal = door.IsHorizontal; }
            }

            Vector2[] verts = new Vector2[]
            {
                new Vector2(rect.X, rect.Bottom),
                new Vector2(rect.Right, rect.Bottom),
                new Vector2(rect.Right, rect.Y),
                new Vector2(rect.X, rect.Y),
            };

            Vector2[] losVerts;
            if (this.isHorizontal)
            {
                thickness = rect.Height;
                losVerts = new Vector2[] { new Vector2(rect.X, rect.Center.Y), new Vector2(rect.Right, rect.Center.Y) };
            }
            else
            {
                thickness = rect.Width;
                losVerts = new Vector2[] { new Vector2(rect.Center.X, rect.Y), new Vector2(rect.Center.X, rect.Bottom) };
            }
            SetVertices(verts, losVerts);
            Enabled = true;

            var chList = HullLists.Find(h => h.Submarine == parent.Submarine);
            if (chList == null)
            {
                chList = new ConvexHullList(parent.Submarine);
                HullLists.Add(chList);
            }                       
            
            foreach (ConvexHull ch in chList.List)
            {
                MergeLosVertices(ch);
                ch.MergeLosVertices(this);
            }

            chList.List.Add(this);
        }

        private void MergeLosVertices(ConvexHull ch, bool refreshOtherOverlappingHulls = true)
        {
            if (ch == this) { return; }

            //merge dist in the direction parallel to the segment
            //(e.g. how far up/down we can stretch a vertical segment)
            float mergeDistParallel = MathHelper.Clamp(ch.thickness * 0.65f, 16, 512);
            if (MaxMergeLosVerticesDist.HasValue)
            {
                mergeDistParallel = Math.Max(mergeDistParallel, MaxMergeLosVerticesDist.Value);
            }
            else
            {
                Rectangle inflatedAABB = ch.BoundingBox;
                inflatedAABB.Inflate(2, 2);
                //if this los segment isn't touching the other's bounding box,
                //don't extend the segment by more than 50% of it's length
                if (!inflatedAABB.Contains(losVertices[0].Pos) && 
                    !inflatedAABB.Contains(losVertices[1].Pos))
                {
                    mergeDistParallel = Math.Min(mergeDistParallel, Vector2.Distance(losVertices[0].Pos, losVertices[1].Pos) * 0.5f);
                }
            }
            //merge dist in the direction perpendicular to the segment
            //(e.g. how far right/left we can stretch a vertical segment)
            //do not allow more than ~half of the thickness, because that'd make the segment go outside the convex hull
            float mergeDistPerpendicular = Math.Min(mergeDistParallel, thickness * 0.35f);

            Vector2 center = (losVertices[0].Pos + losVertices[1].Pos) / 2;

            bool changed = false;
            for (int i = 0; i < losVertices.Length; i++)
            {
                Vector2 segmentDir = Vector2.Normalize(losVertices[i].Pos - center);
                //check if the closest point on the other convex hull segment is close enough, disregarding any offsets
                //otherwise we might end up moving the vertex too much if we stretch it to an already-offset segment
                if (!isCloseEnough(
                        MathUtils.GetClosestPointOnLineSegment(ch.losVertices[0].Pos, ch.losVertices[1].Pos, losVertices[i].Pos),
                        losVertices[i].Pos))
                {
                    continue;
                }

                //check the offset position of the segment next
                Vector2 closest = MathUtils.GetClosestPointOnLineSegment(
                    ch.losVertices[0].Pos + ch.losOffsets[0], 
                    ch.losVertices[1].Pos + ch.losOffsets[1], 
                    losVertices[i].Pos);
                if (!isCloseEnough(closest, losVertices[i].Pos)) { continue; }

                //find where the segments would intersect if they had infinite length
                //   if it's close to the closest point, let's use that instead to keep
                //   the direction of the segment unchanged (i.e. vertical segment stays vertical)
                if (MathUtils.GetLineIntersection(
                    ch.losVertices[0].Pos + ch.losOffsets[0], ch.losVertices[1].Pos + ch.losOffsets[1],
                    losVertices[0].Pos, losVertices[1].Pos,
                    areLinesInfinite: true, out Vector2 intersection) &&
                    //the intersection needs to be outwards from the vertex we're checking
                    Vector2.Dot(segmentDir, intersection - losVertices[i].Pos) > 0 &&
                    //the intersection needs to be close enough to the default position of the vertex and the closest point
                    //(we don't want to merge the segments somewhere close to infinity!)
                    (Vector2.DistanceSquared(intersection, losVertices[i].Pos) < mergeDistParallel * mergeDistParallel ||
                    Vector2.DistanceSquared(intersection, closest) < 16.0f * 16.0f))
                {
                    closest = intersection;
                }

                //don't move the vertices of the segment too close to each other
                if (Vector2.DistanceSquared(losVertices[1 - i].Pos + losOffsets[1 - i], closest) < mergeDistPerpendicular * mergeDistPerpendicular) 
                { 
                    continue; 
                }

                losOffsets[i] = closest - losVertices[i].Pos;
                overlappingHulls.Add(ch);
                ch.overlappingHulls.Add(this);
                changed = true;

                bool isCloseEnough(Vector2 closest, Vector2 vertex)
                {
                    float dist = Vector2.Distance(closest, vertex);
                    if (dist < 0.001f) { return true; }
                    if (dist > mergeDistParallel) { return false; }

                    Vector2 closestDir = (closest - vertex) / dist;

                    float dot = Math.Abs(Vector2.Dot(segmentDir, closestDir));
                    float distAlongAxis = dist * dot;
                    if (distAlongAxis > mergeDistParallel) { return false; }

                    float distPerpendicular = dist * (1.0f - dot);
                    if (distPerpendicular > mergeDistPerpendicular) { return false; }

                    return true;
                }
            }
            if (changed && refreshOtherOverlappingHulls)
            {
                foreach (var overlapping in overlappingHulls)
                {
                    overlapping.MergeLosVertices(this, refreshOtherOverlappingHulls: false);
                }
            }
        }

        public bool LosIntersects(Vector2 pos1, Vector2 pos2)
        {
            return MathUtils.LineSegmentsIntersect(
                losVertices[0].Pos + losOffsets[0], losVertices[1].Pos + losOffsets[1], 
                pos1, pos2);
        }

        public void Rotate(Vector2 origin, float amount)
        {
            Matrix rotationMatrix = 
                Matrix.CreateTranslation(-origin.X, -origin.Y, 0.0f) * 
                Matrix.CreateRotationZ(amount) *
                Matrix.CreateTranslation(origin.X, origin.Y, 0.0f);
            SetVertices(vertices.Select(v => v.Pos).ToArray(), losVertices.Select(v => v.Pos).ToArray(), rotationMatrix: rotationMatrix);
        }

        private void CalculateDimensions()
        {
            float minX = vertices[0].Pos.X, minY = vertices[0].Pos.Y, maxX = vertices[0].Pos.X, maxY = vertices[0].Pos.Y;

            for (int i = 1; i < vertices.Length; i++)
            {
                minX = Math.Min(minX, vertices[i].Pos.X);
                minY = Math.Min(minY, vertices[i].Pos.Y);
                maxX = Math.Max(maxX, vertices[i].Pos.X);
                maxY = Math.Max(maxY, vertices[i].Pos.Y);
            }

            BoundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }
                
        public void Move(Vector2 amount)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].Pos         += amount;
                segments[i].Start.Pos   += amount;
                segments[i].End.Pos     += amount;
            }
            for (int i = 0; i < losVertices.Length; i++)
            {
                losVertices[i].Pos += amount;
            }

            LastVertexChangeTime = (float)Timing.TotalTime;

            overlappingHulls.Clear();

            CalculateDimensions();

            if (ParentEntity == null) { return; }

            var chList = HullLists.Find(h => h.Submarine == ParentEntity.Submarine);
            if (chList != null)
            {
                overlappingHulls.Clear();
                foreach (ConvexHull ch in chList.List)
                {
                    MergeLosVertices(ch);
                    ch.MergeLosVertices(this);
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
                    for (int i = 0; i < ch.losOffsets.Length; i++)
                    {
                        ch.losOffsets[i] = Vector2.Zero;
                    }
                }
                for (int i = 0; i < chList.List.Count; i++)
                {
                    for (int j = i + 1; j < chList.List.Count; j++)
                    {
                        chList.List[i].MergeLosVertices(chList.List[j]);
                        chList.List[j].MergeLosVertices(chList.List[i]);
                    }
                }
            }
        }

        public void SetVertices(Vector2[] points, Vector2[] losPoints, bool mergeOverlappingSegments = true, Matrix? rotationMatrix = null)
        {
            Debug.Assert(points.Length == 4, "Only rectangular convex hulls are supported");

            LastVertexChangeTime = (float)Timing.TotalTime;

            for (int i = 0; i < 4; i++)
            {
                vertices[i] = new SegmentPoint(points[i], this);
            }
            for (int i = 0; i < 2; i++)
            {
                losVertices[i] = new SegmentPoint(losPoints[i], this);
                losOffsets[i] = Vector2.Zero;
            }

            overlappingHulls.Clear();

            if (rotationMatrix.HasValue)
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i].Pos = Vector2.Transform(vertices[i].Pos, rotationMatrix.Value);
                }
                for (int i = 0; i < losVertices.Length; i++)
                {
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
                        MergeLosVertices(ch);
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
        public void GetVisibleSegments(Vector2 viewPosition, List<Segment> visibleSegments)
        {            
            for (int i = 0; i < 4; i++)
            {
                if (IsSegmentFacing(vertices[i].WorldPos, vertices[(i + 1) % 4].WorldPos, viewPosition))
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

            for (int i = 0; i < losVertices.Length; i++)
            {
                int currentIndex = i;
                int nextIndex = (currentIndex + 1) % 2;
                Vector3 vertexPos0 = new Vector3(losVertices[currentIndex].Pos + losOffsets[currentIndex], 0.0f);
                Vector3 vertexPos1 = new Vector3(losVertices[nextIndex].Pos + losOffsets[nextIndex], 0.0f);

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

            if (IsSegmentFacing(losVertices[0].Pos, losVertices[1].Pos, lightSourcePos))
            {
                Array.Reverse(ShadowVertices, 0, ShadowVertexCount);
            }

            CalculateLosPenumbraVertices(lightSourcePos);
        }

        private static bool IsSegmentFacing(Vector2 segmentPos1, Vector2 segmentPos2, Vector2 viewPosition)
        {
            Vector2 segmentMid = (segmentPos1 + segmentPos2) / 2;
            Vector2 segmentDiff = segmentPos2 - segmentPos1;
            Vector2 segmentNormal = new Vector2(-segmentDiff.Y, segmentDiff.X);

            Vector2 viewDirection = viewPosition - segmentMid;
            return Vector2.Dot(segmentNormal, viewDirection) > 0;
        }

        private void CalculateLosPenumbraVertices(Vector2 lightSourcePos)
        {
            Vector3 offset = Vector3.Zero;
            if (ParentEntity != null && ParentEntity.Submarine != null)
            {
                offset = new Vector3(ParentEntity.Submarine.DrawPosition.X, ParentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            PenumbraVertexCount = 0;
            for (int i = 0; i < losVertices.Length; i++)
            {
                int currentIndex = i;
                int nextIndex = (i + 1) % 2;
                Vector2 vertexPos0 = losVertices[currentIndex].Pos + losOffsets[currentIndex];
                Vector2 vertexPos1 = losVertices[nextIndex].Pos + losOffsets[nextIndex];

                if (Vector2.DistanceSquared(vertexPos0, vertexPos1) < 1.0f) { continue; }
                      
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
                
                penumbraStart = new Vector3(vertexPos1, 0.0f);

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

        public void DebugDraw(SpriteBatch spriteBatch)
        {
            //RecalculateAll(Submarine.MainSub);
            //RefreshWorldPositions();

            DrawLine(losVertices[0].Pos, losVertices[1].Pos, Color.Gray * 0.5f, width: 3);
            DrawLine(losVertices[0].Pos + losOffsets[0], losVertices[1].Pos + losOffsets[1], Color.LightGreen, width: 2);
            DrawLine(GameMain.GameScreen.Cam.Position + Vector2.One * 1000, GameMain.GameScreen.Cam.Position - Vector2.One * 1000, Color.Magenta, width: 2);

            if (GameMain.LightManager.LightingEnabled)
            {
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector2 start = vertices[i].Pos;
                    Vector2 end = vertices[(i + 1) % 4].Pos;
                    DrawLine(
                       start,
                       end, Color.Yellow * 0.5f,
                       width: 4);
                }
            }

            void DrawLine(Vector2 vertexPos0, Vector2 vertexPos1, Color color, int width)
            {
                if (ParentEntity != null && ParentEntity.Submarine != null)
                {
                    vertexPos0 += ParentEntity.Submarine.DrawPosition;
                    vertexPos1 += ParentEntity.Submarine.DrawPosition;
                }
                float alpha = 1.0f;
                if (LightManager.ViewTarget != null)
                {
                    alpha = IsSegmentFacing(vertexPos0, vertexPos1, LightManager.ViewTarget.WorldPosition) ? 1.0f : 0.5f;
                }
                vertexPos0.Y = -vertexPos0.Y;
                vertexPos1.Y = -vertexPos1.Y;
                GUI.DrawLine(spriteBatch, vertexPos0, vertexPos1, color * alpha, width: width);
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
                //create a new list because MergeLosVertices can edit overlappingHulls
                foreach (ConvexHull ch2 in overlappingHulls.ToList())
                {
                    ch2.overlappingHulls.Remove(this);
                    foreach (ConvexHull ch in chList.List)
                    {
                        ch.MergeLosVertices(ch2);
                    }
                }
            }
        }
    }
}
