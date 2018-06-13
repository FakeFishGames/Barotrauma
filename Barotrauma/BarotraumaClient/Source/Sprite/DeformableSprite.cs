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

        partial void InitProjSpecific(XElement element, int? subdivisionsX, int? subdivisionsY)
        {
            sprite = new Sprite(element);

            //use subdivisions configured in the xml if the arguments passed to the method are null
            Vector2 subdivisionsInXml = element.GetAttributeVector2("subdivisions", Vector2.One);
            int subDivX = subdivisionsX ?? (int)subdivisionsInXml.X;
            int subDivY = subdivisionsY ?? (int)subdivisionsInXml.Y;

            if (subdivisionsX <= 0 || subdivisionsY <= 0)
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
            for (int x = 0; x <= subdivisionsX; x++)
            {
                for (int y = 0; y <= subdivisionsY; y++)
                {
                    //{0,0} -> {1,1}
                    Vector2 relativePos = new Vector2(x / (float)subdivisionsX, y / (float)subdivisionsY);

                    vertices[x + y * (subDivX + 1)] = new VertexPositionColorTexture(
                        position: new Vector3(relativePos.X * sprite.SourceRect.Width, relativePos.Y * sprite.SourceRect.Height, 0.0f),
                        color: Color.White,
                        textureCoordinate: texelTopLeft + (texelBottomRight - texelTopLeft) * relativePos);
                }
            }

            for (int i = 0; i < triangleCount / 2; i++)
            {
                indices[i * 6] = (ushort)(i + 1);
                indices[i * 6 + 1] = (ushort)(i + 3);
                indices[i * 6 + 2] = (ushort)(i + 2);

                indices[i * 6 + 3] = (ushort)i;
                indices[i * 6 + 4] = (ushort)(i + 1);
                indices[i * 6 + 5] = (ushort)(i + 2);
            }

            vertexBuffer = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.None);
            vertexBuffer.SetData(vertices);
            indexBuffer = new IndexBuffer(GameMain.Instance.GraphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.None);
            indexBuffer.SetData(indices);
        }

        public void Draw(BasicEffect effect, Camera cam, Vector2 pos, Vector2 origin, float rotate, Vector2 scale)
        {
            Matrix matrix = Matrix.CreateTranslation(-origin.X, -origin.Y, 0) *
                Matrix.CreateScale(scale.X, -scale.Y, 1.0f) *
                Matrix.CreateRotationZ(-rotate) *
                Matrix.CreateTranslation(pos.X, pos.Y, 0.0f);

            effect.World = matrix * cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

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
