using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
{
    struct WaterVertex
    {
        public Vector3 position;
        private Vector2 texCoord;

        public WaterVertex(Vector3 position, Vector2 texCoord, Matrix transform)
        {
            this.position = position;

            this.texCoord = Vector2.Transform(texCoord, transform);
        }

        public WaterVertex(Vector3 position, Vector2 texCoord)
        {
            this.position = position;

            this.texCoord = texCoord;
        }

        public readonly static VertexDeclaration VertexDeclaration = new VertexDeclaration
        (
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(sizeof(float) * 3, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
        );

        public void TransformTexCoord(Matrix transform)
        {
            texCoord = Vector2.Transform(texCoord, transform);
        }
    }

    class WaterRenderer : IDisposable
    {
        const int DefaultBufferSize = 1500;

        Effect effect;

        public Vector2 wavePos;

        public WaterVertex[] vertices = new WaterVertex[DefaultBufferSize];

        private VertexBuffer vertexBuffer;

        public int positionInBuffer = 0;

        public WaterRenderer(GraphicsDevice graphicsDevice)
        {
            //vertexBuffer = new VertexBuffer(graphicsDevice, WaterVertex.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            //vertexBuffer.SetData(vertices);

            //effect = Game1.game.Content.Load<Effect>("effects");

            byte[] bytecode = File.ReadAllBytes("Content/effects.mgfx");
            effect = new Effect(graphicsDevice, bytecode);

            //Texture2D waterBumpMap = Game1.textureLoader.FromFile("Content/waterbump.jpg");
            //effect.Parameters["xBump"].SetValue(waterBumpMap);
            //effect.Parameters["xWaveLength"].SetValue(0.5f);
            //effect.Parameters["xWaveHeight"].SetValue(0.03f);
            effect.Parameters["xProjection"].SetValue(Matrix.CreateOrthographic(Game1.GraphicsWidth, Game1.GraphicsHeight, -1, 1));
            effect.Parameters["xColor"].SetValue(new Vector4(0.75f, 0.8f, 0.9f, 1.0f));
            effect.Parameters["xBlurDistance"].SetValue(0.0005f);

            effect.Parameters["xWaterBumpMap"].SetValue(Game1.TextureLoader.FromFile("Content/waterbump.jpg"));
            effect.Parameters["xWaveWidth"].SetValue(0.1f);
            effect.Parameters["xWaveHeight"].SetValue(0.1f);

            vertexBuffer = new VertexBuffer(graphicsDevice, WaterVertex.VertexDeclaration, DefaultBufferSize, BufferUsage.WriteOnly);
        }

        public void RenderBack(GraphicsDevice graphicsDevice, RenderTarget2D texture, Matrix transform)
        {
            WaterVertex[] verts = new WaterVertex[6];

            // create the four corners of our triangle.
            Vector3 p1 = new Vector3(-graphicsDevice.Viewport.Width / 2.0f, graphicsDevice.Viewport.Height / 2.0f, 0.0f);
            Vector3 p2 = new Vector3(-p1.X, p1.Y, 0.0f);

            Vector3 p3 = new Vector3(p2.X, -p1.Y, 0.0f);
            Vector3 p4 = new Vector3(p1.X, -p1.Y, 0.0f);

            verts[0] = new WaterVertex(p1, new Vector2(0, 0));
            verts[1] = new WaterVertex(p2, new Vector2(1, 0));
            verts[2] = new WaterVertex(p3, new Vector2(1, 1));

            verts[3] = new WaterVertex(p1, new Vector2(0, 0));
            verts[4] = new WaterVertex(p3, new Vector2(1, 1));
            verts[5] = new WaterVertex(p4, new Vector2(0, 1));

            vertexBuffer.SetData(verts);

            wavePos.X += 0.0001f;
            wavePos.Y += 0.0001f;

            effect.Parameters["xWavePos"].SetValue(wavePos);

            effect.CurrentTechnique = effect.Techniques["WaterShader"];
            effect.Parameters["xTexture"].SetValue(texture);
            effect.Parameters["xView"].SetValue(Matrix.Identity);

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3, WaterVertex.VertexDeclaration);
            }
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam, RenderTarget2D texture, Matrix transform)
        {
            if (vertices == null) return;
            if (vertices.Length < 0) return;

            vertexBuffer.SetData(vertices);

            effect.Parameters["xBumpPos"].SetValue(cam.Position / Game1.GraphicsWidth / cam.Zoom);

            effect.CurrentTechnique = effect.Techniques["EmptyShader"];
            effect.Parameters["xTexture"].SetValue(texture);
            effect.Parameters["xView"].SetValue(transform);

            foreach (EffectPass pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3, WaterVertex.VertexDeclaration);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (vertexBuffer != null)
                {
                    vertexBuffer.Dispose();
                    vertexBuffer = null;
                }

                if (effect != null)
                {
                    effect.Dispose();
                    effect = null;
                }
            }
        }


    }
}
