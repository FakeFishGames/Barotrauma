using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

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

        public void SetWallVertices(
            VertexPositionColorTexture[] wallVertices, VertexPositionColorTexture[] wallEdgeVertices, 
            Texture2D wallTexture, Texture2D edgeTexture)
        {
            if (VertexBuffer != null && !VertexBuffer.IsDisposed) { VertexBuffer.Dispose(); }
            VertexBuffer = new LevelWallVertexBuffer(wallVertices, wallEdgeVertices, wallInnerVertices: null, wallTexture, edgeTexture);
        }

        public void GenerateVertices()
        {
            float zCoord = this is DestructibleLevelWall ? Rand.Range(0.9f, 1.0f) : 0.9f;
            var nonTexturedWallVerts =
                 CaveGenerator.GenerateWallVertices(triangles, color, zCoord: 0.9f).ToArray();
            var wallVerts = CaveGenerator.ConvertToTextured(nonTexturedWallVerts, level.GenerationParams.WallTextureSize);
            SetWallVertices(
                wallVertices: wallVerts,
                wallEdgeVertices: CaveGenerator.GenerateWallEdgeVertices(Cells,
                    level.GenerationParams.WallEdgeExpandOutwardsAmount, level.GenerationParams.WallEdgeExpandInwardsAmount,
                    outerColor: color, innerColor: color,
                    level, zCoord)
                    .ToArray(),
                level.GenerationParams.WallSprite.Texture,
                level.GenerationParams.WallEdgeSprite.Texture);
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
