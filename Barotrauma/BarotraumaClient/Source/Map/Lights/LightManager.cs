using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using System.Collections.Generic;
using System.Linq;
using System;
using Barotrauma.Items.Components;

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

        private float currLightMapScale;

        public Color AmbientLight;

        public RenderTarget2D LightMap
        {
            get;
            private set;
        }
        public RenderTarget2D SpecularMap
        {
            get;
            private set;
        }
        public RenderTarget2D LosTexture
        {
            get;
            private set;
        }

        private BasicEffect lightEffect;

        public Effect LosEffect
        {
            get; private set;
        }
        
        private List<LightSource> lights;

        public bool LosEnabled = true;
        public LosMode LosMode = LosMode.Transparent;

        public bool LightingEnabled = true;

        public bool ObstructVision;

        private Texture2D visionCircle;

        
        private Dictionary<Hull, Color> hullAmbientLights;
        private Dictionary<Hull, Color> smoothedHullAmbientLights;

        private float ambientLightUpdateTimer;

        public LightManager(GraphicsDevice graphics, ContentManager content)
        {
            lights = new List<LightSource>();

            AmbientLight = new Color(20, 20, 20, 255);

            visionCircle = Sprite.LoadTexture("Content/Lights/visioncircle.png");

            CreateRenderTargets(graphics);
            GameMain.Instance.OnResolutionChanged += () =>
            {
                CreateRenderTargets(graphics);
            };

#if WINDOWS
            LosEffect = content.Load<Effect>("Effects/losshader");
#else
            LosEffect = content.Load<Effect>("Effects/losshader_opengl");
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
        }

        private void CreateRenderTargets(GraphicsDevice graphics)
        {
            var pp = graphics.PresentationParameters;

            currLightMapScale = GameMain.Config.LightMapScale;

            LightMap?.Dispose();
            LightMap = new RenderTarget2D(graphics,
                       (int)(GameMain.GraphicsWidth * GameMain.Config.LightMapScale), (int)(GameMain.GraphicsHeight * GameMain.Config.LightMapScale), false,
                       pp.BackBufferFormat, pp.DepthStencilFormat, pp.MultiSampleCount,
                       RenderTargetUsage.DiscardContents);

            SpecularMap?.Dispose();
            SpecularMap = new RenderTarget2D(graphics,
                       (int)(GameMain.GraphicsWidth * GameMain.Config.LightMapScale), (int)(GameMain.GraphicsHeight * GameMain.Config.LightMapScale), false,
                       pp.BackBufferFormat, pp.DepthStencilFormat, pp.MultiSampleCount,
                       RenderTargetUsage.DiscardContents);

            LosTexture?.Dispose();
            LosTexture = new RenderTarget2D(graphics, (int)(GameMain.GraphicsWidth * GameMain.Config.LightMapScale), (int)(GameMain.GraphicsHeight * GameMain.Config.LightMapScale), false, SurfaceFormat.Color, DepthFormat.None);
        }

        public void AddLight(LightSource light)
        {
            if (!lights.Contains(light)) lights.Add(light);
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

        private List<LightSource> activeLights = new List<LightSource>(capacity: 100);

        public void UpdateLightMap(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam)
        {
            if (!LightingEnabled) return;

            if (Math.Abs(currLightMapScale - GameMain.Config.LightMapScale) > 0.01f)
            {
                //lightmap scale has changed -> recreate render targets
                CreateRenderTargets(graphics);
            }
            
            Matrix spriteBatchTransform = cam.Transform * Matrix.CreateScale(new Vector3(GameMain.Config.LightMapScale, GameMain.Config.LightMapScale, 1.0f));
            Matrix transform = cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

            if (GameMain.Config.SpecularityEnabled)
            {
                UpdateSpecularMap(graphics, spriteBatch, spriteBatchTransform, cam);
            }

            graphics.SetRenderTarget(LightMap);

            Rectangle viewRect = cam.WorldView;
            viewRect.Y -= cam.WorldView.Height;

            //check which lights need to be drawn
            activeLights.Clear();
            foreach (LightSource light in lights)
            {
                if (light.Color.A < 1 || light.Range < 1.0f || !light.Enabled) continue;
                if (!MathUtils.CircleIntersectsRectangle(light.WorldPosition, light.Range, viewRect)) continue;
                activeLights.Add(light);
            }


            //clear the lightmap
            graphics.Clear(Color.Black);
            graphics.BlendState = BlendState.Additive;


            //draw background lights
            //---------------------------------------------------------------------------------------------------
            bool backgroundSpritesDrawn = false;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
            foreach (LightSource light in activeLights)
            {
                if (!light.IsBackground) continue;
                light.DrawSprite(spriteBatch, cam);
                light.DrawLightVolume(spriteBatch, lightEffect, transform);
                backgroundSpritesDrawn = true;
            }
            GameMain.ParticleManager.Draw(spriteBatch, true, null, Particles.ParticleBlendState.Additive);
            spriteBatch.End();
            
            //draw a black rectangle on hulls to hide background lights behind subs
            //---------------------------------------------------------------------------------------------------
            Dictionary<Hull, Rectangle> visibleHulls = new Dictionary<Hull, Rectangle>();
            foreach (Hull hull in Hull.hullList)
            {
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

            if (backgroundSpritesDrawn)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, transformMatrix: spriteBatchTransform);            
                foreach (Rectangle drawRect in visibleHulls.Values)
                {
                    //TODO: draw some sort of smoothed rectangle
                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(drawRect.X, -drawRect.Y),
                        new Vector2(drawRect.Width, drawRect.Height),
                        Color.Black, true);
                }
                spriteBatch.End();
                graphics.BlendState = BlendState.Additive;
            }

            //draw the focused item and character to highlight them,
            //and light sprites (done before drawing the actual light volumes so we can make characters obstruct the highlights and sprites)
            //---------------------------------------------------------------------------------------------------
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);
            
            if (Character.Controlled != null)
            {
                if (Character.Controlled.FocusedItem != null)
                {
                    Character.Controlled.FocusedItem.IsHighlighted = true;
                }
                if (Character.Controlled.FocusedCharacter != null)
                {
                    Character.Controlled.FocusedCharacter.Draw(spriteBatch, cam);
                }

                foreach (Item item in Item.ItemList)
                {
                    if (item.IsHighlighted)
                    {
                        item.Draw(spriteBatch, false, true);
                    }
                }
            }

            foreach (LightSource light in activeLights)
            {
                if (light.IsBackground) continue;
                light.DrawSprite(spriteBatch, cam);
            }
            spriteBatch.End();

            //draw characters to obstruct the highlighted items/characters and light sprites
            //---------------------------------------------------------------------------------------------------
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, transformMatrix: spriteBatchTransform);
            foreach (Character character in Character.CharacterList)
            {
                if (character.CurrentHull == null || !character.Enabled) continue;
                if (Character.Controlled?.FocusedCharacter == character) continue;                
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    limb.Draw(spriteBatch, cam, Color.Black);
                }
            }
            spriteBatch.End();
            graphics.BlendState = BlendState.Additive;
            
            //draw the actual light volumes, additive particles, hull ambient lights and the halo around the player
            //---------------------------------------------------------------------------------------------------
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, transformMatrix: spriteBatchTransform);

            GUI.DrawRectangle(spriteBatch, new Rectangle(cam.WorldView.X, -cam.WorldView.Y, cam.WorldView.Width, cam.WorldView.Height), AmbientLight, isFilled:true);

            foreach (ElectricalDischarger discharger in ElectricalDischarger.List)
            {
                discharger.DrawElectricity(spriteBatch);
            }

            foreach (LightSource light in activeLights)
            {
                if (light.IsBackground) continue;
                light.DrawLightVolume(spriteBatch, lightEffect, transform);
            }
            Vector3 offset = Vector3.Zero;// new Vector3(Submarine.MainSub.DrawPosition.X, Submarine.MainSub.DrawPosition.Y, 0.0f);
            lightEffect.World = Matrix.CreateTranslation(Vector3.Zero) * transform;
            
            GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.Additive);

            foreach (Hull hull in smoothedHullAmbientLights.Keys)
            {
                if (smoothedHullAmbientLights[hull].A < 0.01f) continue;
                if (!visibleHulls.TryGetValue(hull, out Rectangle drawRect)) continue;

                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X, -drawRect.Y),
                    new Vector2(hull.Rect.Width, hull.Rect.Height),
                    smoothedHullAmbientLights[hull], true);
            }

            /*if (Character.Controlled != null)
            {
                Vector2 haloDrawPos = Character.Controlled.DrawPosition;
                haloDrawPos.Y = -haloDrawPos.Y;

                //ambient light decreases the brightness of the halo (no need for a bright halo if the ambient light is bright enough)
                float ambientBrightness = (AmbientLight.R + AmbientLight.B + AmbientLight.G) / 255.0f / 3.0f;
                Color haloColor = Color.White * (1.0f - ambientBrightness);                
                spriteBatch.Draw(
                    LightSource.LightTexture, haloDrawPos, null, haloColor * 0.4f, 0.0f,
                    new Vector2(LightSource.LightTexture.Width / 2, LightSource.LightTexture.Height / 2), 1.0f, SpriteEffects.None, 0.0f);
            }*/
            spriteBatch.End();

            if (GameMain.Config.SpecularityEnabled)
            {
                spriteBatch.Begin(blendState: CustomBlendStates.Multiplicative);
                spriteBatch.Draw(SpecularMap, Vector2.Zero, Color.White);
                //spriteBatch.Draw(SpecularMap, Vector2.Zero, Color.White);
                spriteBatch.End();
            }

            //draw the actual light volumes, additive particles, hull ambient lights and the halo around the player
            //---------------------------------------------------------------------------------------------------

            graphics.SetRenderTarget(null);
            graphics.BlendState = BlendState.AlphaBlend;
        }


        public void UpdateSpecularMap(GraphicsDevice graphics, SpriteBatch spriteBatch, Matrix spriteBatchTransform, Camera cam)
        {
            graphics.SetRenderTarget(SpecularMap);
            
            //clear the lightmap
            graphics.Clear(Color.Gray);
            graphics.BlendState = BlendState.AlphaBlend;

            spriteBatch.Begin(sortMode: SpriteSortMode.Deferred, blendState: BlendState.AlphaBlend, transformMatrix: spriteBatchTransform);

            if (Level.Loaded != null)
            {
                Level.Loaded.LevelObjectManager.DrawObjects(spriteBatch, cam, drawFront: false, specular: true);
            }
            
            Dictionary<Hull, Rectangle> visibleHulls = new Dictionary<Hull, Rectangle>();
            foreach (Hull hull in Hull.hullList)
            {
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
            
            foreach (Rectangle drawRect in visibleHulls.Values)
            {
                //TODO: draw some sort of smoothed rectangle
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(drawRect.X, -drawRect.Y),
                    new Vector2(drawRect.Width, drawRect.Height),
                    Color.Gray, true);
            }
            spriteBatch.End();

            //TODO: specular maps for level walls
            Level.Loaded?.Renderer?.RenderWalls(graphics, cam, specular: true);

            graphics.SetRenderTarget(null);
            graphics.BlendState = BlendState.AlphaBlend;
        }

        public void UpdateObstructVision(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, Vector2 lookAtPosition)
        {
            if ((!LosEnabled || LosMode == LosMode.None) && !ObstructVision) return;
            if (ViewTarget == null) return;

            graphics.SetRenderTarget(LosTexture);

            spriteBatch.Begin(SpriteSortMode.Deferred, transformMatrix: cam.Transform * Matrix.CreateScale(new Vector3(GameMain.Config.LightMapScale, GameMain.Config.LightMapScale, 1.0f)));
            /*if (ObstructVision)
            {
                graphics.Clear(Color.White);
                Vector2 diff = lookAtPosition - ViewTarget.WorldPosition;
                diff.Y = -diff.Y;
                float rotation = MathUtils.VectorToAngle(diff);

                Vector2 scale = new Vector2(
                    MathHelper.Clamp(diff.Length() / 256.0f, 2.0f, 5.0f), 2.0f);

                spriteBatch.Draw(visionCircle, new Vector2(ViewTarget.WorldPosition.X, -ViewTarget.WorldPosition.Y), null, Color.White, rotation,
                    new Vector2(LightSource.LightTexture.Width * 0.2f, LightSource.LightTexture.Height / 2), scale, SpriteEffects.None, 0.0f);
            }
            else
            {
                graphics.Clear(Color.White);
            }*/
            graphics.Clear(Color.White);
            spriteBatch.End();
            

            //--------------------------------------

            if (LosEnabled && LosMode != LosMode.None && ViewTarget != null)
            {
                Vector2 pos = ViewTarget.WorldPosition;

                Rectangle camView = new Rectangle(cam.WorldView.X, cam.WorldView.Y - cam.WorldView.Height, cam.WorldView.Width, cam.WorldView.Height);

                Matrix shadowTransform = cam.ShaderTransform
                    * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;

                var convexHulls = ConvexHull.GetHullsInRange(viewTarget.Position, cam.WorldView.Width*0.75f, viewTarget.Submarine);
                if (convexHulls != null)
                {
                    List<VertexPositionColor> shadowVerts = new List<VertexPositionColor>();
                    List<VertexPositionTexture> penumbraVerts = new List<VertexPositionTexture>();
                    foreach (ConvexHull convexHull in convexHulls)
                    {
                        if (!convexHull.Enabled || !convexHull.Intersects(camView)) continue;

                        Vector2 relativeLightPos = pos;
                        if (convexHull.ParentEntity?.Submarine != null) relativeLightPos -= convexHull.ParentEntity.Submarine.Position;

                        convexHull.CalculateShadowVertices(relativeLightPos, true);

                        //convert triangle strips to a triangle list
                        for (int i = 0; i < convexHull.shadowVertexCount * 2 - 2; i++)
                        {
                            if (i % 2 == 0)
                            {
                                shadowVerts.Add(convexHull.ShadowVertices[i]);
                                shadowVerts.Add(convexHull.ShadowVertices[i + 1]);
                                shadowVerts.Add(convexHull.ShadowVertices[i + 2]);
                            }
                            else
                            {
                                shadowVerts.Add(convexHull.ShadowVertices[i]);
                                shadowVerts.Add(convexHull.ShadowVertices[i + 2]);
                                shadowVerts.Add(convexHull.ShadowVertices[i + 1]);
                            }
                        }

                        penumbraVerts.AddRange(convexHull.PenumbraVertices);
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

            return AmbientLightHulls(hull, hullAmbientLight, light.Color * (light.Range / 2000.0f));
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
