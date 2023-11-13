using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    struct WaterVertexData
    {
        float DistortStrengthX;
        float DistortStrengthY;
        float WaterColorStrength;
        float WaterAlpha;

        public WaterVertexData(float distortStrengthX, float distortStrengthY, float waterColorStrength, float waterAlpha)
        {
            DistortStrengthX = distortStrengthX;
            DistortStrengthY = distortStrengthY;
            WaterColorStrength = waterColorStrength;
            WaterAlpha = waterAlpha;
        }

        public static implicit operator Color(WaterVertexData wd)
        {
            return new Color(wd.DistortStrengthX, wd.DistortStrengthY, wd.WaterColorStrength, wd.WaterAlpha);
        }
    }

    class WaterRenderer : IDisposable
    {
        public static WaterRenderer Instance;

        public const int DefaultBufferSize = 2000;
        public const int DefaultIndoorsBufferSize = 3000;

        public static Vector2 DistortionScale = new Vector2(2f, 1.5f);
        public static Vector2 DistortionStrength = new Vector2(0.01f, 0.33f);
        public static float BlurAmount = 0.0f;

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
        public Dictionary<EntityGrid, VertexPositionColorTexture[]> IndoorsVertices = new Dictionary<EntityGrid, VertexPositionColorTexture[]>();

        public Effect WaterEffect
        {
            get;
            private set;
        }
        private BasicEffect basicEffect;

        public int PositionInBuffer = 0;
        public Dictionary<EntityGrid, int> PositionInIndoorsBuffer = new Dictionary<EntityGrid, int>();

        public Texture2D WaterTexture { get; }

        public WaterRenderer(GraphicsDevice graphicsDevice)
        {
            WaterEffect = EffectLoader.Load("Effects/watershader");

            WaterTexture = TextureLoader.FromFile("Content/Effects/waterbump.png");
            WaterEffect.Parameters["xWaterBumpMap"].SetValue(WaterTexture);
            WaterEffect.Parameters["waterColor"].SetValue(waterColor.ToVector4());

            if (basicEffect == null)
            {
                basicEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                {
                    VertexColorEnabled = false,
                    TextureEnabled = true
                };
            }
        }

        private readonly VertexPositionColorTexture[] tempVertices = new VertexPositionColorTexture[6];
        private readonly Vector3[] tempCorners = new Vector3[4];

        public void RenderWater(SpriteBatch spriteBatch, RenderTarget2D texture, Camera cam)
        {
            spriteBatch.GraphicsDevice.BlendState = BlendState.NonPremultiplied;

            WaterEffect.Parameters["xTexture"].SetValue(texture);
            Vector2 distortionStrength = cam == null ? DistortionStrength : DistortionStrength * cam.Zoom;
            WaterEffect.Parameters["xWaveWidth"].SetValue(distortionStrength.X);
            WaterEffect.Parameters["xWaveHeight"].SetValue(distortionStrength.Y);
            if (BlurAmount > 0.0f)
            {
                WaterEffect.CurrentTechnique = WaterEffect.Techniques["WaterShaderBlurred"];
                WaterEffect.Parameters["xBlurDistance"].SetValue(BlurAmount / 100.0f);
            }
            else
            {
                WaterEffect.CurrentTechnique = WaterEffect.Techniques["WaterShader"];
            }

            Vector2 offset = WavePos;
            if (cam != null)
            {
                offset += (cam.Position - new Vector2(cam.WorldView.Width / 2.0f, -cam.WorldView.Height / 2.0f));
                offset.Y += cam.WorldView.Height;
                offset.X += cam.WorldView.Width;
#if LINUX || OSX
                offset.X += cam.WorldView.Width;
#endif
                offset *= DistortionScale;
            }
            offset.Y = -offset.Y;
            WaterEffect.Parameters["xUvOffset"].SetValue(new Vector2((offset.X / GameMain.GraphicsWidth) % 1.0f, (offset.Y / GameMain.GraphicsHeight) % 1.0f));
            WaterEffect.Parameters["xBumpPos"].SetValue(Vector2.Zero);

            if (cam != null)
            {
                WaterEffect.Parameters["xBumpScale"].SetValue(new Vector2(
                        (float)cam.WorldView.Width / GameMain.GraphicsWidth * DistortionScale.X,
                        (float)cam.WorldView.Height / GameMain.GraphicsHeight * DistortionScale.Y));
                WaterEffect.Parameters["xTransform"].SetValue(cam.ShaderTransform
                    * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f);
                WaterEffect.Parameters["xUvTransform"].SetValue(cam.ShaderTransform
                    * Matrix.CreateOrthographicOffCenter(0, spriteBatch.GraphicsDevice.Viewport.Width * 2, spriteBatch.GraphicsDevice.Viewport.Height * 2, 0, 0, 1) * Matrix.CreateTranslation(0.5f, 0.5f, 0.0f));
            }
            else
            {
                WaterEffect.Parameters["xBumpScale"].SetValue(new Vector2(1.0f, 1.0f));
                WaterEffect.Parameters["xTransform"].SetValue(Matrix.Identity * Matrix.CreateTranslation(-1.0f, 1.0f, 0.0f));
                WaterEffect.Parameters["xUvTransform"].SetValue(Matrix.CreateScale(0.5f, -0.5f, 0.0f));
            }

            WaterEffect.CurrentTechnique.Passes[0].Apply();

            Rectangle view = cam != null ? cam.WorldView : spriteBatch.GraphicsDevice.Viewport.Bounds;

            tempCorners[0] = new Vector3(view.X, view.Y, 0.1f);
            tempCorners[1] = new Vector3(view.Right, view.Y, 0.1f);
            tempCorners[2] = new Vector3(view.Right, view.Y - view.Height, 0.1f);
            tempCorners[3] = new Vector3(view.X, view.Y - view.Height, 0.1f);

            WaterVertexData backGroundColor = new WaterVertexData(0.1f, 0.1f, 0.5f, 1.0f);
            tempVertices[0] = new VertexPositionColorTexture(tempCorners[0], backGroundColor, Vector2.Zero);
            tempVertices[1] = new VertexPositionColorTexture(tempCorners[1], backGroundColor, Vector2.Zero);
            tempVertices[2] = new VertexPositionColorTexture(tempCorners[2], backGroundColor, Vector2.Zero);
            tempVertices[3] = new VertexPositionColorTexture(tempCorners[0], backGroundColor, Vector2.Zero);
            tempVertices[4] = new VertexPositionColorTexture(tempCorners[2], backGroundColor, Vector2.Zero);
            tempVertices[5] = new VertexPositionColorTexture(tempCorners[3], backGroundColor, Vector2.Zero);

            spriteBatch.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, tempVertices, 0, 2);

            foreach (KeyValuePair<EntityGrid, VertexPositionColorTexture[]> subVerts in IndoorsVertices)
            {
                if (!PositionInIndoorsBuffer.ContainsKey(subVerts.Key) || PositionInIndoorsBuffer[subVerts.Key] == 0) { continue; }

                offset = WavePos;
                if (subVerts.Key.Submarine != null) { offset -= subVerts.Key.Submarine.WorldPosition; }
                if (cam != null)
                {
                    offset += cam.Position - new Vector2(cam.WorldView.Width / 2.0f, -cam.WorldView.Height / 2.0f);
                    offset.Y += cam.WorldView.Height;
                    offset.X += cam.WorldView.Width;
                    offset *= DistortionScale;
                }
                offset.Y = -offset.Y;
                WaterEffect.Parameters["xUvOffset"].SetValue(new Vector2((offset.X / GameMain.GraphicsWidth) % 1.0f, (offset.Y / GameMain.GraphicsHeight) % 1.0f));

                WaterEffect.CurrentTechnique.Passes[0].Apply();

                spriteBatch.GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, subVerts.Value, 0, PositionInIndoorsBuffer[subVerts.Key] / 3);
            }

            WaterEffect.Parameters["xTexture"].SetValue((Texture2D)null);
            WaterEffect.CurrentTechnique.Passes[0].Apply();
        }

        public void ScrollWater(Vector2 vel, float deltaTime)
        {
            WavePos = WavePos - vel * deltaTime;
        }

        public void RenderAir(GraphicsDevice graphicsDevice, Camera cam, RenderTarget2D texture, Matrix transform)
        {
            if (vertices == null || vertices.Length < 0 || PositionInBuffer <= 0) return;

            basicEffect.Texture = texture;

            basicEffect.View = Matrix.Identity;
            basicEffect.World = transform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f * Matrix.CreateTranslation(0.0f,0.0f,0f);
            basicEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;
            graphicsDevice.DrawUserPrimitives<VertexPositionTexture>(PrimitiveType.TriangleList, vertices, 0, PositionInBuffer / 3);

            basicEffect.Texture = null;
            basicEffect.CurrentTechnique.Passes[0].Apply();
        }

        private readonly List<EntityGrid> buffersToRemove = new List<EntityGrid>();
        public void ResetBuffers()
        {
            PositionInBuffer = 0;
            PositionInIndoorsBuffer.Clear();
            buffersToRemove.Clear();
            foreach (var buffer in IndoorsVertices.Keys)
            {
                if (buffer.Submarine?.Removed ?? false)
                {
                    buffersToRemove.Add(buffer);
                }
            }
            foreach (var bufferToRemove in buffersToRemove)
            {
                IndoorsVertices.Remove(bufferToRemove);
            }
        }

        public void Dispose()
        {
            if (WaterEffect != null)
            {
                WaterEffect.Dispose();
                WaterEffect = null;
            }

            if (basicEffect != null)
            {
                basicEffect.Dispose();
                basicEffect = null;
            }
        }
    }
}
