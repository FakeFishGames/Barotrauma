using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
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

        public WaterRenderer(GraphicsDevice graphicsDevice)
        {
#if WINDOWS
			byte[] bytecode = File.ReadAllBytes("Content/watershader.mgfx");
#endif
#if LINUX
			byte[] bytecode = File.ReadAllBytes("Content/effects_linux.mgfx");
#endif

            waterEffect = new Effect(graphicsDevice, bytecode);

            waterTexture = Game1.TextureLoader.FromFile("Content/waterbump.jpg");
            waterEffect.Parameters["xWaveWidth"].SetValue(0.1f);
            waterEffect.Parameters["xWaveHeight"].SetValue(0.1f);
            waterEffect.Parameters["xBlurDistance"].SetValue(0.0007f);

            if (basicEffect==null)
            {                
                basicEffect = new BasicEffect(Game1.CurrGraphicsDevice);
                basicEffect.VertexColorEnabled = false;

                basicEffect.TextureEnabled = true;
            }
        }

        public void RenderBack (SpriteBatch spriteBatch, RenderTarget2D texture, Matrix transform)
        {            
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearWrap);

            waterEffect.CurrentTechnique = waterEffect.Techniques["WaterShader"];
            waterEffect.Parameters["xTexture"].SetValue(texture);
            waterEffect.Parameters["xWavePos"].SetValue(wavePos);            
            waterEffect.CurrentTechnique.Passes[0].Apply();

            wavePos.X += 0.0001f;
            wavePos.Y += 0.0001f;

            spriteBatch.Draw(waterTexture, new Rectangle(0,0,Game1.GraphicsWidth, Game1.GraphicsHeight), Color.White);

            spriteBatch.End();
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam, RenderTarget2D texture, Matrix transform)
        {
            if (vertices == null) return;
            if (vertices.Length < 0) return;

            basicEffect.Texture = texture;

            basicEffect.View = Matrix.Identity;
            basicEffect.World = cam.ShaderTransform
                * Matrix.CreateOrthographic(Game1.GraphicsWidth, Game1.GraphicsHeight, -1, 1) * 0.5f;
                        
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
