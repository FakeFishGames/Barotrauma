using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Barotrauma.IO;
using System.Threading.Tasks;
using Lidgren.Network;
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

        private static byte[] CompressDxt5(byte[] data, int width, int height)
        {
            using (System.IO.MemoryStream mstream = new System.IO.MemoryStream())
            {
                for (int y = 0; y < height; y += 4)
                {
                    for (int x = 0; x < width; x += 4)
                    {
                        int offset = x * 4 + y * 4 * width;
                        CompressDxt5Block(data, offset, width, mstream);
                    }
                }
                return mstream.ToArray();
            }
        }

        private static void CompressDxt5Block(byte[] data, int offset, int width, System.IO.Stream output)
        {
            int r1 = 255, g1 = 255, b1 = 255, a1 = 255;
            int r2 = 0, g2 = 0, b2 = 0, a2 = 0;

            //determine the two colors to interpolate between:
            //color 1 represents lowest luma, color 2 represents highest luma
            for (int i = 0; i < 16; i++)
            {
                int pixelOffset = offset + (4 * ((i % 4) + (width * (i >> 2))));
                int r, g, b, a;
                r = data[pixelOffset + 0];
                g = data[pixelOffset + 1];
                b = data[pixelOffset + 2];
                a = data[pixelOffset + 3];
                if (r * 299 + g * 587 + b * 114 < r1 * 299 + g1 * 587 + b1 * 114)
                {
                    r1 = r; g1 = g; b1 = b;
                }
                if (r * 299 + g * 587 + b * 114 > r2 * 299 + g2 * 587 + b2 * 114)
                {
                    r2 = r; g2 = g; b2 = b;
                }
                if (a < a1) { a1 = a; }
                if (a > a2) { a2 = a; }
            }

            //convert the colors to rgb565 (16-bit rgb)
            int r1_565 = (r1 * 0x1f) / 0xff; if (r1_565 > 0x1f) { r1_565 = 0x1f; }
            int g1_565 = (g1 * 0x3f) / 0xff; if (g1_565 > 0x3f) { g1_565 = 0x3f; }
            int b1_565 = (b1 * 0x1f) / 0xff; if (b1_565 > 0x1f) { b1_565 = 0x1f; }

            int r2_565 = (r2 * 0x1f) / 0xff; if (r2_565 > 0x1f) { r2_565 = 0x1f; }
            int g2_565 = (g2 * 0x3f) / 0xff; if (g2_565 > 0x3f) { g2_565 = 0x3f; }
            int b2_565 = (b2 * 0x1f) / 0xff; if (b2_565 > 0x1f) { b2_565 = 0x1f; }

            //luma is also used to determine which color on the palette
            //most closely resembles each pixel to compress, so we
            //calculate this here
            int y1 = r1 * 299 + g1 * 587 + b1 * 114;
            int y2 = r2 * 299 + g2 * 587 + b2 * 114;

            byte[] newData = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                int pixelOffset = offset + (4 * ((i % 4) + (width * (i >> 2))));
                int r, g, b, a;
                r = data[pixelOffset + 0];
                g = data[pixelOffset + 1];
                b = data[pixelOffset + 2];
                a = data[pixelOffset + 3];

                if (a1 < a2)
                {
                    a -= a1;
                    a = (a * 0x7) / (a2 - a1);
                    if (a > 0x7) { a = 0x7; }

                    switch (a)
                    {
                        case 0:
                            a = 1;
                            break;
                        case 1:
                            a = 7;
                            break;
                        case 2:
                            a = 6;
                            break;
                        case 3:
                            a = 5;
                            break;
                        case 4:
                            a = 4;
                            break;
                        case 5:
                            a = 3;
                            break;
                        case 6:
                            a = 2;
                            break;
                        case 7:
                            a = 0;
                            break;
                    }
                }
                else
                {
                    a = 0;
                }

                NetBitWriter.WriteUInt32((uint)a, 3, newData, 16 + (i * 3));

                int y = r * 299 + g * 587 + b * 114;

                int max = y2 - y1;
                int diffY = y - y1;

                int paletteIndex;
                if (diffY < max / 4)
                {
                    paletteIndex = 0;
                }
                else if (diffY < max / 2)
                {
                    paletteIndex = 2;
                }
                else if (diffY < max * 3 / 4)
                {
                    paletteIndex = 3;
                }
                else
                {
                    paletteIndex = 1;
                }
                newData[12 + (i / 4)] |= (byte)(paletteIndex << (2 * (i % 4)));
            }

            newData[0] = (byte)a2;
            newData[1] = (byte)a1;

            newData[9] = (byte)((r1_565 << 3) | (g1_565 >> 3));
            newData[8] = (byte)((g1_565 << 5) | b1_565);
            newData[11] = (byte)((r2_565 << 3) | (g2_565 >> 3));
            newData[10] = (byte)((g2_565 << 5) | b2_565);

            output.Write(newData, 0, 16);
        }

        public static Texture2D FromFile(string path, bool compress = true, bool mipmap = false)
        {
            using (FileStream fileStream = File.OpenRead(path))
            {
                return FromStream(fileStream, path, compress, mipmap);
            }
        }

        public static Texture2D FromStream(System.IO.Stream stream, string path = null, bool compress = true, bool mipmap = false)
        {
            try
            {
                path = path.CleanUpPath();
                byte[] textureData = null;
                textureData = Texture2D.TextureDataFromStream(stream, out int width, out int height, out int channels);

                SurfaceFormat format = SurfaceFormat.Color;
                if (GameMain.Config.TextureCompressionEnabled && compress)
                {
                    if (((width & 0x03) == 0) && ((height & 0x03) == 0))
                    {
                        textureData = CompressDxt5(textureData, width, height);
                        format = SurfaceFormat.Dxt5;
                        mipmap = false;
                    }
                    else
                    {
                        DebugConsole.NewMessage($"Could not compress a texture because the dimensions aren't a multiple of 4 (path: {path ?? "null"}, size: {width}x{height})", Color.Orange);
                    }
                }

                Texture2D tex = null;
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    tex = new Texture2D(_graphicsDevice, width, height, mipmap, format);
                    tex.SetData(textureData);
                });
                return tex;
            }
            catch (Exception e)
            {
#if WINDOWS
                if (e is SharpDX.SharpDXException) { throw; }
#endif

                DebugConsole.ThrowError(string.IsNullOrEmpty(path) ? "Loading texture from stream failed!" :
                                                                     "Loading texture \"" + path + "\" failed!", e);
                return null;
            }
        }
        
        private static GraphicsDevice _graphicsDevice;
    }
}
