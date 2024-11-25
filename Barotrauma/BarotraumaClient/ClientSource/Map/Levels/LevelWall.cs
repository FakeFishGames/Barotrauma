using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class LevelWall : IDisposable
    {
        public LevelWallVertexBuffer VertexBuffer { get; private set; }

        public VertexBuffer WallBuffer { get { return VertexBuffer.WallBuffer; } }

        public VertexBuffer WallEdgeBuffer { get { return VertexBuffer.WallEdgeBuffer; } }

        public virtual float Alpha => 1.0f;

        public Matrix GetTransform()
        {
            return Body.FixedRotation ?
                Matrix.CreateTranslation(new Vector3(ConvertUnits.ToDisplayUnits(Body.Position), 0.0f)) :
                Matrix.CreateRotationZ(Body.Rotation) *
                Matrix.CreateTranslation(new Vector3(ConvertUnits.ToDisplayUnits(Body.Position), 0.0f));
        }

        public void SetWallVertices(VertexPositionTexture[] wallVertices, VertexPositionTexture[] wallEdgeVertices, Texture2D wallTexture, Texture2D edgeTexture, Color color)
        {
            if (VertexBuffer != null && !VertexBuffer.IsDisposed) { VertexBuffer.Dispose(); }
            VertexBuffer = new LevelWallVertexBuffer(wallVertices, wallEdgeVertices, wallTexture, edgeTexture, color);
        }

        public void GenerateVertices()
        {
            float zCoord = this is DestructibleLevelWall ? Rand.Range(0.9f, 1.0f) : 0.9f;
            List<VertexPositionTexture> wallVertices = CaveGenerator.GenerateWallVertices(triangles, level.GenerationParams, zCoord);
            SetWallVertices(
                wallVertices.ToArray(),
                CaveGenerator.GenerateWallEdgeVertices(Cells, level, zCoord).ToArray(),
                level.GenerationParams.WallSprite.Texture,
                level.GenerationParams.WallEdgeSprite.Texture,
                color);
        }

        public bool IsVisible(Rectangle worldView)
        {
            RectangleF worldViewInSimUnits = new RectangleF(
                ConvertUnits.ToSimUnits(worldView.Location.ToVector2()), 
                ConvertUnits.ToSimUnits(worldView.Size.ToVector2()));

            foreach (var fixture in Body.FixtureList)
            {
                fixture.GetAABB(out var aabb, 0);
                Vector2 lowerBound = aabb.LowerBound + Body.Position;
                if (lowerBound.X > worldViewInSimUnits.Right || lowerBound.Y > worldViewInSimUnits.Y) { continue; }
                Vector2 upperBound = aabb.UpperBound + Body.Position;
                if (upperBound.X < worldViewInSimUnits.X || upperBound.Y < worldViewInSimUnits.Y - worldViewInSimUnits.Height) { continue; }
                return true;                
            }
            return false;
        }
    }
}
