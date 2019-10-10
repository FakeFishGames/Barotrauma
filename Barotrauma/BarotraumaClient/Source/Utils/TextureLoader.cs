using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Threading.Tasks;
using Color = Microsoft.Xna.Framework.Color;

namespace Barotrauma
{
    /// <summary>
    /// Based on http://jakepoz.com/jake_poznanski__background_load_xna.html 
    /// </summary>
    public static class TextureLoader
    {
        public static Texture2D PlaceHolderTexture
        {
            get;
            private set;
        }

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
            
            Color[] data = new Color[32 * 32];
            for (int i = 0; i < 32 * 32; i++)
            {
                data[i] = Color.Magenta;
            }

            CrossThread.RequestExecutionOnMainThread(() =>
            {
                PlaceHolderTexture = new Texture2D(graphicsDevice, 32, 32);
                PlaceHolderTexture.SetData(data);
            });
        }

        public static Texture2D FromFile(string path, bool preMultiplyAlpha = true)
        {
            try
            {
                using (Stream fileStream = File.OpenRead(path))
                {
                    return FromStream(fileStream, preMultiplyAlpha, path);
                }

            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading texture \"" + path + "\" failed!", e);
                return null;
            }
        }

        public static Texture2D FromStream(Stream fileStream, bool preMultiplyAlpha = true, string path=null)
        {
            try
            {
                int width = 0; int height = 0; int channels = 0;
                byte[] textureData = null;
                textureData = Texture2D.TextureDataFromStream(fileStream, out width, out height, out channels);
                if (preMultiplyAlpha)
                {
                    PreMultiplyAlpha(ref textureData);
                }
                Texture2D tex = null;
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    tex = new Texture2D(_graphicsDevice, width, height);
                    tex.SetData(textureData);
                });
                return tex;
            }
            catch (Exception e)
            {
#if WINDOWS
                if (e is SharpDX.SharpDXException) { throw; }
#endif

                DebugConsole.ThrowError("Loading texture from stream failed!", e);
                return null;
            }
        }

        private static void PreMultiplyAlpha(ref byte[] data)
        {
            for (int i = 0; i < data.Length; i+=4)
            {
                uint a = data[i+3];
                if (a == 0)
                {
                    data[i + 0] = 0;
                    data[i + 1] = 0;
                    data[i + 2] = 0;
                    continue;
                }
                else if (a == uint.MaxValue)
                {
                    continue;
                }
                uint r = data[i+0];
                uint g = data[i+1];
                uint b = data[i+2];
                // Monogame 3.7 needs the line below.
                a *= a; a /= 255;
                b *= a; b /= 255;
                g *= a; g /= 255;
                r *= a; r /= 255;
                data[i + 0] = (byte)r;
                data[i + 1] = (byte)g;
                data[i + 2] = (byte)b;
                data[i + 3] = (byte)a;
            }
        }
       
        
        private static readonly BlendState BlendColorBlendState;
        private static readonly BlendState BlendAlphaBlendState;

        private static GraphicsDevice _graphicsDevice;
        private static SpriteBatch _spriteBatch;
        private static bool _needsBmp;
    }
}
