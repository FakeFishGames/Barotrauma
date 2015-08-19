using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Subsurface.Lights
{
    class ConvexHull
    {
        public static List<ConvexHull> list = new List<ConvexHull>();
        static BasicEffect losEffect;
        static BasicEffect shadowEffect;
        
        private VertexPositionColor[] vertices;
        private short[] indices;
        int primitiveCount;

        bool[] backFacing;
        VertexPositionColor[] shadowVertices;

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
            int vertexCount = points.Length;
            vertices = new VertexPositionColor[vertexCount + 1];
            Vector2 center = Vector2.Zero;

            float? minX = null, minY = null, maxX = null, maxY = null;

            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new VertexPositionColor(new Vector3(points[i], 0), color);
                center += points[i];

                if (minX == null || points[i].X < minX) minX = points[i].X;
                if (minY == null || points[i].Y < minY) minY = points[i].Y;

                if (maxX == null || points[i].X > maxX) maxX = points[i].X;
                if (maxY == null || points[i].Y > minY) maxY = points[i].Y;
            }
            center /= points.Length;
            vertices[vertexCount] = new VertexPositionColor(new Vector3(center, 0), color);

            boundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX-minX), (int)(maxY-minY));

            primitiveCount = points.Length;
            indices = new short[primitiveCount * 3];

            for (int i = 0; i < primitiveCount; i++)
            {
                indices[3 * i] = (short)i;
                indices[3 * i + 1] = (short)((i + 1) % vertexCount);
                indices[3 * i + 2] = (short)vertexCount;
            }
            backFacing = new bool[vertexCount];
            
            Enabled = true;

            list.Add(this);
        }

        public void Move(Vector2 amount)
        {
            for (int i = 0; i < vertices.Count(); i++)
            {
                vertices[i].Position = new Vector3(vertices[i].Position.X + amount.X, vertices[i].Position.Y + amount.Y, vertices[i].Position.Z);
            }
        }

        public void SetVertices(Vector2[] points)
        {
            int vertexCount = points.Length;
            vertices = new VertexPositionColor[vertexCount + 1];

            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new VertexPositionColor(new Vector3(points[i], 0), Color.Black);
            }
        }

        //public void Draw(GameTime gameTime)
        //{
        //    device.RasterizerState = RasterizerState.CullNone;
        //    device.BlendState = BlendState.Opaque;

        //    drawingEffect.World = Matrix.CreateTranslation(position.X, position.Y, 0);

        //    foreach (EffectPass pass in drawingEffect.CurrentTechnique.Passes)
        //    {
        //        pass.Apply();
        //        device.DrawUserIndexedPrimitives<VertexPositionColor>(PrimitiveType.TriangleList, vertices, 0, vertices.Length, indices, 0, primitiveCount);
        //    }
        //}

        public void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, Vector2 lightSourcePos, bool los = true)
        {
            if (!Enabled) return;

            if (losEffect == null)
            {
                losEffect = new BasicEffect(graphicsDevice);
                losEffect.VertexColorEnabled = true;
            }
            if (shadowEffect==null)
            {
                shadowEffect = new BasicEffect(graphicsDevice);
                shadowEffect.TextureEnabled = true;
                //shadowEffect.VertexColorEnabled = true;
                shadowEffect.LightingEnabled = false;
                shadowEffect.Texture = Game1.TextureLoader.FromFile("Content/lights/penumbra.png");
            }
            
            //compute facing of each edge, using N*L
            for (int i = 0; i < primitiveCount; i++)
            {
                Vector2 firstVertex = new Vector2(vertices[i].Position.X, vertices[i].Position.Y);
                int secondIndex = (i + 1) % primitiveCount;
                Vector2 secondVertex = new Vector2(vertices[secondIndex].Position.X, vertices[secondIndex].Position.Y);
                Vector2 middle = (firstVertex + secondVertex) / 2;

                Vector2 L = lightSourcePos - middle;

                Vector2 N = new Vector2();
                N.X = -(secondVertex.Y - firstVertex.Y);
                N.Y = secondVertex.X - firstVertex.X;

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

            VertexPositionTexture[] penumbraVertices = new VertexPositionTexture[6];

            if (los)
            {
                for (int n = 0; n < 4; n+=3)
                {
                    Vector3 penumbraStart = (n == 0) ? vertices[startingIndex].Position : vertices[endingIndex].Position;

                    penumbraVertices[n] = new VertexPositionTexture();
                    penumbraVertices[n].Position = penumbraStart;
                    penumbraVertices[n].TextureCoordinate = new Vector2(0.0f, 1.0f);
                    //penumbraVertices[0].te = fow ? Color.Black : Color.Transparent;

                    for (int i = 0; i < 2; i++ )
                    {
                        penumbraVertices[n + i + 1] = new VertexPositionTexture();
                        Vector3 vertexDir = penumbraStart - new Vector3(lightSourcePos, 0);
                        vertexDir.Normalize();

                        Vector3 normal = (i == 0) ? new Vector3(-vertexDir.Y, vertexDir.X, 0.0f) : new Vector3(vertexDir.Y, -vertexDir.X, 0.0f)*0.05f;
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
                Vector3 vertexPos = vertices[currentIndex].Position;

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

            losEffect.World = cam.ShaderTransform
                * Matrix.CreateOrthographic(Game1.GraphicsWidth, Game1.GraphicsHeight, -1, 1) * 0.5f;
            losEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.DrawUserPrimitives<VertexPositionColor>(PrimitiveType.TriangleStrip, shadowVertices, 0, shadowVertexCount * 2 - 2);

            if (los)
            {
                shadowEffect.World = cam.ShaderTransform
                    * Matrix.CreateOrthographic(Game1.GraphicsWidth, Game1.GraphicsHeight, -1, 1) * 0.5f;
                shadowEffect.CurrentTechnique.Passes[0].Apply();

#if WINDOWS
    graphicsDevice.DrawUserPrimitives<VertexPositionTexture>(PrimitiveType.TriangleList, penumbraVertices, 0, 2, VertexPositionTexture.VertexDeclaration);
            
#endif
            }
        }

        public void Remove()
        {
            list.Remove(this);
        }


    }

}
