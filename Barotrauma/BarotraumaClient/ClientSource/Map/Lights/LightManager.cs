using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using System.Collections.Generic;
using System.Linq;
using System;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma.Lights
{
    class LightManager
    {
        public static Entity ViewTarget { get; set; }

        // Lighting

        private float currLightMapScale;

        public Color AmbientLight;

        public RenderTarget2D LightMap
        {
            get;
            private set;
        }
        public RenderTarget2D LimbLightMap
        {
            get;
            private set;
        }

        private BasicEffect lightEffect;
        public Effect SolidColorEffect { get; private set; }

        private readonly List<LightSource> lights;

        public bool LightingEnabled = true;

        public IEnumerable<LightSource> Lights
        {
            get { return lights; }
        }

        // LoS

        public bool LosEnabled = true;
        public float LosAlpha = 1f;
        public LosMode LosMode = LosMode.Transparent;

        public bool ObstructVision;

        private readonly Texture2D visionCircle;

        private Vector2 losOffset;

        private LosRaycastSettings currLosRaycastSettings;

        public RenderTarget2D LosMap
        {
            get;
            private set;
        }

        public RenderTarget2D LosOcclusionMap
        {
            get;
            private set;
        }
        public RenderTarget2D[] LosRaycastMap
        {
            get;
            private set;
        }
        public RenderTarget2D LosShadownMap
        {
            get;
            private set;
        }

        private readonly Texture2D penumbraLut;

        // A bunch more shaders are used. Might be useful to add these to the same effect?

        public Effect LosRaycastEffect { get; private set; } // Effect for raycasting from a screen position
        public Effect LosShadowEffect { get; private set; } // Effect for adding the shadows back to carthesian coordinates with light
        public Effect LosPenumbraEffect { get; private set; } // Effect for adding the shadows back to carthesian coordinates with light
        public Effect LosEffect { get; private set; } // Effect for combining the LoS texture with the game screen

        // Highlights

        public RenderTarget2D HighlightMap
        {
            get;
            private set;
        }

        private readonly Texture2D highlightRaster;

        public LightManager(GraphicsDevice graphics, ContentManager content)
        {
            lights = new List<LightSource>(100);

            AmbientLight = new Color(20, 20, 20, 255);

            visionCircle = Sprite.LoadTexture("Content/Lights/visioncircle.png");
            penumbraLut = Sprite.LoadTexture("Content/Lights/penumbra_lut.png");
            highlightRaster = Sprite.LoadTexture("Content/UI/HighlightRaster.png");

            GameMain.Instance.ResolutionChanged += () =>
            {
                CreateRenderTargets(graphics);
            };

            CrossThread.RequestExecutionOnMainThread(() =>
            {
                CreateRenderTargets(graphics);

#if WINDOWS
                LosEffect = content.Load<Effect>("Effects/losshader");
                LosRaycastEffect = content.Load<Effect>("Effects/losraycast");
                LosShadowEffect = content.Load<Effect>("Effects/losshadow");
                LosPenumbraEffect = content.Load<Effect>("Effects/lospenumbra");
                SolidColorEffect = content.Load<Effect>("Effects/solidcolor");
#else
                LosEffect = content.Load<Effect>("Effects/losshader_opengl");
                SolidColorEffect = content.Load<Effect>("Effects/solidcolor_opengl");
#endif

                if (lightEffect == null)
                {
                    lightEffect = new BasicEffect(GameMain.Instance.GraphicsDevice)
                    {
                        VertexColorEnabled = true,
                        TextureEnabled = true,
                        Texture = LightSource.LightTexture
                    };
                }
            });

            LosRaycastMap = new RenderTarget2D[2];

            currLosRaycastSettings = new LosRaycastSettings();
            CreateLosRendertargets(graphics);
        }

        private void CreateRenderTargets(GraphicsDevice graphics)
        {

            var pp = graphics.PresentationParameters;

            currLightMapScale = GameMain.Config.LightMapScale;

            LightMap?.Dispose();
            LightMap = CreateRenderTarget();

            LimbLightMap?.Dispose();
            LimbLightMap = CreateRenderTarget();

            HighlightMap?.Dispose();
            HighlightMap = CreateRenderTarget();

            RenderTarget2D CreateRenderTarget()
            {
               return new RenderTarget2D(graphics,
                       (int)(GameMain.GraphicsWidth * GameMain.Config.LightMapScale), (int)(GameMain.GraphicsHeight * GameMain.Config.LightMapScale), false,
                       pp.BackBufferFormat, pp.DepthStencilFormat, pp.MultiSampleCount,
                       RenderTargetUsage.DiscardContents);
            }
        }
        private void CreateLosRendertargets(GraphicsDevice graphics)
        {
            currLosRaycastSettings.losTexScale = GameMain.Config.LosRaycastSetting.losTexScale;
            currLosRaycastSettings.RayStepIterations = GameMain.Config.LosRaycastSetting.RayStepIterations;
            currLosRaycastSettings.RayCount = GameMain.Config.LosRaycastSetting.RayCount;

            LosMap?.Dispose();
            LosMap = new RenderTarget2D(graphics,
                (int)(GameMain.GraphicsWidth * GameMain.Config.LosRaycastSetting.losTexScale),
                (int)(GameMain.GraphicsHeight * GameMain.Config.LosRaycastSetting.losTexScale), false, SurfaceFormat.Color, DepthFormat.None);

            // TODO: Disposal/creation of these RTs based on LoS mode to save memory (though it's already very limited)

            LosOcclusionMap?.Dispose();
            LosRaycastMap[0]?.Dispose();
            LosRaycastMap[1]?.Dispose();
            LosShadownMap?.Dispose();

            LosOcclusionMap = new RenderTarget2D(graphics,
                (int)(GameMain.GraphicsWidth * GameMain.Config.LosRaycastSetting.losTexScale),
                (int)(GameMain.GraphicsHeight * GameMain.Config.LosRaycastSetting.losTexScale), false, SurfaceFormat.Color, DepthFormat.None);

            LosRaycastMap[0] = new RenderTarget2D(graphics,
                (int)(GameMain.Config.LosRaycastSetting.RayCount), 1, false, SurfaceFormat.Color, DepthFormat.None);
            LosRaycastMap[1] = new RenderTarget2D(graphics,
                (int)(GameMain.Config.LosRaycastSetting.RayCount), 1, false, SurfaceFormat.Color, DepthFormat.None);

            LosShadownMap = new RenderTarget2D(graphics,
                (int)(GameMain.Config.LosRaycastSetting.RayCount),
                (int)(GameMain.Config.LosRaycastSetting.RayStepIterations * 64), false, SurfaceFormat.Color, DepthFormat.None);
        }

        public void AddLight(LightSource light)
        {
            if (!lights.Contains(light)) { lights.Add(light); }
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

        private readonly List<LightSource> activeLights = new List<LightSource>(capacity: 100);

        public void Update(float deltaTime)
        {
            foreach (LightSource light in activeLights)
            {
                if (!light.Enabled) { continue; }
                light.Update(deltaTime);
            }
        }

        public void RenderLightMap(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, RenderTarget2D backgroundObstructor = null)
        {
            if (!LightingEnabled) { return; }

            if (Math.Abs(currLightMapScale - GameMain.Config.LightMapScale) > 0.01f)
            {
                //lightmap scale has changed -> recreate render targets
                CreateRenderTargets(graphics);
            }

            Matrix spriteBatchTransform = cam.Transform * Matrix.CreateScale(new Vector3(GameMain.Config.LightMapScale, GameMain.Config.LightMapScale, 1.0f));
            Matrix transform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            bool highlightsVisible = UpdateHighlights(graphics, spriteBatch, spriteBatchTransform, cam);

            Rectangle viewRect = cam.WorldView;
            viewRect.Y -= cam.WorldView.Height;
            //check which lights need to be drawn
            activeLights.Clear();
            foreach (LightSource light in lights)
            {
                if (!light.Enabled) { continue; }
                if ((light.Color.A < 1 || light.Range < 1.0f) && !light.LightSourceParams.OverrideLightSpriteAlpha.HasValue) { continue; }
                if (light.ParentBody != null)
                {
                    light.Position = light.ParentBody.DrawPosition;
                    if (light.ParentSub != null) { light.Position -= light.ParentSub.DrawPosition; }
                }

                float range = light.LightSourceParams.TextureRange;
                if (light.LightSprite != null)
                {
                    float spriteRange = Math.Max(
                        light.LightSprite.size.X * light.SpriteScale.X * (0.5f + Math.Abs(light.LightSprite.RelativeOrigin.X - 0.5f)),
                        light.LightSprite.size.Y * light.SpriteScale.Y * (0.5f + Math.Abs(light.LightSprite.RelativeOrigin.Y - 0.5f)));

                    float targetSize = Math.Max(light.LightTextureTargetSize.X, light.LightTextureTargetSize.Y);
                    range = Math.Max(Math.Max(spriteRange, targetSize), range);
                }
                if (!MathUtils.CircleIntersectsRectangle(light.WorldPosition, range, viewRect)) { continue; }
                activeLights.Add(light);
            }

            //draw light sprites attached to characters
            //render into a separate rendertarget using alpha blending (instead of on top of everything else with alpha blending)
            //to prevent the lights from showing through other characters or other light sprites attached to the same character
            //---------------------------------------------------------------------------------------------------
            graphics.SetRenderTarget(LimbLightMap);
            graphics.Clear(Color.Black);
            graphics.BlendState = BlendState.NonPremultiplied;
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.NonPremultiplied, transformMatrix: spriteBatchTransform);
            foreach (LightSource light in activeLights)
            {
                if (light.IsBackground || light.CurrentBrightness <= 0.0f) { continue; }
                //draw limb lights at this point, because they were skipped over previously to prevent them from being obstructed
                if (light.ParentBody?.UserData is Limb limb && !limb.Hide) { light.DrawSprite(spriteBatch, cam); }
            }
            spriteBatch.End();

            //draw background lights
            //---------------------------------------------------------------------------------------------------
            graphics.SetRenderTarget(LightMap);
            graphics.Clear(AmbientLight);
            graphics.BlendState = BlendState.Additive;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
            Level.Loaded?.BackgroundCreatureManager?.DrawLights(spriteBatch, cam);
            foreach (LightSource light in activeLights)
            {
                if (!light.IsBackground || light.CurrentBrightness <= 0.0f) { continue; }
                light.DrawSprite(spriteBatch, cam);
                light.DrawLightVolume(spriteBatch, lightEffect, transform);
            }
            GameMain.ParticleManager.Draw(spriteBatch, true, null, Particles.ParticleBlendState.Additive);
            spriteBatch.End();

            //draw a black rectangle on hulls to hide background lights behind subs
            //---------------------------------------------------------------------------------------------------

            /*if (backgroundObstructor != null)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
                spriteBatch.Draw(backgroundObstructor, new Rectangle(0, 0,
                    (int)(GameMain.GraphicsWidth * currLightMapScale), (int)(GameMain.GraphicsHeight * currLightMapScale)), Color.Black);
                spriteBatch.End();
            }*/

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, transformMatrix: spriteBatchTransform);
            Dictionary<Hull, Rectangle> visibleHulls = GetVisibleHulls(cam);
            foreach (KeyValuePair<Hull, Rectangle> hull in visibleHulls)
            {
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(hull.Value.X, -hull.Value.Y),
                    new Vector2(hull.Value.Width, hull.Value.Height),
                    hull.Key.AmbientLight == Color.TransparentBlack ? Color.Black : hull.Key.AmbientLight.Multiply(hull.Key.AmbientLight.A / 255.0f), true);
            }
            spriteBatch.End();

            SolidColorEffect.CurrentTechnique = SolidColorEffect.Techniques["SolidColor"];
            SolidColorEffect.Parameters["color"].SetValue(AmbientLight.Opaque().ToVector4());
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, transformMatrix: spriteBatchTransform, effect: SolidColorEffect);
            Submarine.DrawDamageable(spriteBatch, null);
            spriteBatch.End();

            graphics.BlendState = BlendState.Additive;


            //draw the focused item and character to highlight them,
            //and light sprites (done before drawing the actual light volumes so we can make characters obstruct the highlights and sprites)
            //---------------------------------------------------------------------------------------------------
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
            foreach (LightSource light in activeLights)
            {
                //don't draw limb lights at this point, they need to be drawn after lights have been obstructed by characters
                if (light.IsBackground || light.ParentBody?.UserData is Limb || light.CurrentBrightness <= 0.0f) { continue; }
                light.DrawSprite(spriteBatch, cam);
            }
            spriteBatch.End();

            if (highlightsVisible)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive);
                spriteBatch.Draw(HighlightMap, Vector2.Zero, Color.White);
                spriteBatch.End();
            }

            //draw characters to obstruct the highlighted items/characters and light sprites
            //---------------------------------------------------------------------------------------------------

            SolidColorEffect.CurrentTechnique = SolidColorEffect.Techniques["SolidVertexColor"];
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, effect: SolidColorEffect, transformMatrix: spriteBatchTransform);
            foreach (Character character in Character.CharacterList)
            {
                if (character.CurrentHull == null || !character.Enabled || !character.IsVisible) { continue; }
                if (Character.Controlled?.FocusedCharacter == character) { continue; }
                Color lightColor = character.CurrentHull.AmbientLight == Color.TransparentBlack ?
                    Color.Black :
                    character.CurrentHull.AmbientLight.Multiply(character.CurrentHull.AmbientLight.A / 255.0f).Opaque();
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb.DeformSprite != null) { continue; }
                    limb.Draw(spriteBatch, cam, lightColor);
                }
            }
            spriteBatch.End();

            DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShaderSolidVertexColor"];
            DeformableSprite.Effect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, transformMatrix: spriteBatchTransform);
            foreach (Character character in Character.CharacterList)
            {
                if (character.CurrentHull == null || !character.Enabled || !character.IsVisible) { continue; }
                if (Character.Controlled?.FocusedCharacter == character) { continue; }
                Color lightColor = character.CurrentHull.AmbientLight == Color.TransparentBlack ?
                    Color.Black :
                    character.CurrentHull.AmbientLight.Multiply(character.CurrentHull.AmbientLight.A / 255.0f).Opaque();
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (limb.DeformSprite == null) { continue; }
                    limb.Draw(spriteBatch, cam, lightColor);
                }
            }
            spriteBatch.End();
            DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShader"];
            graphics.BlendState = BlendState.Additive;

            //draw the actual light volumes, additive particles, hull ambient lights and the halo around the player
            //---------------------------------------------------------------------------------------------------
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);

            spriteBatch.Draw(LimbLightMap, new Rectangle(cam.WorldView.X, -cam.WorldView.Y, cam.WorldView.Width, cam.WorldView.Height), Color.White);

            foreach (ElectricalDischarger discharger in ElectricalDischarger.List)
            {
                discharger.DrawElectricity(spriteBatch);
            }

            foreach (LightSource light in activeLights)
            {
                if (light.IsBackground || light.CurrentBrightness <= 0.0f) { continue; }
                light.DrawLightVolume(spriteBatch, lightEffect, transform);
            }

            lightEffect.World = transform;

            GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.Additive);

            if (Character.Controlled != null)
            {
                DrawHalo(Character.Controlled);
            }
            else
            {
                foreach (Character character in Character.CharacterList)
                {
                    if (character.Submarine == null || character.IsDead || !character.IsHuman) { continue; }
                    DrawHalo(character);
                }
            }

            void DrawHalo(Character character)
            {
                if (character == null || character.Removed) { return; }
                Vector2 haloDrawPos = character.DrawPosition;
                haloDrawPos.Y = -haloDrawPos.Y;

                //ambient light decreases the brightness of the halo (no need for a bright halo if the ambient light is bright enough)
                float ambientBrightness = (AmbientLight.R + AmbientLight.B + AmbientLight.G) / 255.0f / 3.0f;
                Color haloColor = Color.White.Multiply(0.3f - ambientBrightness);
                if (haloColor.A > 0)
                {
                    float scale = 512.0f / LightSource.LightTexture.Width;
                    spriteBatch.Draw(
                        LightSource.LightTexture, haloDrawPos, null, haloColor, 0.0f,
                        new Vector2(LightSource.LightTexture.Width, LightSource.LightTexture.Height) / 2, scale, SpriteEffects.None, 0.0f);
                }
            }

            spriteBatch.End();

            //draw the actual light volumes, additive particles, hull ambient lights and the halo around the player
            //---------------------------------------------------------------------------------------------------

            graphics.SetRenderTarget(null);
            graphics.BlendState = BlendState.NonPremultiplied;
        }

        private readonly List<Entity> highlightedEntities = new List<Entity>();

        private bool UpdateHighlights(GraphicsDevice graphics, SpriteBatch spriteBatch, Matrix spriteBatchTransform, Camera cam)
        {
            if (GUI.DisableItemHighlights) { return false; }

            highlightedEntities.Clear();
            if (Character.Controlled != null && (!Character.Controlled.IsKeyDown(InputType.Aim) || Character.Controlled.HeldItems.Any(it => it.GetComponent<Sprayer>() == null)))
            {
                if (Character.Controlled.FocusedItem != null)
                {
                    highlightedEntities.Add(Character.Controlled.FocusedItem);
                }
                if (Character.Controlled.FocusedCharacter != null)
                {
                    highlightedEntities.Add(Character.Controlled.FocusedCharacter);
                }
                foreach (Item item in Item.ItemList)
                {
                    if ((item.IsHighlighted || item.IconStyle != null) && !highlightedEntities.Contains(item))
                    {
                        highlightedEntities.Add(item);
                    }
                }
            }
            if (highlightedEntities.Count == 0) { return false; }

            //draw characters in light blue first
            graphics.SetRenderTarget(HighlightMap);
            SolidColorEffect.CurrentTechnique = SolidColorEffect.Techniques["SolidColor"];
            SolidColorEffect.Parameters["color"].SetValue(Color.LightBlue.ToVector4());
            SolidColorEffect.CurrentTechnique.Passes[0].Apply();
            DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShaderSolidColor"];
            DeformableSprite.Effect.Parameters["solidColor"].SetValue(Color.LightBlue.ToVector4());
            DeformableSprite.Effect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, samplerState: SamplerState.LinearWrap, effect: SolidColorEffect, transformMatrix: spriteBatchTransform);
            foreach (Entity highlighted in highlightedEntities)
            {
                if (highlighted is Item item)
                {
                    if (item.IconStyle != null && (item != Character.Controlled.FocusedItem || Character.Controlled.FocusedItem == null))
                    {
                        //wait until next pass
                    }
                    else
                    {
                        item.Draw(spriteBatch, false, true);
                    }
                }
                else if (highlighted is Character character)
                {
                    character.Draw(spriteBatch, cam);
                }
            }
            spriteBatch.End();

            //draw items with iconstyles in the style's color
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, samplerState: SamplerState.LinearWrap, effect: SolidColorEffect, transformMatrix: spriteBatchTransform);
            foreach (Entity highlighted in highlightedEntities)
            {
                if (highlighted is Item item)
                {
                    if (item.IconStyle != null && (item != Character.Controlled.FocusedItem || Character.Controlled.FocusedItem == null))
                    {
                        SolidColorEffect.Parameters["color"].SetValue(item.IconStyle.Color.ToVector4());
                        SolidColorEffect.CurrentTechnique.Passes[0].Apply();
                        item.Draw(spriteBatch, false, true);
                    }
                }
            }
            spriteBatch.End();

            //draw characters in black with a bit of blur, leaving the white edges visible
            float phase = (float)(Math.Sin(Timing.TotalTime * 3.0f) + 1.0f) / 2.0f; //phase oscillates between 0 and 1
            Vector4 overlayColor = Color.Black.ToVector4() * MathHelper.Lerp(0.5f, 0.9f, phase);
            SolidColorEffect.Parameters["color"].SetValue(overlayColor);
            SolidColorEffect.CurrentTechnique = SolidColorEffect.Techniques["SolidColorBlur"];
            SolidColorEffect.CurrentTechnique.Passes[0].Apply();
            DeformableSprite.Effect.Parameters["solidColor"].SetValue(overlayColor);
            DeformableSprite.Effect.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, samplerState: SamplerState.LinearWrap, effect: SolidColorEffect, transformMatrix: spriteBatchTransform);
            foreach (Entity highlighted in highlightedEntities)
            {
                if (highlighted is Item item)
                {
                    SolidColorEffect.Parameters["blurDistance"].SetValue(0.02f);
                    item.Draw(spriteBatch, false, true);
                }
                else if (highlighted is Character character)
                {
                    SolidColorEffect.Parameters["blurDistance"].SetValue(0.05f);
                    character.Draw(spriteBatch, cam);
                }
            }
            spriteBatch.End();

            //raster pattern on top of everything
            spriteBatch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearWrap);
            spriteBatch.Draw(highlightRaster,
                new Rectangle(0, 0, HighlightMap.Width, HighlightMap.Height),
                new Rectangle(0, 0, (int)(HighlightMap.Width / currLightMapScale * 0.5f), (int)(HighlightMap.Height / currLightMapScale * 0.5f)),
                Color.White * 0.5f);
            spriteBatch.End();

            DeformableSprite.Effect.CurrentTechnique = DeformableSprite.Effect.Techniques["DeformShader"];

            return true;
        }

        private readonly Dictionary<Hull, Rectangle> visibleHulls = new Dictionary<Hull, Rectangle>();
        private Dictionary<Hull, Rectangle> GetVisibleHulls(Camera cam)
        {
            visibleHulls.Clear();
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.HiddenInGame) { continue; }
                var drawRect =
                    hull.Submarine == null ?
                    hull.Rect :
                    new Rectangle((int)(hull.Submarine.DrawPosition.X + hull.Rect.X), (int)(hull.Submarine.DrawPosition.Y + hull.Rect.Y), hull.Rect.Width, hull.Rect.Height);

                if (drawRect.Right < cam.WorldView.X || drawRect.X > cam.WorldView.Right ||
                    drawRect.Y - drawRect.Height > cam.WorldView.Y || drawRect.Y < cam.WorldView.Y - cam.WorldView.Height)
                {
                    continue;
                }
                visibleHulls.Add(hull, drawRect);
            }
            return visibleHulls;
        }

        public void UpdateObstructVision(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Vector2 lookAtPosition)
        {
            if (currLosRaycastSettings.losTexScale != GameMain.Config.LosRaycastSetting.losTexScale ||
                currLosRaycastSettings.RayStepIterations != GameMain.Config.LosRaycastSetting.RayStepIterations ||
                currLosRaycastSettings.RayCount != GameMain.Config.LosRaycastSetting.RayCount)
            {
                //los settings changed -> recreate render targets
                CreateLosRendertargets(graphics);
            }
            if ((!LosEnabled || LosMode == LosMode.None) && !ObstructVision) return;
            if (ViewTarget == null) return;

            //--------------------------------------

            if (LosEnabled && LosMode != LosMode.None && ViewTarget != null)
            {
                graphics.SetRenderTarget(LosMap);

                if (ObstructVision)
                {
                    graphics.Clear(Color.Black);
                    Vector2 diff = lookAtPosition - ViewTarget.WorldPosition;
                    diff.Y = -diff.Y;
                    if (diff.LengthSquared() > 20.0f * 20.0f) { losOffset = diff; }
                    float rotation = MathUtils.VectorToAngle(losOffset);

                    Vector2 scale = new Vector2(
                        MathHelper.Clamp(losOffset.Length() / 256.0f, 4.0f, 5.0f), 3.0f);

                    spriteBatch.Begin(SpriteSortMode.Deferred, transformMatrix: cam.Transform * Matrix.CreateScale(new Vector3(GameMain.Config.LightMapScale, GameMain.Config.LightMapScale, 1.0f)));
                    spriteBatch.Draw(visionCircle, new Vector2(ViewTarget.WorldPosition.X, -ViewTarget.WorldPosition.Y), null, Color.White, rotation,
                        new Vector2(visionCircle.Width * 0.2f, visionCircle.Height / 2), scale, SpriteEffects.None, 0.0f);
                    spriteBatch.End();
                }
                else
                {
                    graphics.Clear(Color.White);
                }

                Vector2 pos = ViewTarget.DrawPosition;

                Rectangle camView = new Rectangle(cam.WorldView.X, cam.WorldView.Y - cam.WorldView.Height, cam.WorldView.Width, cam.WorldView.Height);

                Matrix shadowTransform = cam.ShaderTransform
                    * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

                var convexHulls = ConvexHull.GetHullsInRange(ViewTarget.Position, cam.WorldView.Width * 0.75f, ViewTarget.Submarine);

                if (LosMode != LosMode.Raycast)
                {
                    if (convexHulls != null)
                    {
                        List<VertexPositionColor> shadowVerts = new List<VertexPositionColor>();
                        List<VertexPositionTexture> penumbraVerts = new List<VertexPositionTexture>();
                        foreach (ConvexHull convexHull in convexHulls)
                        {
                            if (!convexHull.Enabled || !convexHull.Intersects(camView)) continue;

                            Vector2 relativeLightPos = pos;
                            if (convexHull.ParentEntity?.Submarine != null) relativeLightPos -= convexHull.ParentEntity.Submarine.Position;

                            convexHull.CalculateLosVertices(relativeLightPos);

                            for (int i = 0; i < convexHull.ShadowVertexCount; i++)
                            {
                                shadowVerts.Add(convexHull.ShadowVertices[i]);
                            }

                            for (int i = 0; i < convexHull.PenumbraVertexCount; i++)
                            {
                                penumbraVerts.Add(convexHull.PenumbraVertices[i]);
                            }
                        }

                        if (shadowVerts.Count > 0)
                        {
                            ConvexHull.shadowEffect.World = shadowTransform;
                            ConvexHull.shadowEffect.CurrentTechnique.Passes[0].Apply();
                            graphics.DrawUserPrimitives(PrimitiveType.TriangleList, shadowVerts.ToArray(), 0, shadowVerts.Count / 3, VertexPositionColor.VertexDeclaration);

                            if (penumbraVerts.Count > 0)
                            {
                                ConvexHull.penumbraEffect.World = shadowTransform;
                                ConvexHull.penumbraEffect.CurrentTechnique.Passes[0].Apply();
                                graphics.DrawUserPrimitives(PrimitiveType.TriangleList, penumbraVerts.ToArray(), 0, penumbraVerts.Count / 3, VertexPositionTexture.VertexDeclaration);
                            }
                        }
                    }
                }
                else
                {

                    graphics.SetRenderTarget(LosOcclusionMap);

                    graphics.Clear(Color.White);

                    // Set drawing settings
                    LosRaycastSettings losRaycastSetting = GameMain.Config.LosRaycastSetting;

                    Matrix spriteBatchTransform = cam.Transform * Matrix.CreateScale(new Vector3(GameMain.Config.LosRaycastSetting.losTexScale, GameMain.Config.LosRaycastSetting.losTexScale, 1.0f));


                    SolidColorEffect.CurrentTechnique = SolidColorEffect.Techniques["solidColorThreshold"];
                    SolidColorEffect.Parameters["color"].SetValue(new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    SolidColorEffect.Parameters["threshold"].SetValue(GameMain.Config.LosRaycastSetting.OccluderAlphaThreshold);

                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, transformMatrix: spriteBatchTransform, effect: SolidColorEffect);

                    // Draw walls (done separarely to filter out broken segments)
                    Submarine.DrawDamageable(spriteBatch, null, predicate: (MapEntity e) => { return (e is Structure s) ? s.CastShadow : true; }, excludeBroken: true);

                    foreach (ConvexHull convexHull in convexHulls)
                    {
                        //Technically means that structures not drawn with DrawDamageable are not drawn.
                        //However, these should not exist as all structures that have an occluding body should be drawn.
                        if (convexHull.ParentEntity is Item parent && parent.Condition - float.Epsilon > 0.0f)
                        {
                            if (parent.GetComponent<Door>() is { } door)
                            {
                                // drawing doors directly omits drawing the decorative (background) sprite as occluder
                                door.Draw(spriteBatch, false);
                            }
                            else
                            {
                                parent.Draw(spriteBatch, false);
                            }
                        }
                    }

                    spriteBatch.End();

                    // Do the raycasting

                    graphics.SetRenderTarget(LosRaycastMap[0]);

                    graphics.Clear(Color.Black); // init texture as black (all rays start at 0)

                    // Screen UVs of view target
                    Vector2 center = new Vector2((ViewTarget.WorldPosition.X - cam.WorldView.X) /cam.WorldView.Width, (-ViewTarget.WorldPosition.Y + cam.WorldView.Y) / cam.WorldView.Height);

                    // Calculate distance to furtherst corner
                    float rayLength = 0.0f;
                    Vector2 aspect = new Vector2((float)GameMain.GraphicsWidth / GameMain.GraphicsHeight, 1.0f); // Need to compensate for aspect ratio against distortions
                    if (center.X > 0.5f)
                    {
                        if (center.Y > 0.5f)
                            rayLength = (aspect * (center - new Vector2(0f, 0f))).Length();
                        else
                            rayLength = (aspect * (center - new Vector2(0f, 1f))).Length();
                    }
                    else
                    {
                        if (center.Y > 0.5f)
                            rayLength = (aspect * (center - new Vector2(1f, 0f))).Length();
                        else
                            rayLength = (aspect * (center - new Vector2(1f, 1f))).Length();
                    }
                    rayLength *= GameMain.Config.LosRaycastSetting.RayLength;

                    LosRaycastEffect.CurrentTechnique = LosRaycastEffect.Techniques["losRaycast64"];

                    LosRaycastEffect.Parameters["occlusionMap"].SetValue(LosOcclusionMap);
                    LosRaycastEffect.Parameters["center"].SetValue(center);
                    LosRaycastEffect.Parameters["bias"].SetValue(GameMain.Config.LosRaycastSetting.OccluderAlphaThreshold);
                    LosRaycastEffect.Parameters["rayStepSize"].SetValue(rayLength / (64.0f * GameMain.Config.LosRaycastSetting.RayStepIterations));
                    LosRaycastEffect.Parameters["rayLength"].SetValue(rayLength);
                    LosRaycastEffect.Parameters["iaspect"].SetValue((float)GameMain.GraphicsHeight / GameMain.GraphicsWidth);

                    graphics.BlendState = BlendState.Opaque;
                    graphics.SamplerStates[0] = SamplerState.LinearWrap;

                    for (int i = 0; i < losRaycastSetting.RayStepIterations; i++)
                    {
                        // swap buffers
                        // swap first, then draw so that end result is always drawn to LosRaycastMap[0]
                        RenderTarget2D temp = LosRaycastMap[0];
                        LosRaycastMap[0] = LosRaycastMap[1];
                        LosRaycastMap[1] = temp;

                        graphics.SetRenderTarget(LosRaycastMap[0]);
                        LosRaycastEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[1]);
                        LosRaycastEffect.CurrentTechnique.Passes[0].Apply();
                        Quad.Render();

                    }

                    // Blur the raycast onto the second texture to smoothen out the shading on colliders

                    if (GameMain.Config.LosRaycastSetting.RayBlurSteps > 0.0f)
                    {
                        graphics.SetRenderTarget(LosRaycastMap[1]);

                        if (GameMain.Config.LosRaycastSetting.RayBlurSteps > 4.0f)
                            LosRaycastEffect.CurrentTechnique = LosRaycastEffect.Techniques["losBlurRaycast8"];
                        else
                            LosRaycastEffect.CurrentTechnique = LosRaycastEffect.Techniques["losBlurRaycast4"];

                        LosRaycastEffect.Parameters["rayBlurDistWeight"].SetValue(GameMain.Config.LosRaycastSetting.RayBlurDistWeight);

                        LosRaycastEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[0]);

                        LosRaycastEffect.CurrentTechnique.Passes[0].Apply();
                        Quad.Render();
                    }

                    // Render penumbra in polar coords

                    if (GameMain.Config.LosRaycastSetting.PenumbraSteps > 0.0f)
                    {
                        graphics.SetRenderTarget(LosShadownMap);

                        if (GameMain.Config.LosRaycastSetting.PenumbraSteps > 4.0f)
                            LosPenumbraEffect.CurrentTechnique = LosPenumbraEffect.Techniques["losPenumbra8"];
                        else
                            LosPenumbraEffect.CurrentTechnique = LosPenumbraEffect.Techniques["losPenumbra4"];

                        LosPenumbraEffect.Parameters["penumbraAngle"].SetValue(GameMain.Config.LosRaycastSetting.PenumbraAngle);
                        LosPenumbraEffect.Parameters["rayLength"].SetValue(rayLength);
                        LosPenumbraEffect.Parameters["margin"].SetValue(1f / 255f);

                        LosPenumbraEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[0]);
                        LosPenumbraEffect.Parameters["penumbraLut"].SetValue(penumbraLut);

                        LosPenumbraEffect.CurrentTechnique.Passes[0].Apply();
                        Quad.Render();

                    }

                    // Use raycasting output to cast shadow

                    graphics.SetRenderTarget(LosMap);

                    if (GameMain.Config.LosRaycastSetting.PenumbraSteps > 0.0f)
                    {
                        LosShadowEffect.CurrentTechnique = ObstructVision ? LosShadowEffect.Techniques["losShadowMappedObstruct"] : LosShadowEffect.Techniques["losShadowMapped"];
                        LosShadowEffect.Parameters["occlusionMap"].SetValue(LosOcclusionMap);
                        LosShadowEffect.Parameters["shadowMap"].SetValue(LosShadownMap);

                        if (GameMain.Config.LosRaycastSetting.RayBlurSteps > 0.0f)
                            LosShadowEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[1]);
                        else
                            LosShadowEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[0]);
                    }
                    else if (GameMain.Config.LosRaycastSetting.RayBlurSteps > 0.0f)
                    {
                        LosShadowEffect.CurrentTechnique = ObstructVision ? LosShadowEffect.Techniques["losShadowBlurredObstruct"] : LosShadowEffect.Techniques["losShadowBlurred"];
                        LosShadowEffect.Parameters["occlusionMap"].SetValue(LosOcclusionMap);
                        LosShadowEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[0]);
                        LosShadowEffect.Parameters["shadowMap"].SetValue(LosRaycastMap[1]);
                    }
                    else
                    {
                        LosShadowEffect.CurrentTechnique = ObstructVision ? LosShadowEffect.Techniques["losShadowObstruct"] : LosShadowEffect.Techniques["losShadow"];
                        LosShadowEffect.Parameters["occlusionMap"].SetValue(LosOcclusionMap);
                        LosShadowEffect.Parameters["raycastMap"].SetValue(LosRaycastMap[0]);
                    }

                    LosShadowEffect.Parameters["center"].SetValue(center);
                    LosShadowEffect.Parameters["bias"].SetValue(GameMain.Config.LosRaycastSetting.OccluderAlphaThreshold);
                    LosShadowEffect.Parameters["inDepth"].SetValue(GameMain.Config.LosRaycastSetting.InDepth*cam.Zoom);
                    LosShadowEffect.Parameters["rayLength"].SetValue(rayLength);
                    LosShadowEffect.Parameters["aspect"].SetValue((float)GameMain.GraphicsWidth / (float)GameMain.GraphicsHeight);

                    // ObstructVision

                    Vector4 visionCoords = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);

                    if (ObstructVision)
                    {
                        Vector2 diff = lookAtPosition - ViewTarget.WorldPosition;
                        diff.Y = -diff.Y;
                        if (diff.LengthSquared() > 20.0f * 20.0f) { losOffset = diff; }
                        float rotation = MathUtils.VectorToAngle(losOffset);

                        Vector2 scale = new Vector2(
                            MathHelper.Clamp(losOffset.Length() / 256.0f, 4.0f, 5.0f), 3.0f);

                        // Camera world position
                        Matrix camTransform = Matrix.CreateScale(new Vector3(cam.WorldView.Width, cam.WorldView.Height, 1.0f)) *
                            Matrix.CreateTranslation(new Vector3(cam.WorldView.X, -cam.WorldView.Y, 0.0f));

                        // Target sprite position
                        // Order Translate origin, Scale, Rotate, Translate
                        Matrix visionSpriteTransform = Matrix.CreateTranslation(new Vector3(-0.2f, -1.0f / 2.0f, 0.0f)) *
                            Matrix.CreateScale(new Vector3(scale*new Vector2(visionCircle.Width, visionCircle.Height), 1.0f)) *
                            Matrix.CreateRotationZ(rotation) *
                            Matrix.CreateTranslation(new Vector3(ViewTarget.WorldPosition.X, -ViewTarget.WorldPosition.Y, 0.0f));

                        // Matrix for converting screen UVs to sprite UVs
                        // First cam transform (UVs to world), then inverse sprite transform (world back to UV)
                        Matrix visionTransform = camTransform * Matrix.Invert(visionSpriteTransform);

                        LosShadowEffect.Parameters["visionTransform"].SetValue(visionTransform);
                        LosShadowEffect.Parameters["visionCirlce"].SetValue(visionCircle);
                    }

                    graphics.BlendState = BlendState.Opaque;
                    graphics.SamplerStates[0] = SamplerState.LinearWrap;

                    LosShadowEffect.CurrentTechnique.Passes[0].Apply();
                    Quad.Render();
                }
            }

            graphics.SetRenderTarget(null);
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
            Multiplicative = new BlendState
            {
                ColorSourceBlend = Blend.DestinationColor,
                ColorDestinationBlend = Blend.SourceColor,
                ColorBlendFunction = BlendFunction.Add
            };
        }
        public static BlendState Multiplicative { get; private set; }
    }

}
