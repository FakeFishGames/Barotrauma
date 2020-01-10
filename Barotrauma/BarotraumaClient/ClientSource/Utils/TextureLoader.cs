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

        public static void Init(GraphicsDevice graphicsDevice, bool needsBmp = false)
        {
            _graphicsDevice = graphicsDevice;

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

        public static Texture2D FromFile(string path, bool mipmap=false)
        {
            path = path.CleanUpPath();
            try
            {
                using (Stream fileStream = File.OpenRead(path))
                {
                    return FromStream(fileStream, path, mipmap);
                }

            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Loading texture \"" + path + "\" failed!", e);
                return null;
            }
        }

        public static Texture2D FromStream(Stream fileStream, string path=null, bool mipmap=false)
        {
            try
            {
                int width = 0; int height = 0; int channels = 0;
                byte[] textureData = null;
                textureData = Texture2D.TextureDataFromStream(fileStream, out width, out height, out channels);

                Texture2D tex = null;
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    tex = new Texture2D(_graphicsDevice, width, height, mipmap, SurfaceFormat.Color);
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
        
        private static GraphicsDevice _graphicsDevice;
    }
}
