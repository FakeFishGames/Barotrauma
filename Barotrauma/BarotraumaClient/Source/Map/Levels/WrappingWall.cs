using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class WrappingWall : IDisposable
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

        public void SetWallVertices(VertexPositionTexture[] vertices)
        {
            wallVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            wallVertices.SetData(vertices);
        }

        public void SetBodyVertices(VertexPositionColor[] vertices)
        {
            bodyVertices = new VertexBuffer(GameMain.Instance.GraphicsDevice, VertexPositionColor.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            bodyVertices.SetData(vertices);
        }
    }
}
