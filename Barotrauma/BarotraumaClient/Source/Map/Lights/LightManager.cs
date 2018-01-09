using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Lights
{
    class LightManager
    {
        private const float AmbientLightUpdateInterval = 0.2f;
        private const float AmbientLightFalloff = 0.8f;

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

        private float lightmapScale = 0.5f;
        public RenderTarget2D lightMap
        {
            get;
            private set;
        }
        public RenderTarget2D losTexture
        {
            get;
            private set;
        }

        BasicEffect lightEffect;

        public Effect losEffect
        {
            get; private set;
        }
        
        private static Texture2D alphaClearTexture;

        private List<LightSource> lights;

        public bool LosEnabled = true;

        public bool LightingEnabled = true;

        public bool ObstructVision;
        LightSource losSource;

        private Sprite visionCircle;
        
        private Dictionary<Hull, Color> hullAmbientLights;
        private Dictionary<Hull, Color> smoothedHullAmbientLights;

        private float ambientLightUpdateTimer;

        public LightManager(GraphicsDevice graphics, ContentManager content)
        {
            lights = new List<LightSource>();

            AmbientLight = new Color(20, 20, 20, 255);

            //visionCircle = Sprite.LoadTexture("Content/Lights/visioncircle.png");
            visionCircle = new Sprite("Content/Lights/visioncircle.png", new Vector2(0.2f, 0.5f));

            var pp = graphics.PresentationParameters;

            lightMap = new RenderTarget2D(graphics, 
                       (int)(GameMain.GraphicsWidth*lightmapScale), (int)(GameMain.GraphicsHeight*lightmapScale), false,
                       pp.BackBufferFormat, pp.DepthStencilFormat, pp.MultiSampleCount,
                       RenderTargetUsage.DiscardContents);

            losTexture = new RenderTarget2D(graphics, (int)(GameMain.GraphicsWidth*lightmapScale), (int)(GameMain.GraphicsHeight*lightmapScale), false, SurfaceFormat.Color, DepthFormat.None);

            losSource = new LightSource(Vector2.Zero, GameMain.GraphicsWidth, Color.White, null, false);
            losSource.texture = new Texture2D(graphics, 1, 1);
            losSource.texture.SetData(new Color[] { Color.White });// fill the texture with white

#if WINDOWS
            losEffect = content.Load<Effect>("losshader");
#else
            losEffect = content.Load<Effect>("losshader_opengl");
#endif

            if (lightEffect == null)
            {
                lightEffect = new BasicEffect(GameMain.Instance.GraphicsDevice);
                lightEffect.VertexColorEnabled = true;

                lightEffect.TextureEnabled = true;
                lightEffect.Texture = LightSource.LightTexture;
            }

            hullAmbientLights = new Dictionary<Hull, Color>();
            smoothedHullAmbientLights = new Dictionary<Hull, Color>();

            if (alphaClearTexture == null)
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

        public void OnMapLoaded()
        {
            foreach (LightSource light in lights)
            {
                light.NeedsHullCheck = true;
                light.NeedsRecalculation = true;
            }
        }

        public void Update(float deltaTime)
        {
            if (ambientLightUpdateTimer > 0.0f)
            {
                ambientLightUpdateTimer -= deltaTime;
            }
            else
            {
                CalculateAmbientLights();

                ambientLightUpdateTimer = AmbientLightUpdateInterval;
            }

            foreach (Hull hull in hullAmbientLights.Keys)
            {
                if (!smoothedHullAmbientLights.ContainsKey(hull))
                {
                    smoothedHullAmbientLights.Add(hull, Color.TransparentBlack);
                }
            }

            foreach (Hull hull in smoothedHullAmbientLights.Keys.ToList())
            {
                Color targetColor = Color.TransparentBlack;

                hullAmbientLights.TryGetValue(hull, out targetColor);

                smoothedHullAmbientLights[hull] = Color.Lerp(smoothedHullAmbientLights[hull], targetColor, deltaTime);
            }
        }

        public void UpdateLightMap(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Effect blur)
        {
            if (!LightingEnabled) return;
            
            graphics.SetRenderTarget(lightMap);

            Rectangle viewRect = cam.WorldView;
            viewRect.Y -= cam.WorldView.Height;
            
            //clear to some small ambient light
            graphics.Clear(AmbientLight);
            graphics.BlendState = BlendState.Additive;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, null, null, null, cam.Transform * Matrix.CreateScale(new Vector3(lightmapScale, lightmapScale, 1.0f)));
            
            Matrix transform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            Vector3 offset = Vector3.Zero;// new Vector3(Submarine.MainSub.DrawPosition.X, Submarine.MainSub.DrawPosition.Y, 0.0f);

            foreach (LightSource light in lights)
            {
                if (light.Color.A < 1 || light.Range < 1.0f || !light.CastShadows || !light.Enabled) continue;
                if (!MathUtils.CircleIntersectsRectangle(light.WorldPosition, light.Range, viewRect)) continue;

                light.Draw(spriteBatch, lightEffect, transform);
            }

            lightEffect.World = Matrix.CreateTranslation(offset) * transform;
            
            GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.Additive);

            if (Character.Controlled != null)
            {
                if (Character.Controlled.FocusedItem != null)
                {
                    Character.Controlled.FocusedItem.IsHighlighted = true;
                    Character.Controlled.FocusedItem.Draw(spriteBatch, false, true);
                    Character.Controlled.FocusedItem.IsHighlighted = true;
                }
                else if (Character.Controlled.FocusedCharacter != null)
                {
                    Character.Controlled.FocusedCharacter.Draw(spriteBatch);
                }
            }

            foreach (Hull hull in smoothedHullAmbientLights.Keys)
            {
                if (smoothedHullAmbientLights[hull].A < 0.01f) continue;

                var drawRect =
                    hull.Submarine == null ?
                    hull.Rect :
                    new Rectangle((int)(hull.Submarine.DrawPosition.X + hull.Rect.X), (int)(hull.Submarine.DrawPosition.Y + hull.Rect.Y), hull.Rect.Width, hull.Rect.Height);

                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X, -drawRect.Y),
                    new Vector2(hull.Rect.Width, hull.Rect.Height),
                    smoothedHullAmbientLights[hull] * 0.5f, true);
            }

            spriteBatch.End();

            //clear alpha, to avoid messing stuff up later
            //ClearAlphaToOne(graphics, spriteBatch);

            graphics.SetRenderTarget(null);
            graphics.BlendState = BlendState.AlphaBlend;
        }

        public void UpdateObstructVision(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Vector2 lookAtPosition)
        {
            if (!LosEnabled || ViewTarget == null) return;

            graphics.SetRenderTarget(losTexture);

            //--------------------------------------

            graphics.Clear(Color.Black);
            if (ObstructVision)
            {
                Vector2 diff = lookAtPosition - ViewTarget.WorldPosition;
                float rotation = MathUtils.VectorToAngle(diff);

                Vector2 scale = new Vector2(MathHelper.Clamp(diff.Length() / 256.0f, 2.0f, 5.0f), 2.0f) * 0.3f;

                visionCircle.size = new Vector2(visionCircle.SourceRect.Width * scale.X, visionCircle.SourceRect.Height * scale.Y);
                losSource.overrideLightTexture = visionCircle;
                losSource.Rotation = rotation;
            }
            else
            {
                losSource.overrideLightTexture = null;
            }

            graphics.BlendState = BlendState.Additive;

            Vector2 pos = ViewTarget.Position;
            losSource.Position = pos;
            losSource.NeedsRecalculation = true;
            losSource.ParentSub = ViewTarget.Submarine;

            Matrix transform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;
            
            losSource.Draw(spriteBatch, lightEffect, transform);

            graphics.BlendState = BlendState.AlphaBlend;

            graphics.SetRenderTarget(null);            
        }
        

        private void CalculateAmbientLights()
        {
            hullAmbientLights.Clear();

            foreach (LightSource light in lights)
            {
                if (light.Color.A < 1f || light.Range < 1.0f) continue;

                var newAmbientLights = AmbientLightHulls(light);
                foreach (Hull hull in newAmbientLights.Keys)
                {
                    if (hullAmbientLights.ContainsKey(hull))
                    {
                        //hull already lit by some other light source -> add the ambient lights up
                        hullAmbientLights[hull] = new Color(
                            hullAmbientLights[hull].R + newAmbientLights[hull].R, 
                            hullAmbientLights[hull].G + newAmbientLights[hull].G, 
                            hullAmbientLights[hull].B + newAmbientLights[hull].B,
                            hullAmbientLights[hull].A + newAmbientLights[hull].A);
                    }
                    else
                    {
                        hullAmbientLights.Add(hull, newAmbientLights[hull]);
                    }
                }
            }
        }

        /// <summary>
        /// Add ambient light to the hull the lightsource is inside + all adjacent hulls connected by a gap
        /// </summary>
        private Dictionary<Hull, Color> AmbientLightHulls(LightSource light)
        {
            Dictionary<Hull, Color> hullAmbientLight = new Dictionary<Hull, Color>();

            var hull = Hull.FindHull(light.WorldPosition);
            if (hull == null) return hullAmbientLight;

            return AmbientLightHulls(hull, hullAmbientLight, light.Color * (light.Range/2000.0f));
        }

        /// <summary>
        /// A flood fill algorithm that adds ambient light to all hulls the starting hull is connected to
        /// </summary>
        private Dictionary<Hull, Color> AmbientLightHulls(Hull hull, Dictionary<Hull, Color> hullAmbientLight, Color currColor)
        {
            if (hullAmbientLight.ContainsKey(hull))
            {
                if (hullAmbientLight[hull].A > currColor.A)
                    return hullAmbientLight;
                else
                    hullAmbientLight[hull] = currColor;
            }
            else
            {
                hullAmbientLight.Add(hull, currColor);
            }

            Color nextHullLight = currColor * AmbientLightFalloff;
            //light getting too dark to notice -> no need to spread further
            if (nextHullLight.A < 20) return hullAmbientLight;

            //use hashset to make sure that each hull is only included once
            HashSet<Hull> hulls = new HashSet<Hull>();
            foreach (Gap g in hull.ConnectedGaps)
            {
                if (!g.IsRoomToRoom || !g.PassAmbientLight || g.Open < 0.5f) continue;
                
                hulls.Add((g.linkedTo[0] == hull ? g.linkedTo[1] : g.linkedTo[0]) as Hull);                
            }

            foreach (Hull h in hulls)
            {
                hullAmbientLight = AmbientLightHulls(h, hullAmbientLight, nextHullLight); 
            }

            return hullAmbientLight;
        }
        
        public void DrawLightMap(SpriteBatch spriteBatch, Effect effect)
        {
            if (!LightingEnabled) return;

            spriteBatch.Begin(SpriteSortMode.Deferred, CustomBlendStates.Multiplicative, null, null, null, null);
            spriteBatch.Draw(lightMap, new Rectangle(0,0,GameMain.GraphicsWidth,GameMain.GraphicsHeight), Color.White);            
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
