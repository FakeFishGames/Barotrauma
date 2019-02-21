using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Threading;
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
                        PreMultiplyAlpha(ref texture);
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
                PreMultiplyAlpha(ref texture);
                return texture;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading texture from stream failed!", e);
                return null;
            }
        }

        private static void PreMultiplyAlpha(ref Texture2D texture)
        {
            UInt32[] data = new UInt32[texture.Width * texture.Height];
            texture.GetData(data);

            for (int i = 0; i < data.Length; i++)
            {
                uint a = (data[i] & 0xff000000) >> 24;
                if (a == 0)
                {
                    data[i] = 0;
                    continue;
                }
                else if (a == uint.MaxValue)
                {
                    continue;
                }
                uint r = (data[i] & 0x00ff0000) >> 16;
                uint g = (data[i] & 0x0000ff00) >> 8;
                uint b = (data[i] & 0x000000ff);
                // Monogame 3.7 needs the line below.
                a *= a; a /= 255;
                b *= a; b /= 255;
                g *= a; g /= 255;
                r *= a; r /= 255;
                data[i] = (a << 24) | (r << 16) | (g << 8) | b;
            }
            
            //not sure why this is needed, but it seems to cut the memory usage of the game almost in half
            //GetData/SetData might be leaking memory?
            int width = texture.Width; int height = texture.Height;
            texture.Dispose();
            texture = new Texture2D(_graphicsDevice, width, height);
            texture.SetData(data);
        }
       
        
        private static readonly BlendState BlendColorBlendState;
        private static readonly BlendState BlendAlphaBlendState;

        private static GraphicsDevice _graphicsDevice;
        private static SpriteBatch _spriteBatch;
        private static bool _needsBmp;
    }
}