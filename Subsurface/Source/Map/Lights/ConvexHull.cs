using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Lights
{
    class CachedShadow
    {

        public VertexPositionColor[] ShadowVertices;
        public VertexPositionTexture[] PenumbraVertices;

        public Vector2 LightPos;

        public CachedShadow(VertexPositionColor[] shadowVertices, VertexPositionTexture[] penumbraVertices, Vector2 lightPos)
        {
            ShadowVertices = shadowVertices;
            PenumbraVertices = penumbraVertices;

            LightPos = lightPos;
        }
    }

    class ConvexHull
    {
        public static List<ConvexHull> list = new List<ConvexHull>();
        static BasicEffect shadowEffect;
        static BasicEffect penumbraEffect;

        private Dictionary<LightSource, CachedShadow> cachedShadows;
                
        private Vector2[] vertices;
        private int primitiveCount;

        private bool[] backFacing;

        private VertexPositionColor[] shadowVertices;
        private VertexPositionTexture[] penumbraVertices;

        private Rectangle boundingBox;

        public bool Enabled
        {
            get;
            set;
        }

        public Rectangle BoundingBox
        {
            get { return boundingBox; }
        }
                
        public ConvexHull(Vector2[] points, Color color)
        {
            if (shadowEffect == null)
            {
                shadowEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                shadowEffect.VertexColorEnabled = true;
            }
            if (penumbraEffect == null)
            {
                penumbraEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                penumbraEffect.TextureEnabled = true;
                //shadowEffect.VertexColorEnabled = true;
                penumbraEffect.LightingEnabled = false;
                penumbraEffect.Texture = TextureLoader.FromFile("Content/Lights/penumbra.png");
            }

            cachedShadows = new Dictionary<LightSource, CachedShadow>();
            
            vertices = points;
            primitiveCount = vertices.Length;

            CalculateDimensions();
            
            backFacing = new bool[primitiveCount];
            
            Enabled = true;

            list.Add(this);
        }

        private void CalculateDimensions()
        {
            Vector2 center = Vector2.Zero;

            float? minX = null, minY = null, maxX = null, maxY = null;

            for (int i = 0; i < vertices.Length; i++)
            {
                center += vertices[i];

                if (minX == null || vertices[i].X < minX) minX = vertices[i].X;
                if (minY == null || vertices[i].Y < minY) minY = vertices[i].Y;

                if (maxX == null || vertices[i].X > maxX) maxX = vertices[i].X;
                if (maxY == null || vertices[i].Y > minY) maxY = vertices[i].Y;
            }
            center /= vertices.Length;

            boundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }
                
        public void Move(Vector2 amount)
        {
            cachedShadows.Clear();

            for (int i = 0; i < vertices.Count(); i++)
            {
                vertices[i] += amount;
            }

            CalculateDimensions();
        }

        public void SetVertices(Vector2[] points)
        {
            cachedShadows.Clear();

            vertices = points;
        }

        private void CalculateShadowVertices(Vector2 lightSourcePos, bool los = true)
        {
            //compute facing of each edge, using N*L
            for (int i = 0; i < primitiveCount; i++)
            {
                Vector2 firstVertex = new Vector2(vertices[i].X, vertices[i].Y);
                int secondIndex = (i + 1) % primitiveCount;
                Vector2 secondVertex = new Vector2(vertices[secondIndex].X, vertices[secondIndex].Y);
                Vector2 middle = (firstVertex + secondVertex) / 2;

                Vector2 L = lightSourcePos - middle;

                Vector2 N = new Vector2(
                    -(secondVertex.Y - firstVertex.Y),
                    secondVertex.X - firstVertex.X);

                backFacing[i] = (Vector2.Dot(N, L) < 0);
            }

            //find beginning and ending vertices which
            //belong to the shadow
            int startingIndex = 0;
            int endingIndex = 0;
            for (int i = 0; i < primitiveCount; i++)
            {
                int currentEdge = i;
                int nextEdge = (i + 1) % primitiveCount;

                if (backFacing[currentEdge] && !backFacing[nextEdge])
                    endingIndex = nextEdge;

                if (!backFacing[currentEdge] && backFacing[nextEdge])
                    startingIndex = nextEdge;
            }

            int shadowVertexCount;

            //nr of vertices that are in the shadow
            if (endingIndex > startingIndex)
                shadowVertexCount = endingIndex - startingIndex + 1;
            else
                shadowVertexCount = primitiveCount + 1 - startingIndex + endingIndex;

            shadowVertices = new VertexPositionColor[shadowVertexCount * 2];

            //create a triangle strip that has the shape of the shadow
            int currentIndex = startingIndex;
            int svCount = 0;
            while (svCount != shadowVertexCount * 2)
            {
                Vector3 vertexPos = new Vector3(vertices[currentIndex], 0.0f);

                //one vertex on the hull
                shadowVertices[svCount] = new VertexPositionColor();
                shadowVertices[svCount].Color = los ? Color.Black : Color.Transparent;
                shadowVertices[svCount].Position = vertexPos;

                //one extruded by the light direction
                shadowVertices[svCount + 1] = new VertexPositionColor();
                shadowVertices[svCount + 1].Color = los ? Color.Black : Color.Transparent;
                Vector3 L2P = vertexPos - new Vector3(lightSourcePos, 0);
                L2P.Normalize();
                shadowVertices[svCount + 1].Position = new Vector3(lightSourcePos, 0) + L2P * 9000;

                svCount += 2;
                currentIndex = (currentIndex + 1) % primitiveCount;
            }

            if (los)
            {
                CalculatePenumbraVertices(startingIndex, endingIndex, lightSourcePos, los);
            }
        }

        private void CalculatePenumbraVertices(int startingIndex, int endingIndex, Vector2 lightSourcePos, bool los)
        {
            penumbraVertices = new VertexPositionTexture[6];

            for (int n = 0; n < 4; n += 3)
            {
                Vector3 penumbraStart = new Vector3((n == 0) ? vertices[startingIndex] : vertices[endingIndex], 0.0f);

                penumbraVertices[n] = new VertexPositionTexture();
                penumbraVertices[n].Position = penumbraStart;
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
                    penumbraVertices[n + i + 1].Position = new Vector3(lightSourcePos, 0) + vertexDir * 9000;

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

        public void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, LightSource light, Matrix transform, bool los = true)
        {
            if (!Enabled) return;

            CachedShadow cachedShadow = null;
            if (cachedShadows.TryGetValue(light, out cachedShadow))
            {
                if (light.Position == cachedShadow.LightPos ||
                    Vector2.DistanceSquared(light.Position, cachedShadow.LightPos) < 1.0f)
                {
                    shadowVertices = cachedShadow.ShadowVertices;
                    penumbraVertices = cachedShadow.PenumbraVertices;

                }
                else
                {
                    CalculateShadowVertices(light.Position, los);
                    cachedShadow.LightPos = light.Position;
                    cachedShadow.ShadowVertices = shadowVertices;
                    cachedShadow.PenumbraVertices = penumbraVertices;

                }
            }
            else
            {
                CalculateShadowVertices(light.Position, los);
                cachedShadow = new CachedShadow(shadowVertices, penumbraVertices, light.Position);
                cachedShadows.Add(light, cachedShadow);
            }

            DrawShadows(graphicsDevice, cam, transform, los);
        }

        public void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, Vector2 lightSourcePos, Matrix transform, bool los = true)
        {
            if (!Enabled) return;

            CalculateShadowVertices(lightSourcePos, los);

            DrawShadows(graphicsDevice, cam, transform, los);
        }

        private void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, Matrix transform, bool los = true)
        {
            shadowEffect.World = transform;
            shadowEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, shadowVertices, 0, shadowVertices.Length - 2);
            
            if (los)
            {
                penumbraEffect.World = shadowEffect.World;
                penumbraEffect.CurrentTechnique.Passes[0].Apply();

#if WINDOWS
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, penumbraVertices, 0, 2, VertexPositionTexture.VertexDeclaration);
#endif
            }
        }

        public void Remove()
        {
            list.Remove(this);
        }


    }

}
