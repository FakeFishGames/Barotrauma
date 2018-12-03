using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

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
        
        Segment[] segments = new Segment[4];
        SegmentPoint[] vertices = new SegmentPoint[4];
        SegmentPoint[] losVertices = new SegmentPoint[4];
        
        private bool[] backFacing;
        private bool[] ignoreEdge;

        private VertexPositionColor[] shadowVertices;
        private VertexPositionTexture[] penumbraVertices;

        public VertexPositionColor[] ShadowVertices
        {
            get { return shadowVertices; }
        }

        public VertexPositionTexture[] PenumbraVertices
        {
            get { return penumbraVertices; }
        }

        public int shadowVertexCount;

        private Entity parentEntity;

        private Rectangle boundingBox;

        public Entity ParentEntity
        {
            get { return parentEntity; }

        }

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

        public Rectangle BoundingBox
        {
            get { return boundingBox; }
        }
                
        public ConvexHull(Vector2[] points, Color color, Entity parent)
        {
            if (shadowEffect == null)
            {
                shadowEffect = new BasicEffect(GameMain.Instance.GraphicsDevice);
                shadowEffect.VertexColorEnabled = true;
            }
            if (penumbraEffect == null)
            {
                penumbraEffect = new BasicEffect(GameMain.Instance.GraphicsDevice);
                penumbraEffect.TextureEnabled = true;
                //shadowEffect.VertexColorEnabled = true;
                penumbraEffect.LightingEnabled = false;
                penumbraEffect.Texture = TextureLoader.FromFile("Content/Lights/penumbra.png");
            }

            parentEntity = parent;

            //cachedShadows = new Dictionary<LightSource, CachedShadow>();
            
            shadowVertices = new VertexPositionColor[6 * 2];
            penumbraVertices = new VertexPositionTexture[6];
            
            //vertices = points;
            SetVertices(points);
            //CalculateDimensions();
            
            backFacing = new bool[4];
            ignoreEdge = new bool[4];
                        
            Enabled = true;

            var chList = HullLists.Find(x => x.Submarine == parent.Submarine);
            if (chList == null)
            {
                chList = new ConvexHullList(parent.Submarine);
                HullLists.Add(chList);
            }                       
            
            foreach (ConvexHull ch in chList.List)
            {
                UpdateIgnoredEdges(ch);
                ch.UpdateIgnoredEdges(this);
            }

            chList.List.Add(this);
        }

        private void UpdateIgnoredEdges(ConvexHull ch)
        {
            if (ch == this) return;
            //ignore edges that are inside some other convex hull
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].Pos.X >= ch.boundingBox.X && vertices[i].Pos.X <= ch.boundingBox.Right && 
                    vertices[i].Pos.Y >= ch.boundingBox.Y && vertices[i].Pos.Y <= ch.boundingBox.Bottom)
                {
                    Vector2 p = vertices[(i + 1) % vertices.Length].Pos;

                    if (p.X >= ch.boundingBox.X && p.X <= ch.boundingBox.Right && 
                        p.Y >= ch.boundingBox.Y && p.Y <= ch.boundingBox.Bottom)
                    {
                        ignoreEdge[i] = true;
                    }
                }
            }
        }

        public void Rotate(Vector2 origin, float amount)
        {
            Matrix rotationMatrix = Matrix.CreateRotationZ(amount);

            Vector2[] newVerts = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                newVerts[i] = Vector2.Transform(vertices[i].Pos - origin, rotationMatrix) + origin;
            }

            SetVertices(newVerts);
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

            boundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
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

            CalculateDimensions();
        }

        public void SetVertices(Vector2[] points)
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
                segments[i] = new Segment(vertices[i], vertices[(i + 1) % 4], this);
            }
            
            int margin = 0;

            if (Math.Abs(points[0].X - points[2].X) < Math.Abs(points[0].Y - points[1].Y))
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

            CalculateDimensions();

            if (parentEntity == null || ignoreEdge == null) return;
            for (int i = 0; i<4; i++)
            {
                ignoreEdge[i] = false;
            }

            var chList = HullLists.Find(x => x.Submarine == parentEntity.Submarine);
            if (chList != null)
            {
                foreach (ConvexHull ch in chList.List)
                {
                    UpdateIgnoredEdges(ch);
                }
            }
        }

        /*private void RemoveCachedShadow(Lights.LightSource light)
        {
            CachedShadow shadow = null;
            cachedShadows.TryGetValue(light, out shadow);

            if (shadow != null)
            {
                shadow.Dispose();
                cachedShadows.Remove(light);
            }
        }

        private void ClearCachedShadows()
        {
            foreach (KeyValuePair<LightSource, CachedShadow> cachedShadow in cachedShadows)
            {
                cachedShadow.Key.NeedsHullUpdate = true;
                cachedShadow.Value.Dispose();
            }
            cachedShadows.Clear();
        }*/

        public bool Intersects(Rectangle rect)
        {
            if (!Enabled) return false;

            Rectangle transformedBounds = boundingBox;
            if (parentEntity != null && parentEntity.Submarine != null)
            {
                transformedBounds.X += (int)parentEntity.Submarine.Position.X;
                transformedBounds.Y += (int)parentEntity.Submarine.Position.Y;
            }
            return transformedBounds.Intersects(rect);
        }
        
        /// <summary>
        /// Returns the segments that are facing towards viewPosition
        /// </summary>
        public List<Segment> GetVisibleSegments(Vector2 viewPosition)
        {
            List<Segment> visibleFaces = new List<Segment>();
            
            for (int i = 0; i < 4; i++)
            {
                if (ignoreEdge[i]) continue;

                Vector2 pos1 = vertices[i].WorldPos;
                Vector2 pos2 = vertices[(i + 1) % 4].WorldPos;

                Vector2 middle = (pos1 + pos2) / 2;

                Vector2 L = viewPosition - middle;

                Vector2 N = new Vector2(
                    -(pos2.Y - pos1.Y),
                    pos2.X - pos1.X);

                if (Vector2.Dot(N, L) > 0)
                {
                    visibleFaces.Add(segments[i]);
                }
            }

            return visibleFaces;
        }


        public void RefreshWorldPositions()
        {
            if (parentEntity == null || parentEntity.Submarine == null) return;
            for (int i = 0; i < 4; i++)
            {
                vertices[i].WorldPos = vertices[i].Pos + parentEntity.Submarine.DrawPosition;
                segments[i].Start.WorldPos = segments[i].Start.Pos + parentEntity.Submarine.DrawPosition;
                segments[i].End.WorldPos = segments[i].End.Pos + parentEntity.Submarine.DrawPosition;

            }
        }

        public void CalculateShadowVertices(Vector2 lightSourcePos, bool los = true)
        {
            Vector3 offset = Vector3.Zero;
            if (parentEntity != null && parentEntity.Submarine != null)
            {
                offset = new Vector3(parentEntity.Submarine.DrawPosition.X, parentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            shadowVertexCount = 0;

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
            int startingIndex = 0;
            int endingIndex = 0;
            for (int i = 0; i < 4; i++)
            {
                int currentEdge = i;
                int nextEdge = (i + 1) % 4;

                if (backFacing[currentEdge] && !backFacing[nextEdge])
                    endingIndex = nextEdge;

                if (!backFacing[currentEdge] && backFacing[nextEdge])
                    startingIndex = nextEdge;
            }

            //nr of vertices that are in the shadow
            if (endingIndex > startingIndex)
                shadowVertexCount = endingIndex - startingIndex + 1;
            else
                shadowVertexCount = 4 + 1 - startingIndex + endingIndex;

            //shadowVertices = new VertexPositionColor[shadowVertexCount * 2];

            //create a triangle strip that has the shape of the shadow
            int currentIndex = startingIndex;
            int svCount = 0;
            while (svCount != shadowVertexCount * 2)
            {
                Vector3 vertexPos = new Vector3(vertices[currentIndex].Pos, 0.0f);

                int i = los ? svCount : svCount + 1;
                int j = los ? svCount + 1 : svCount;

                //one vertex on the hull
                shadowVertices[i] = new VertexPositionColor();
                shadowVertices[i].Color = los ? Color.Black : Color.Transparent;
                shadowVertices[i].Position = vertexPos+ offset;

                //one extruded by the light direction
                shadowVertices[j] = new VertexPositionColor();
                shadowVertices[j].Color = shadowVertices[i].Color;

                Vector3 L2P = vertexPos - new Vector3(lightSourcePos, 0);
                L2P.Normalize();
                
                shadowVertices[j].Position = new Vector3(lightSourcePos, 0) + L2P * 9000 + offset;

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
            Vector3 offset = Vector3.Zero;
            if (parentEntity != null && parentEntity.Submarine != null)
            {
                offset = new Vector3(parentEntity.Submarine.DrawPosition.X, parentEntity.Submarine.DrawPosition.Y, 0.0f);
            }

            for (int n = 0; n < 4; n += 3)
            {
                Vector3 penumbraStart = new Vector3((n == 0) ? vertices[startingIndex].Pos : vertices[endingIndex].Pos, 0.0f);

                penumbraVertices[n] = new VertexPositionTexture();
                penumbraVertices[n].Position = penumbraStart + offset;
                penumbraVertices[n].TextureCoordinate = new Vector2(0.0f, 1.0f);
                //penumbraVertices[0].te = fow ? Color.Black : Color.Transparent;

                for (int i = 0; i < 2; i++)
                {
                    penumbraVertices[n + i + 1] = new VertexPositionTexture();
                    Vector3 vertexDir = penumbraStart - new Vector3(lightSourcePos, 0);
                    vertexDir.Normalize();

                    Vector3 normal = (i == 0) ? new Vector3(-vertexDir.Y, vertexDir.X, 0.0f) : new Vector3(vertexDir.Y, -vertexDir.X, 0.0f) * 0.05f;
                    if (n > 0) normal = -normal;

                    vertexDir = penumbraStart - (new Vector3(lightSourcePos, 0) - normal * 20.0f);
                    vertexDir.Normalize();
                    penumbraVertices[n + i + 1].Position = new Vector3(lightSourcePos, 0) + vertexDir * 9000 + offset;

                    if (los)
                    {
                        penumbraVertices[n + i + 1].TextureCoordinate = (i == 0) ? new Vector2(0.05f, 0.0f) : new Vector2(1.0f, 0.0f);
                    }
                    else
                    {
                        penumbraVertices[n + i + 1].TextureCoordinate = (i == 0) ? new Vector2(1.0f, 0.0f) : Vector2.Zero;
                    }
                }

                if (n > 0)
                {
                    var temp = penumbraVertices[4];
                    penumbraVertices[4] = penumbraVertices[5];
                    penumbraVertices[5] = temp;
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
        
        public void Remove()
        {
            var chList = HullLists.Find(x => x.Submarine == parentEntity.Submarine);

            if (chList != null)
            {
                chList.List.Remove(this);
                if (chList.List.Count == 0)
                {
                    HullLists.Remove(chList);
                }
            }
        }
    }

}
