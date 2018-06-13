using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class DeformableSprite
    {
        private int triangleCount;
        private VertexPositionColorTexture[] vertices;
        private ushort[] indices;

        private VertexBuffer vertexBuffer;
        private IndexBuffer indexBuffer;

        int subDivX, subDivY;

        partial void InitProjSpecific(XElement element, int? subdivisionsX, int? subdivisionsY)
        {
            sprite = new Sprite(element);

            //use subdivisions configured in the xml if the arguments passed to the method are null
            Vector2 subdivisionsInXml = element.GetAttributeVector2("subdivisions", Vector2.One);
            subDivX = subdivisionsX ?? (int)subdivisionsInXml.X;
            subDivY = subdivisionsY ?? (int)subdivisionsInXml.Y;

            if (subDivX <= 0 || subDivY <= 0)
            {
                throw new ArgumentException("Deformable sprites must have one or more subdivisions on each axis.");
            }

            Vector2 textureSize = new Vector2(sprite.Texture.Width, sprite.Texture.Height);
            Vector2 texelTopLeft = Vector2.Divide(sprite.SourceRect.Location.ToVector2(), textureSize);
            Vector2 texelBottomRight = Vector2.Divide((sprite.SourceRect.Location + sprite.SourceRect.Size).ToVector2(), textureSize);

            System.Diagnostics.Debug.Assert(texelTopLeft.X < texelBottomRight.X);
            System.Diagnostics.Debug.Assert(texelTopLeft.Y < texelBottomRight.Y);

            vertices = new VertexPositionColorTexture[(subDivX + 1) * (subDivY + 1)];
            triangleCount = subDivX * subDivY * 2;
            indices = new ushort[triangleCount * 3];
            for (int x = 0; x <= subDivX; x++)
            {
                for (int y = 0; y <= subDivY; y++)
                {
                    //{0,0} -> {1,1}
                    Vector2 relativePos = new Vector2(x / (float)subDivX, y / (float)subDivY);

                    vertices[x + y * (subDivX + 1)] = new VertexPositionColorTexture(
                        position: new Vector3(relativePos.X * sprite.SourceRect.Width, relativePos.Y * sprite.SourceRect.Height, 0.0f),
                        color: Color.White,
                        textureCoordinate: texelTopLeft + (texelBottomRight - texelTopLeft) * relativePos);
                }
            }

            int offset = 0;
            for (int i = 0; i < triangleCount / 2; i++)
            {
                indices[i * 6] = (ushort)(i + offset + 1);
                indices[i * 6 + 1] = (ushort)(i + offset + (subDivX + 1) + 1);
                indices[i * 6 + 2] = (ushort)(i + offset + (subDivX + 1));

                indices[i * 6 + 3] = (ushort)(i + offset);
                indices[i * 6 + 4] = (ushort)(i + offset + 1);
                indices[i * 6 + 5] = (ushort)(i + offset + (subDivX + 1));

                if ((i + 1) % subDivX == 0) offset++;
            }

            vertexBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.None);
            vertexBuffer.SetData(vertices);
            indexBuffer = new IndexBuffer(GameMain.Instance.GraphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.None);
            indexBuffer.SetData(indices);
        }


        /// <summary>
        /// Distort the vertices of the sprite using an arbitrary function. The in-parameter of the function is the
        /// normalized position of the vertex (i.e. 0,0 = top-left corner of the sprite, 1,1 = bottom-right) and the output
        /// is the amount of distortion.
        /// </summary>
        public void Distort(Func<Vector2, Vector2> distortFunction)
        {
            var distortedVertices = new VertexPositionColorTexture[vertices.Length];
            for (int x = 0; x <= subDivX; x++)
            {
                for (int y = 0; y <= subDivY; y++)
                {
                    Vector3 distort = new Vector3(distortFunction(new Vector2(x / (float)subDivX, y / (float)subDivY)), 0.0f);
                    int vertexIndex = x + y * (subDivX + 1);
                    distortedVertices[vertexIndex] = new VertexPositionColorTexture(
                        position: vertices[vertexIndex].Position + distort,
                        color: vertices[vertexIndex].Color,
                        textureCoordinate: vertices[vertexIndex].TextureCoordinate);
                }
            }
            vertexBuffer.SetData(distortedVertices);
        }
        
        public void Distort(Vector2[,] distortDir)
        {
            var distortedVertices = new VertexPositionColorTexture[vertices.Length];
            Vector2 div = new Vector2(1.0f / subDivX, 1.0f / subDivY);
            Vector2 div2 = new Vector2(1.0f / distortDir.GetLength(0), 1.0f / distortDir.GetLength(1));
            
            for (int x = 0; x <= subDivX; x++)
            {
                for (int y = 0; y <= subDivY; y++)
                {
                    float normalizedX = x / (float)subDivX;
                    float normalizedY = y / (float)subDivY;

                    Point distortIndexTopLeft = new Point(
                        Math.Min((int)Math.Floor(normalizedX * distortDir.GetLength(0)), distortDir.GetLength(0) - 1),
                        Math.Min((int)Math.Floor(normalizedY * distortDir.GetLength(1)), distortDir.GetLength(1) - 1));

                    Point distortIndexBottomRight = new Point(
                        Math.Min(distortIndexTopLeft.X + 1, distortDir.GetLength(0) - 1),
                        Math.Min(distortIndexTopLeft.Y + 1, distortDir.GetLength(1) - 1));

                    Vector2 distortTopLeft = distortDir[distortIndexTopLeft.X, distortIndexTopLeft.Y];
                    Vector2 distortBottomRight = distortDir[distortIndexBottomRight.X, distortIndexBottomRight.Y];

                    Vector3 distort = new Vector3(
                        MathHelper.Lerp(distortTopLeft.X, distortBottomRight.X, (normalizedX % div2.X) / div2.X),
                        MathHelper.Lerp(distortTopLeft.Y, distortBottomRight.Y, (normalizedY % div2.Y) / div2.Y), 
                        0.0f);
                    
                    int vertexIndex = x + y * (subDivX + 1);
                    distortedVertices[vertexIndex] = new VertexPositionColorTexture(
                        position: vertices[vertexIndex].Position + distort,
                        color: vertices[vertexIndex].Color,
                        textureCoordinate: vertices[vertexIndex].TextureCoordinate);
                }
            }

            vertexBuffer.SetData(distortedVertices);
        }

        public void Reset()
        {
            vertexBuffer.SetData(vertices);
        }

        public void Draw(BasicEffect effect, Camera cam, Vector2 pos, Vector2 origin, float rotate, Vector2 scale)
        {
            Matrix matrix = Matrix.CreateTranslation(-origin.X, -origin.Y, 0) *
                Matrix.CreateScale(scale.X, -scale.Y, 1.0f) *
                Matrix.CreateRotationZ(-rotate) *
                Matrix.CreateTranslation(pos.X, pos.Y, 0.0f);

            effect.World = matrix * cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;
            effect.GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            effect.Texture = sprite.Texture;
            effect.GraphicsDevice.SetVertexBuffer(vertexBuffer);
            effect.GraphicsDevice.Indices = indexBuffer;
            effect.CurrentTechnique.Passes[0].Apply();
            effect.GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triangleCount);
        }

        public void Remove()
        {
            sprite?.Remove();
            sprite = null;
            vertexBuffer?.Dispose();
            vertexBuffer = null;
            indexBuffer?.Dispose();
            indexBuffer = null;
        }
    }
}
