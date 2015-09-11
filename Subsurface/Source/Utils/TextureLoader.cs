using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using System;
using Microsoft.Xna.Framework;

namespace Subsurface
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
#if WINDOWS
                using (Stream fileStream = File.OpenRead(path))
                    return FromStream(fileStream, preMultiplyAlpha);
#endif
#if LINUX
			    using (Stream fileStream = File.OpenRead(path))
                {
                    var texture = Texture2D.FromStream(_graphicsDevice, fileStream);
                    texture = PreMultiplyAlpha(texture);
                    return texture;
                }
#endif

            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading texture ''"+path+"'' failed!", e);
                return null;
            }

        }

#if WINDOWS
 private static Texture2D FromStream(Stream stream, bool preMultiplyAlpha = true)
        {
            Texture2D texture;

            if (_needsBmp)
            {
                // Load image using GDI because Texture2D.FromStream doesn't support BMP
                using (Image image = Image.FromStream(stream))
                {
                    // Now create a MemoryStream which will be passed to Texture2D after converting to PNG internally
                    using (MemoryStream ms = new MemoryStream())
                    {
                        image.Save(ms, ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);
                        texture = Texture2D.FromStream(_graphicsDevice, ms);
                    }
                }
            }
            else
            {
                texture = Texture2D.FromStream(_graphicsDevice, stream);
            }

            if (preMultiplyAlpha) texture = PreMultiplyAlpha(texture);

            return texture;
        }
#endif

        private static Texture2D PreMultiplyAlpha(Texture2D texture)
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

            return texture;
        }
       
        
        private static readonly BlendState BlendColorBlendState;
        private static readonly BlendState BlendAlphaBlendState;

        private static GraphicsDevice _graphicsDevice;
        private static SpriteBatch _spriteBatch;
        private static bool _needsBmp;
    }
}