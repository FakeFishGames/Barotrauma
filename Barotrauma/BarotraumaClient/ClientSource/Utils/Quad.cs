using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    static class Quad
    {
        private static VertexBuffer vertexBuffer = null;
        private static IndexBuffer indexBuffer = null;
        private static BasicEffect basicEffect = null;
        private static GraphicsDevice graphicsDevice = null;

        public static void Init(GraphicsDevice graphics)
        {
            if (graphicsDevice != null) { return; }

            graphicsDevice = graphics;

            vertexBuffer = new VertexBuffer(graphics, VertexPositionTexture.VertexDeclaration, 4, BufferUsage.WriteOnly);
            indexBuffer = new IndexBuffer(graphics, IndexElementSize.SixteenBits, 4, BufferUsage.WriteOnly);

            InitVertexData();
            indexBuffer.SetData(new ushort[] { 0, 1, 2, 3 });

            basicEffect = new BasicEffect(graphics) { TextureEnabled = true };

            GameMain.Instance.ResolutionChanged += () =>
            {
                InitVertexData();
            };
        }

        private static void InitVertexData()
        {
            Vector2 halfPixelOffset = Vector2.Zero;
#if LINUX || OSX
            halfPixelOffset = new Vector2(0.5f / GameMain.GraphicsWidth, 0.5f / GameMain.GraphicsHeight);
#endif

            VertexPositionTexture[] vertices =
            {
                new VertexPositionTexture(new Vector3(-1f, -1f, 1f), new Vector2(0f, 1f) + halfPixelOffset),
                new VertexPositionTexture(new Vector3(-1f, 1f, 1f), new Vector2(0f, 0f) + halfPixelOffset),
                new VertexPositionTexture(new Vector3(1f, -1f, 1f), new Vector2(1f, 1f) + halfPixelOffset),
                new VertexPositionTexture(new Vector3(1f, 1f, 1f), new Vector2(1f, 0f) + halfPixelOffset)
            };

            vertexBuffer.SetData(vertices);
        }

        public static void UseBasicEffect(Texture2D texture)
        {
            basicEffect.Texture = texture;
            basicEffect.CurrentTechnique.Passes[0].Apply();
        }

        public static void Render()
        {
            graphicsDevice.SetVertexBuffer(vertexBuffer);
            graphicsDevice.Indices = indexBuffer;
            graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleStrip, 0, 0, 2);
        }
    }
}
