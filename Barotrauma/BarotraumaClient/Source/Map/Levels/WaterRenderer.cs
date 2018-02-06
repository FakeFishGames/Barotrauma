using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    struct WaterVertexData
    {
        float DistortStrengthX;
        float DistortStrengthY;
        float WaterAlpha;
        float WaterColorStrength;

        public WaterVertexData(float distortStrengthX, float distortStrengthY, float waterAlpha, float waterColorStrength)
        {
            DistortStrengthX = distortStrengthX;
            DistortStrengthY = distortStrengthY;
            WaterAlpha = waterAlpha;
            WaterColorStrength = waterColorStrength;
        }

        public static implicit operator Color(WaterVertexData wd)
        {
            return new Color(wd.DistortStrengthX, wd.DistortStrengthY, wd.WaterAlpha, wd.WaterColorStrength);
        }
    }

    class WaterRenderer : IDisposable
    {
        const int DefaultBufferSize = 1500;

        public Vector2 WavePos
        {
            get;
            private set;
        }

        public readonly Color waterColor = new Color(0.75f * 0.5f, 0.8f * 0.5f, 0.9f * 0.5f, 1.0f);

        public readonly WaterVertexData IndoorsWaterColor = new WaterVertexData(0.1f, 0.1f, 0.5f, 1.0f);
        public readonly WaterVertexData IndoorsSurfaceTopColor = new WaterVertexData(0.5f, 0.5f, 0.0f, 1.0f);
        public readonly WaterVertexData IndoorsSurfaceBottomColor = new WaterVertexData(0.2f, 0.1f, 0.9f, 1.0f);

        public VertexPositionTexture[] vertices = new VertexPositionTexture[DefaultBufferSize];
        public VertexPositionColorTexture[] SurfaceVertices = new VertexPositionColorTexture[DefaultBufferSize * 2];

        public Effect waterEffect
        {
            get;
            private set;
        }
        private BasicEffect basicEffect;

        public int PositionInBuffer = 0;
        public int PositionInSurfaceBuffer = 0;

        private Texture2D waterTexture;

        public Texture2D WaterTexture
        {
            get { return waterTexture; }
        }

        public WaterRenderer(GraphicsDevice graphicsDevice, ContentManager content)
        {
#if WINDOWS
            waterEffect = content.Load<Effect>("watershader");
#endif
#if LINUX

            waterEffect = content.Load<Effect>("watershader_opengl");
#endif

            waterTexture = TextureLoader.FromFile("Content/waterbump.png");
            waterEffect.Parameters["xWaveWidth"].SetValue(0.5f);
            waterEffect.Parameters["xWaveHeight"].SetValue(0.5f);

            waterEffect.Parameters["xWaterBumpMap"].SetValue(waterTexture);
            waterEffect.Parameters["waterColor"].SetValue(waterColor.ToVector4());

            if (basicEffect == null)
            {
                basicEffect = new BasicEffect(GameMain.Instance.GraphicsDevice);
                basicEffect.VertexColorEnabled = false;

                basicEffect.TextureEnabled = true;
            }
        }

        public void RenderBack(SpriteBatch spriteBatch, RenderTarget2D texture, Camera cam, float blurAmount = 0.0f)
        {
            spriteBatch.GraphicsDevice.BlendState = BlendState.AlphaBlend;
            
            waterEffect.Parameters["xTexture"].SetValue(texture);
            waterEffect.CurrentTechnique = waterEffect.Techniques["WaterShader"];

            Vector2 offset = WavePos;
            if (cam != null)
            {
                offset += cam.WorldView.Location.ToVector2();
                offset.Y += cam.WorldView.Height;
                offset.X += cam.WorldView.Width;
            }
            offset.Y = -offset.Y;
            waterEffect.Parameters["xUvOffset"].SetValue(
                new Vector2(
                    offset.X / GameMain.GraphicsWidth,
                    offset.Y / GameMain.GraphicsHeight));

            waterEffect.Parameters["xBumpPos"].SetValue(Vector2.Zero);
            waterEffect.Parameters["xBlurDistance"].SetValue(blurAmount);

            if (cam != null)
            {
                waterEffect.Parameters["xBumpScale"].SetValue(new Vector2(
                        (float)cam.WorldView.Width / GameMain.GraphicsWidth,
                        (float)cam.WorldView.Height / GameMain.GraphicsHeight));
                waterEffect.Parameters["xTransform"].SetValue(cam.ShaderTransform
                    * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f);
                waterEffect.Parameters["xUvTransform"].SetValue(cam.ShaderTransform
                    * Matrix.CreateOrthographicOffCenter(0, spriteBatch.GraphicsDevice.Viewport.Width * 2, spriteBatch.GraphicsDevice.Viewport.Height * 2, 0, 0, 1) * Matrix.CreateTranslation(0.5f, 0.5f, 0.0f));
            }
            else
            {
                waterEffect.Parameters["xBumpScale"].SetValue(new Vector2(1.0f, 1.0f));
                waterEffect.Parameters["xTransform"].SetValue(Matrix.Identity * Matrix.CreateTranslation(-1.0f, 1.0f, 0.0f));
                waterEffect.Parameters["xUvTransform"].SetValue(Matrix.CreateScale(0.5f, -0.5f, 0.0f));
            }

            waterEffect.CurrentTechnique.Passes[0].Apply();

            VertexPositionColorTexture[] verts = new VertexPositionColorTexture[6];

            Rectangle view = cam != null ? cam.WorldView : spriteBatch.GraphicsDevice.Viewport.Bounds;

            var corners = new Vector3[4];
            corners[0] = new Vector3(view.X, view.Y, 0.1f);
            corners[1] = new Vector3(view.Right, view.Y, 0.1f);
            corners[2] = new Vector3(view.Right, view.Y - view.Height, 0.1f);
            corners[3] = new Vector3(view.X, view.Y - view.Height, 0.1f);

            WaterVertexData backGroundColor = new WaterVertexData(0.1f, 0.1f, 0.5f, 0.1f);
            verts[0] = new VertexPositionColorTexture(corners[0], backGroundColor, Vector2.Zero);
            verts[1] = new VertexPositionColorTexture(corners[1], backGroundColor, Vector2.Zero);
            verts[2] = new VertexPositionColorTexture(corners[2], backGroundColor, Vector2.Zero);
            verts[3] = new VertexPositionColorTexture(corners[0], backGroundColor, Vector2.Zero);
            verts[4] = new VertexPositionColorTexture(corners[2], backGroundColor, Vector2.Zero);
            verts[5] = new VertexPositionColorTexture(corners[3], backGroundColor, Vector2.Zero);

            spriteBatch.GraphicsDevice.DrawUserPrimitives<VertexPositionColorTexture>(
                PrimitiveType.TriangleList, verts, 0, 2);

            if (PositionInSurfaceBuffer == 0) return;

            offset = WavePos;
            if (cam != null)
            {
                offset.Y += cam.WorldView.Height;
                offset.X += cam.WorldView.Width;
            }
            offset.Y = -offset.Y;
            waterEffect.Parameters["xUvOffset"].SetValue(
                new Vector2(
                    offset.X / GameMain.GraphicsWidth,
                    offset.Y / GameMain.GraphicsHeight));

            waterEffect.CurrentTechnique.Passes[0].Apply();

            spriteBatch.GraphicsDevice.DrawUserPrimitives<VertexPositionColorTexture>(
                PrimitiveType.TriangleList, SurfaceVertices, 0, PositionInSurfaceBuffer / 3);
        }

        public void ScrollWater(float deltaTime)
        {
            WavePos = WavePos + Vector2.One * 10.0f * deltaTime;
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam, RenderTarget2D texture, Matrix transform)
        {
            if (vertices == null || vertices.Length < 0 || PositionInBuffer <= 0) return;

            basicEffect.Texture = texture;

            basicEffect.View = Matrix.Identity;
            basicEffect.World = transform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f * Matrix.CreateTranslation(0.0f,0.0f,0f);
            basicEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;
            graphicsDevice.DrawUserPrimitives<VertexPositionTexture>(PrimitiveType.TriangleList, vertices, 0, PositionInBuffer / 3);
         
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            if (waterEffect != null)
            {
                waterEffect.Dispose();
                waterEffect = null;
            }

            if (basicEffect != null)
            {
                basicEffect.Dispose();
                basicEffect = null;
            }
        }

    }
}
