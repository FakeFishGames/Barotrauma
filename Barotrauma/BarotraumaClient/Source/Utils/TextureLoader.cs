using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using Color = Microsoft.Xna.Framework.Color;

namespace Barotrauma
{
    /// <summary>
    /// Based on http://jakepoz.com/jake_poznanski__background_load_xna.html 
    /// </summary>
    public static class TextureLoader
    {
        static TextureLoader()
        {

            BlendColorBlendState = new BlendState
            {
                ColorDestinationBlend = Blend.Zero,
                ColorWriteChannels = ColorWriteChannels.Red | ColorWriteChannels.Green | ColorWriteChannels.Blue,
                AlphaDestinationBlend = Blend.Zero,
                AlphaSourceBlend = Blend.SourceAlpha,
                ColorSourceBlend = Blend.SourceAlpha
            };

            BlendAlphaBlendState = new BlendState
            {
                ColorWriteChannels = ColorWriteChannels.Alpha,
                AlphaDestinationBlend = Blend.Zero,
                ColorDestinationBlend = Blend.Zero,
                AlphaSourceBlend = Blend.One,
                ColorSourceBlend = Blend.One
            };
        }

        public static void Init(GraphicsDevice graphicsDevice, bool needsBmp = false)
        {
            _graphicsDevice = graphicsDevice;
            _needsBmp = needsBmp;
            _spriteBatch = new SpriteBatch(_graphicsDevice);
        }

        public static Texture2D FromFile(string path, bool preMultiplyAlpha = true)
        {
            try
            {
                using (Stream fileStream = File.OpenRead(path))
                {
                    var texture = Texture2D.FromStream(_graphicsDevice, fileStream);
                    if (preMultiplyAlpha)
                    {
                        PreMultiplyAlpha(texture);
                    }
                    return texture;
                }

            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading texture \"" + path + "\" failed!", e);
                return null;
            }
        }

        public static Texture2D FromStream(Stream fileStream, bool preMultiplyAlpha = true)
        {
            try
            {
                var texture = Texture2D.FromStream(_graphicsDevice, fileStream);
                PreMultiplyAlpha(texture);
                return texture;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading texture from stream failed!", e);
                return null;
            }
        }

        private static void PreMultiplyAlpha(Texture2D texture)
        {
            // Setup a render target to hold our final texture which will have premulitplied alpha values
            using (RenderTarget2D renderTarget = new RenderTarget2D(_graphicsDevice, texture.Width, texture.Height))
            {
                Viewport viewportBackup = _graphicsDevice.Viewport;
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.Black);

                // Multiply each color by the source alpha, and write in just the color values into the final texture
                _spriteBatch.Begin(SpriteSortMode.Immediate, BlendColorBlendState);
                _spriteBatch.Draw(texture, texture.Bounds, Color.White);
                _spriteBatch.End();

                // Now copy over the alpha values from the source texture to the final one, without multiplying them
                _spriteBatch.Begin(SpriteSortMode.Immediate, BlendAlphaBlendState);
                _spriteBatch.Draw(texture, texture.Bounds, Color.White);
                _spriteBatch.End();

                // Release the GPU back to drawing to the screen
                _graphicsDevice.SetRenderTarget(null);
                _graphicsDevice.Viewport = viewportBackup;

                // Store data from render target because the RenderTarget2D is volatile
                Color[] data = new Color[texture.Width * texture.Height];
                renderTarget.GetData(data);

                // Unset texture from graphic device and set modified data back to it
                _graphicsDevice.Textures[0] = null;
                texture.SetData(data);
            }
        }
       
        
        private static readonly BlendState BlendColorBlendState;
        private static readonly BlendState BlendAlphaBlendState;

        private static GraphicsDevice _graphicsDevice;
        private static SpriteBatch _spriteBatch;
        private static bool _needsBmp;
    }
}