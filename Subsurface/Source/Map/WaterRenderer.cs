using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class WaterRenderer : IDisposable
    {
        const int DefaultBufferSize = 1500;

        private Vector2 wavePos;

        public VertexPositionTexture[] vertices = new VertexPositionTexture[DefaultBufferSize];

        private Effect waterEffect;
        private BasicEffect basicEffect;

        public int PositionInBuffer = 0;

        private Texture2D waterTexture;

        public Texture2D WaterTexture
        {
            get { return waterTexture; }
        }

        public WaterRenderer(GraphicsDevice graphicsDevice)
        {
#if WINDOWS
            byte[] bytecode = File.ReadAllBytes("Content/watershader.mgfx");
#endif
#if LINUX
			byte[] bytecode = File.ReadAllBytes("Content/watershader_opengl.mgfx");
#endif

            waterEffect = new Effect(graphicsDevice, bytecode);

            waterTexture = TextureLoader.FromFile("Content/waterbump.png");
            waterEffect.Parameters["xWaveWidth"].SetValue(0.05f);
            waterEffect.Parameters["xWaveHeight"].SetValue(0.05f);
#if WINDOWS
            //waterEffect.Parameters["xTexture"].SetValue(waterTexture);
#endif
#if LINUX
            waterEffect.Parameters["xWaterBumpMap"].SetValue(waterTexture);
#endif

            if (basicEffect == null)
            {
                basicEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                basicEffect.VertexColorEnabled = false;

                basicEffect.TextureEnabled = true;
            }
        }

        public void RenderBack(SpriteBatch spriteBatch, RenderTarget2D texture, float blurAmount = 0.0f)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearWrap);

            waterEffect.CurrentTechnique = waterEffect.Techniques["WaterShader"];
            waterEffect.Parameters["xWavePos"].SetValue(wavePos);
            waterEffect.Parameters["xBlurDistance"].SetValue(blurAmount);
            waterEffect.CurrentTechnique.Passes[0].Apply();

            wavePos.X += 0.0001f;
            wavePos.Y += 0.0001f;

#if WINDOWS
            waterEffect.Parameters["xTexture"].SetValue(texture);
            spriteBatch.Draw(waterTexture, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
#elif LINUX

            spriteBatch.Draw(texture, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
#endif

            spriteBatch.End();
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam, RenderTarget2D texture, Matrix transform)
        {
            if (vertices == null) return;
            if (vertices.Length < 0) return;

            basicEffect.Texture = texture;

            basicEffect.View = Matrix.Identity;
            basicEffect.World = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            basicEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            graphicsDevice.DrawUserPrimitives<VertexPositionTexture>(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
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
