using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;
using Barotrauma.IO;
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

        private static volatile bool cancelAll = false;

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

        public static void CancelAll()
        {
            cancelAll = true;
        }

        private static byte[] CompressDxt5(byte[] data, int width, int height)
        {
            var output = new byte[width * height];
            Parallel.For(
                fromInclusive: 0,
                toExclusive: width * height / 16,
                i =>
                {
                    int i4 = i * 4;
                    int inputOffset = (i4 % width + (i4 / width) * 4 * width) * 4;
                    int outputOffset = i * 16;
                    CompressDxt5Block(data, inputOffset, width, output, outputOffset);
                });
            return output;
        }

        private static void CompressDxt5Block(byte[] data, int inputOffset, int width, byte[] output, int outputOffset)
        {
            int r1 = 255, g1 = 255, b1 = 255, a1 = 255;
            int r2 = 0, g2 = 0, b2 = 0, a2 = 0;

            // Determine the two colors to interpolate between:
            // color 1 represents lowest luma, color 2 represents highest luma.
            // Luma is also used to determine which color on the palette
            // most closely resembles each pixel to compress, so we
            // cache our calculations here.
            int y1 = 255000;
            int y2 = 0;
            for (int i = 0; i < 16; i++)
            {
                int pixelOffset = inputOffset + (4 * ((i % 4) + (width * (i >> 2))));
                int r = data[pixelOffset + 0];
                int g = data[pixelOffset + 1];
                int b = data[pixelOffset + 2];
                int a = data[pixelOffset + 3];
                int y = r * 299 + g * 587 + b * 114;
                if (y < y1)
                {
                    r1 = r; g1 = g; b1 = b; y1 = y;
                }
                if (y > y2)
                {
                    r2 = r; g2 = g; b2 = b; y2 = y;
                }
                if (a < a1) { a1 = a; }
                if (a > a2) { a2 = a; }
            }

            //convert the colors to rgb565 (16-bit rgb)
            int r1_565 = r1 >> (8 - 5);
            int g1_565 = g1 >> (8 - 6);
            int b1_565 = b1 >> (8 - 5);

            int r2_565 = r2 >> (8 - 5);
            int g2_565 = g2 >> (8 - 6);
            int b2_565 = b2 >> (8 - 5);

            int y2y1Diff = y2 - y1;
            if (y2y1Diff > 0 || a1 < a2)
            {
                for (int i = 0; i < 16; i++)
                {
                    int pixelOffset = inputOffset + (4 * ((i % 4) + (width * (i >> 2))));
                    int r = data[pixelOffset + 0];
                    int g = data[pixelOffset + 1];
                    int b = data[pixelOffset + 2];

                    if (a1 < a2)
                    {
                        int a = data[pixelOffset + 3];
                        a -= a1;
                        a = (a * 0x7) / (a2 - a1);
                        if (a < 0x7)
                        {
                            a = a switch
                            {
                                0 => 1,
                                1 => 7,
                                _ => 8 - a
                            };
                            NetBitWriter.WriteByte((byte)a, 3, output, (outputOffset * 8) + 16 + (i * 3));
                        }
                    }

                    if (y2y1Diff <= 0) { continue; }

                    int y = r * 299 + g * 587 + b * 114;
                    int diffY = y - y1;
                    int paletteIndex = (diffY * 4) / y2y1Diff;
                    paletteIndex = paletteIndex switch
                    {
                        0 => 0,
                        1 => 2,
                        2 => 3,
                        _ => 1
                    };
                    output[outputOffset + 12 + (i / 4)] |= (byte)(paletteIndex << (2 * (i % 4)));
                }
            }

            output[outputOffset + 0] = (byte)a2;
            output[outputOffset + 1] = (byte)a1;

            output[outputOffset + 9] = (byte)((r1_565 << 3) | (g1_565 >> 3));
            output[outputOffset + 8] = (byte)((g1_565 << 5) | b1_565);
            output[outputOffset + 11] = (byte)((r2_565 << 3) | (g2_565 >> 3));
            output[outputOffset + 10] = (byte)((g2_565 << 5) | b2_565);
        }

        public static Texture2D FromFile(string path, bool compress = true, bool mipmap = false, ContentPackage contentPackage = null)
        {
            using FileStream fileStream = File.OpenRead(path);
            return FromStream(fileStream, path, compress, mipmap, contentPackage);
        }

        public static Texture2D FromStream(System.IO.Stream stream, string path = null, bool compress = true, bool mipmap = false, ContentPackage contentPackage = null)
        {
            try
            {
                path = path.CleanUpPath();
                byte[] textureData = null;
                textureData = Texture2D.TextureDataFromStream(stream, out int width, out int height, out int channels);

                SurfaceFormat format = SurfaceFormat.Color;
                if (GameSettings.CurrentConfig.Graphics.CompressTextures && compress)
                {
                    if (((width & 0x03) == 0) && ((height & 0x03) == 0))
                    {
                        textureData = CompressDxt5(textureData, width, height);
                        format = SurfaceFormat.Dxt5;
                        mipmap = false;
                    }
                    else
                    {
                        DebugConsole.AddWarning($"Could not compress a texture because the dimensions aren't a multiple of 4 (path: {path ?? "null"}, size: {width}x{height})",
                            contentPackage);
                    }
                }

                Texture2D tex = null;
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    if (cancelAll) { return; }
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
