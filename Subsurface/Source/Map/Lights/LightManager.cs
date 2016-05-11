using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma.Lights
{
    class LightManager
    {
        //public static Vector2 ViewPos;
        private static Entity viewTarget;

        public static Entity ViewTarget
        {
            get { return viewTarget; }
            set {
                if (viewTarget == value) return;
                viewTarget = value; 
            }
        }

        public Color AmbientLight;

        RenderTarget2D lightMap, losTexture;
        
        private static Texture2D alphaClearTexture;

        private List<LightSource> lights;

        public bool LosEnabled = true;

        public bool LightingEnabled = true;

        public bool ObstructVision;

        private Texture2D visionCircle;

        public LightManager(GraphicsDevice graphics)
        {
            lights = new List<LightSource>();

            AmbientLight = new Color(60, 60, 60, 255);

            visionCircle = Sprite.LoadTexture("Content/Lights/visioncircle.png");

            var pp = graphics.PresentationParameters;

            lightMap = new RenderTarget2D(graphics, 
                        GameMain.GraphicsWidth, GameMain.GraphicsHeight, false,
                       pp.BackBufferFormat, pp.DepthStencilFormat, pp.MultiSampleCount,
                       RenderTargetUsage.DiscardContents);

            losTexture = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

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

        public void DrawLOS(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Effect effect)
        {            
            if (!LosEnabled || ViewTarget==null) return;

            //Vector2 pos = ViewTarget.WorldPosition;

            //Rectangle camView = new Rectangle(cam.WorldView.X, cam.WorldView.Y - cam.WorldView.Height, cam.WorldView.Width, cam.WorldView.Height);

            //Matrix shadowTransform = cam.ShaderTransform
            //    * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            //graphics.SetRenderTarget(losTexture);
            //graphics.Clear(Color.Transparent);

            //foreach (ConvexHull convexHull in ConvexHull.list)
            //{
            //    if (!convexHull.Intersects(camView)) continue;
            //    //if (!camView.Intersects(convexHull.BoundingBox)) continue;

            //    convexHull.DrawShadows(graphics, cam, pos, shadowTransform);
            //}

            //graphics.SetRenderTarget(null);
            
            //if (!ObstructVision) return;

            //spriteBatch.Begin(SpriteSortMode.Deferred, CustomBlendStates.Multiplicative);
            //spriteBatch.Draw(losTexture, Vector2.Zero);
            //spriteBatch.End();



            spriteBatch.Begin(SpriteSortMode.Deferred, ObstructVision ? CustomBlendStates.Multiplicative : BlendState.AlphaBlend, null, null, null, effect);
            //effect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Draw(losTexture, Vector2.Zero, Color.White);
            spriteBatch.End();  

            ObstructVision = false;

        }

        public void OnMapLoaded()
        {
            foreach (LightSource light in lights)
            {
                light.UpdateHullsInRange();
            }
        }

        public void UpdateLightMap(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam)
        {
            if (!LightingEnabled) return;

            Matrix shadowTransform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            graphics.SetRenderTarget(lightMap);

            Rectangle viewRect = cam.WorldView;
            viewRect.Y -= cam.WorldView.Height;
            
            //clear to some small ambient light
            graphics.Clear(AmbientLight);
            
            foreach (LightSource light in lights)
            {
                if (light.Color.A < 0.01f || light.Range < 1.0f) continue;
                if (light.hullsInRange == null) light.UpdateHullsInRange();
                if (!light.hullsInRange.Any() || !MathUtils.CircleIntersectsRectangle(light.WorldPosition, light.Range, viewRect)) continue;
                            
                //clear alpha to 1
                ClearAlphaToOne(graphics, spriteBatch);
             
                //draw all shadows
                //write only to the alpha channel, which sets alpha to 0
                graphics.RasterizerState = RasterizerState.CullNone;
                graphics.BlendState = CustomBlendStates.WriteToAlpha;

                foreach (ConvexHull ch in light.hullsInRange)
                {
                    //if (!MathUtils.CircleIntersectsRectangle(light.Position, light.Range, ch.BoundingBox)) continue;
                    //draw shadow
                    ch.DrawShadows(graphics, cam, light, shadowTransform, false);
                }

                //draw the light shape
                //where Alpha is 0, nothing will be written
                spriteBatch.Begin(SpriteSortMode.Deferred, CustomBlendStates.MultiplyWithAlpha, null, null, null, null, cam.Transform);
                light.Draw(spriteBatch);
                spriteBatch.End();
            }


            ClearAlphaToOne(graphics, spriteBatch);


            spriteBatch.Begin(SpriteSortMode.Deferred, CustomBlendStates.MultiplyWithAlpha, null, null, null, null, cam.Transform);

            GameMain.ParticleManager.Draw(spriteBatch, false, Particles.ParticleBlendState.Additive);

            foreach (LightSource light in lights)
            {
                if (light.hullsInRange==null || light.hullsInRange.Any() || light.Color.A < 0.01f) continue;
                //if (!MathUtils.CircleIntersectsRectangle(light.WorldPosition, light.Range, viewRect)) continue;

                light.Draw(spriteBatch);
            }

            spriteBatch.End();

            //clear alpha, to avoid messing stuff up later
            ClearAlphaToOne(graphics, spriteBatch);
            
            graphics.SetRenderTarget(null);
        }

        public void UpdateObstructVision(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Vector2 lookAtPosition)
        {
            if (!LosEnabled && !ObstructVision) return;

            graphics.SetRenderTarget(losTexture);

            if (ObstructVision)
            {
                graphics.Clear(Color.Black);

                spriteBatch.Begin(SpriteSortMode.Deferred, null, null, null, null, null, cam.Transform);

                Vector2 diff = lookAtPosition - ViewTarget.WorldPosition;
                diff.Y = -diff.Y;
                float rotation = MathUtils.VectorToAngle(diff);
            
                Vector2 scale = new Vector2(MathHelper.Clamp(diff.Length()/256.0f, 2.0f, 5.0f), 2.0f);

                spriteBatch.Draw(visionCircle, new Vector2(ViewTarget.WorldPosition.X, -ViewTarget.WorldPosition.Y), null, Color.White, rotation, 
                    new Vector2(LightSource.LightTexture.Width*0.2f, LightSource.LightTexture.Height/2), scale, SpriteEffects.None, 0.0f);
                spriteBatch.End();

            }
            else
            {
                graphics.Clear(Color.Transparent);
            }

            //--------------------------------------

            if (LosEnabled && ViewTarget != null)
            {
                Vector2 pos = ViewTarget.WorldPosition;

                Rectangle camView = new Rectangle(cam.WorldView.X, cam.WorldView.Y - cam.WorldView.Height, cam.WorldView.Width, cam.WorldView.Height);

                Matrix shadowTransform = cam.ShaderTransform
                    * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

                foreach (ConvexHull convexHull in ConvexHull.list)
                {
                    if (!convexHull.Intersects(camView)) continue;
                    //if (!camView.Intersects(convexHull.BoundingBox)) continue;

                    convexHull.DrawShadows(graphics, cam, pos, shadowTransform);
                }
            }

            graphics.SetRenderTarget(null);            
        }

        private void ClearAlphaToOne(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, CustomBlendStates.WriteToAlpha);
            spriteBatch.Draw(alphaClearTexture, new Rectangle(0, 0,graphics.Viewport.Width, graphics.Viewport.Height), Color.White);
            spriteBatch.End();
        }

        public void DrawLightMap(SpriteBatch spriteBatch, Camera cam, Effect effect)
        {
            if (!LightingEnabled) return;

            spriteBatch.Begin(SpriteSortMode.Deferred, CustomBlendStates.Multiplicative, null, null, null, effect);
            //effect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Draw(lightMap, Vector2.Zero, Color.White);            
            spriteBatch.End();
        }



        public void ClearLights()
        {
            lights.Clear();
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
