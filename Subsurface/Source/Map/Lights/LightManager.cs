using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Diagnostics;

namespace Subsurface.Lights
{
    class LightManager
    {
        public static Vector2 ViewPos;

        public Color AmbientLight;

        RenderTarget2D lightMap;

        private static Texture2D alphaClearTexture;

        private List<LightSource> lights;

        public bool LosEnabled = true;

        public bool LightingEnabled = true;

        public RenderTarget2D LightMap
        {
            get { return lightMap; }
        }

        public LightManager(GraphicsDevice graphics)
        {
            lights = new List<LightSource>();

            AmbientLight = new Color(80, 80, 80, 255);

            var pp = graphics.PresentationParameters;

            lightMap = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight, false,
                       pp.BackBufferFormat, pp.DepthStencilFormat, pp.MultiSampleCount,
                       RenderTargetUsage.DiscardContents);


            if (alphaClearTexture==null)
            {
                alphaClearTexture = TextureLoader.FromFile("Content/Lights/alphaOne.png");
            }
        }

        public void AddLight(LightSource light)
        {
            lights.Add(light);
        }

        public void RemoveLight(LightSource light)
        {
            lights.Remove(light);
        }

        public void DrawLOS(GraphicsDevice graphics, Camera cam, Vector2 pos)
        {
            Rectangle camView = new Rectangle(cam.WorldView.X, cam.WorldView.Y - cam.WorldView.Height, cam.WorldView.Width, cam.WorldView.Height);

            Matrix shadowTransform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            if (!LosEnabled) return;
            foreach (ConvexHull convexHull in ConvexHull.list)
            {
                if (!camView.Intersects(convexHull.BoundingBox)) continue;

                convexHull.DrawShadows(graphics, cam, pos, shadowTransform);
            }

        }

        public void DrawLightmap(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam)
        {
            Matrix shadowTransform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            graphics.SetRenderTarget(lightMap);

            Rectangle viewRect = cam.WorldView;
            viewRect.Y -= cam.WorldView.Height;

            //clear to some small ambient light
            graphics.Clear(AmbientLight);
            
            foreach (LightSource light in lights)
            {
                if (light.Color.A < 0.01f || light.Range < 0.01f) continue;
                //clear alpha to 1
                ClearAlphaToOne(graphics, spriteBatch);   
             
                if (!MathUtils.CircleIntersectsRectangle(light.Position, light.Range, viewRect)) continue;
                
                //draw all shadows
                //write only to the alpha channel, which sets alpha to 0
                graphics.RasterizerState = RasterizerState.CullNone;
                graphics.BlendState = CustomBlendStates.WriteToAlpha;

                foreach (ConvexHull ch in ConvexHull.list)
                {
                    if (!MathUtils.CircleIntersectsRectangle(light.Position, light.Range, ch.BoundingBox)) continue;
                    //draw shadow
                    ch.DrawShadows(graphics, cam, light.Position, shadowTransform, false);
                }

                //draw the light shape
                //where Alpha is 0, nothing will be written
                spriteBatch.Begin(SpriteSortMode.Immediate, CustomBlendStates.MultiplyWithAlpha, null, null, null, null, cam.Transform);
                light.Draw(spriteBatch);
                spriteBatch.End();
            }
            //clear alpha, to avoid messing stuff up later
            ClearAlphaToOne(graphics, spriteBatch);
            graphics.SetRenderTarget(null);
        }

        private void ClearAlphaToOne(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, CustomBlendStates.WriteToAlpha);
            spriteBatch.Draw(alphaClearTexture, new Rectangle(0, 0,graphics.Viewport.Width, graphics.Viewport.Height), Color.White);
            spriteBatch.End();
        }
    }


    class CustomBlendStates
    {
        static CustomBlendStates()
        {
            Multiplicative = new BlendState();
            Multiplicative.ColorSourceBlend = Multiplicative.AlphaSourceBlend = Blend.Zero;
            Multiplicative.ColorDestinationBlend = Multiplicative.AlphaDestinationBlend = Blend.SourceColor;
            Multiplicative.ColorBlendFunction = Multiplicative.AlphaBlendFunction = BlendFunction.Add;

            WriteToAlpha = new BlendState();
            WriteToAlpha.ColorWriteChannels = ColorWriteChannels.Alpha;

            MultiplyWithAlpha = new BlendState();
            MultiplyWithAlpha.ColorDestinationBlend = MultiplyWithAlpha.AlphaDestinationBlend = Blend.One;
            MultiplyWithAlpha.ColorSourceBlend = MultiplyWithAlpha.AlphaSourceBlend = Blend.DestinationAlpha;
        }
        public static BlendState Multiplicative { get; private set; }
        public static BlendState WriteToAlpha { get; private set; }
        public static BlendState MultiplyWithAlpha { get; private set; }



    }

}
