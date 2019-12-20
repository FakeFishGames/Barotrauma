using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class LevelWall : IDisposable
    {
        private VertexBuffer wallVertices, bodyVertices;

        public VertexBuffer WallVertices
        {
            get { return wallVertices; }
        }

        public VertexBuffer BodyVertices
        {
            get { return bodyVertices; }
        }

        public Matrix GetTransform()
        {
            return body.BodyType == BodyType.Static ?
                Matrix.Identity :
                Matrix.CreateRotationZ(body.Rotation) *
                Matrix.CreateTranslation(new Vector3(ConvertUnits.ToDisplayUnits(body.Position), 0.0f));
        }

        public void SetWallVertices(VertexPositionTexture[] vertices, Color color)
        {
            wallVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            wallVertices.SetData(LevelRenderer.GetColoredVertices(vertices, color));
        }

        public void SetBodyVertices(VertexPositionTexture[] vertices, Color color)
        {
            bodyVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            bodyVertices.SetData(LevelRenderer.GetColoredVertices(vertices, color));
        }

        public void SetWallVertices(VertexPositionColorTexture[] vertices)
        {
            if (wallVertices != null && !wallVertices.IsDisposed) wallVertices.Dispose();
            wallVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            wallVertices.SetData(vertices);
        }

        public void SetBodyVertices(VertexPositionColorTexture[] vertices)
        {
            if (bodyVertices != null && !bodyVertices.IsDisposed) bodyVertices.Dispose();
            bodyVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            bodyVertices.SetData(vertices);
        }
    }
}
