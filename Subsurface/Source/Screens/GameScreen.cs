using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Lights;
using System.Diagnostics;
using Microsoft.Xna.Framework.Content;

namespace Barotrauma
{
    class GameScreen : Screen
    {
        Camera cam;

        readonly RenderTarget2D renderTarget;
        readonly RenderTarget2D renderTargetWater;
        readonly RenderTarget2D renderTargetAir;

        Effect blurEffect;

        public BackgroundCreatureManager BackgroundCreatureManager;

        public Camera Cam
        {
            get { return cam; }
        }

        public GameScreen(GraphicsDevice graphics, ContentManager content)
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));
            
            renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetWater = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetAir = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                        
            BackgroundCreatureManager = new BackgroundCreatureManager("Content/BackgroundSprites/BackgroundCreaturePrefabs.xml");

            blurEffect = content.Load<Effect>("blurshader");
            SetBlurEffectParameters(0.001f, 0.001f);
        }

        public override void Select()
        {
            base.Select();

            if (Character.Controlled!=null)
            {
                cam.Position = Character.Controlled.WorldPosition;
            }
            else if (Submarine.Loaded != null)
            {
                cam.Position = Submarine.Loaded.WorldPosition;
            }

            foreach (MapEntity entity in MapEntity.mapEntityList)
                entity.IsHighlighted = false;
        }

        public override void Deselect()
        {
            base.Deselect();

            Sounds.SoundManager.LowPassHFGain = 1.0f;
        }
        
        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        public override void Update(double deltaTime)
        {
            //the accumulator code is based on this article:
            //http://gafferongames.com/game-physics/fix-your-timestep/
            Physics.accumulator += deltaTime;

#if DEBUG
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null && GameMain.GameSession.Submarine != null)
            {
                Vector2 targetMovement = Vector2.Zero;
                if (PlayerInput.KeyDown(Keys.I)) targetMovement.Y += 1.0f;
                if (PlayerInput.KeyDown(Keys.K)) targetMovement.Y -= 1.0f;
                if (PlayerInput.KeyDown(Keys.J)) targetMovement.X -= 1.0f;
                if (PlayerInput.KeyDown(Keys.L)) targetMovement.X += 1.0f;

                GameMain.GameSession.Submarine.ApplyForce(targetMovement * 100000.0f);
            }
#endif

            if (GameMain.GameSession!=null) GameMain.GameSession.Update((float)deltaTime);
            //EventManager.Update(gameTime);

            if (Level.Loaded != null) Level.Loaded.Update((float)deltaTime);

            Character.UpdateAll(cam, (float)deltaTime);

            BackgroundCreatureManager.Update(cam, (float)deltaTime);

            GameMain.ParticleManager.Update((float)deltaTime);

            StatusEffect.UpdateAll((float)deltaTime);

            Physics.accumulator = Math.Min(Physics.accumulator, Physics.step * 6);
            //Physics.accumulator = Physics.step;
            while (Physics.accumulator >= Physics.step)
            {
                if (Character.Controlled != null && Lights.LightManager.ViewTarget != null)
                {
                    cam.TargetPos = Lights.LightManager.ViewTarget.WorldPosition;
                    //Lights.LightManager.ViewPos = Character.Controlled.WorldPosition; 
                }
                cam.MoveCamera((float)Physics.step);


                if (Submarine.Loaded != null) Submarine.Loaded.SetPrevTransform(Submarine.Loaded.Position);

                foreach (PhysicsBody pb in PhysicsBody.list)
                {
                    pb.SetPrevTransform(pb.SimPosition, pb.Rotation);
                }
                
                MapEntity.UpdateAll(cam, (float)Physics.step);

                Character.UpdateAnimAll((float)Physics.step);

                Ragdoll.UpdateAll(cam, (float)Physics.step);

                if (GameMain.GameSession != null && GameMain.GameSession.Level != null && GameMain.GameSession.Submarine!=null)
                {
                    GameMain.GameSession.Submarine.Update((float)Physics.step);
                }

                GameMain.World.Step((float)Physics.step);

                //Level.AfterWorldStep();

                Physics.accumulator -= Physics.step;
            }


            Physics.Alpha = Physics.accumulator / Physics.step;

        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform();

            DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null)
            {
                if (Character.Controlled.SelectedConstruction == Character.Controlled.ClosestItem)
                {
                    Character.Controlled.SelectedConstruction.DrawHUD(spriteBatch, Character.Controlled);
                }
                else if (!Character.Controlled.SelectedConstruction.IsInPickRange(Character.Controlled.WorldPosition))
                {
                    Character.Controlled.SelectedConstruction = null;
                }
            }

            if (Character.Controlled != null && cam != null) Character.Controlled.DrawHUD(spriteBatch, cam);

            if (GameMain.GameSession != null) GameMain.GameSession.Draw(spriteBatch);

            if (Character.Controlled == null && Submarine.Loaded != null) DrawSubmarineIndicator(spriteBatch, Submarine.Loaded);
            
            GUI.Draw((float)deltaTime, spriteBatch, cam);
                        
            if (!PlayerInput.LeftButtonHeld()) Inventory.draggingItem = null;

            spriteBatch.End();
        }

        public void DrawMap(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {

            if (Submarine.Loaded != null) Submarine.Loaded.UpdateTransform();

            GameMain.LightManager.ObstructVision = Character.Controlled != null && Character.Controlled.ObstructVision;

            GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam);
            if (Character.Controlled != null)
            {
                GameMain.LightManager.UpdateObstructVision(graphics, spriteBatch, cam, Character.Controlled.CursorWorldPosition);
            }
            

            //----------------------------------------------------------------------------------------
            //1. draw the background, characters and the parts of the submarine that are behind them
            //----------------------------------------------------------------------------------------

            graphics.SetRenderTarget(renderTarget);
            
            if (Level.Loaded == null)
            {
                graphics.Clear(new Color(11, 18, 26, 255));
            }
            else
            {
                Level.Loaded.DrawBack(graphics, spriteBatch, cam, BackgroundCreatureManager);
            }

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);

            Submarine.DrawBack(spriteBatch);

            foreach (Character c in Character.CharacterList) c.Draw(spriteBatch);

            spriteBatch.End();

            //----------------------------------------------------------------------------------------
            //draw the rendertarget and particles that are only supposed to be drawn in water into renderTargetWater
            graphics.SetRenderTarget(renderTargetWater);

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Opaque);
            spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), new Color(0.75f, 0.8f, 0.9f, 1.0f));
            spriteBatch.End();

#if LINUX
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                null, DepthStencilState.DepthRead, null, null,
                cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            null, DepthStencilState.DepthRead, null, null,
            cam.Transform);
#endif
            GameMain.ParticleManager.Draw(spriteBatch, true, Particles.ParticleBlendState.AlphaBlend);
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Additive,
                null, DepthStencilState.Default, null, null,
                cam.Transform);
            GameMain.ParticleManager.Draw(spriteBatch, true, Particles.ParticleBlendState.Additive);
            spriteBatch.End();

            //----------------------------------------------------------------------------------------
            //draw the rendertarget and particles that are only supposed to be drawn in air into renderTargetAir

            graphics.SetRenderTarget(renderTargetAir);
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Opaque);
            spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
            spriteBatch.End();
#if LINUX
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                null, DepthStencilState.DepthRead, null, null,
                cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                null, DepthStencilState.DepthRead, null, null,
                cam.Transform);
#endif

            GameMain.ParticleManager.Draw(spriteBatch, false, Particles.ParticleBlendState.AlphaBlend);
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Additive,
                null, DepthStencilState.DepthRead, null, null,
                cam.Transform);
            GameMain.ParticleManager.Draw(spriteBatch, false, Particles.ParticleBlendState.Additive);
            spriteBatch.End();


            graphics.SetRenderTarget(null);

            //----------------------------------------------------------------------------------------
            //2. pass the renderTarget to the water shader to do the water effect
            //----------------------------------------------------------------------------------------

            Hull.renderer.RenderBack(spriteBatch, renderTargetWater);

            Array.Clear(Hull.renderer.vertices, 0, Hull.renderer.vertices.Length);
            Hull.renderer.PositionInBuffer = 0;
            foreach (Hull hull in Hull.hullList)
            {
                hull.Render(graphics, cam);
            }

            Hull.renderer.Render(graphics, cam, renderTargetAir, Cam.ShaderTransform);
            
            //----------------------------------------------------------------------------------------
            //3. draw the sections of the map that are on top of the water
            //----------------------------------------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend, SamplerState.LinearWrap,
                null, null, null,
                cam.Transform);

            foreach (Character c in Character.CharacterList) c.DrawFront(spriteBatch);

            Submarine.DrawFront(spriteBatch);


            
            if (Level.Loaded!=null) Level.Loaded.DrawFront(spriteBatch);            

            spriteBatch.End();


            GameMain.LightManager.DrawLightMap(spriteBatch, cam, blurEffect);

            if (Character.Controlled != null)
            {
                GameMain.LightManager.DrawLOS(graphics, spriteBatch, cam, blurEffect);
            }

        }

        private void DrawSubmarineIndicator(SpriteBatch spriteBatch, Submarine submarine)
        {
            Vector2 subDiff = submarine.WorldPosition - cam.Position;

            if (Math.Abs(subDiff.X) > cam.WorldView.Width || Math.Abs(subDiff.Y) > cam.WorldView.Height)
            {
                Vector2 normalizedSubDiff = Vector2.Normalize(subDiff);

                Vector2 iconPos =
                    cam.WorldToScreen(cam.Position) +
                    new Vector2(normalizedSubDiff.X * GameMain.GraphicsWidth * 0.4f, -normalizedSubDiff.Y * GameMain.GraphicsHeight * 0.4f);

                GUI.SubmarineIcon.Draw(spriteBatch, iconPos, Color.LightBlue * 0.5f);

                Vector2 arrowOffset = normalizedSubDiff * GUI.SubmarineIcon.size.X * 0.7f;
                arrowOffset.Y = -arrowOffset.Y;
                GUI.Arrow.Draw(spriteBatch, iconPos + arrowOffset, Color.LightBlue * 0.5f, MathUtils.VectorToAngle(arrowOffset) + MathHelper.PiOver2);
            }
        }

        /// <summary>
        /// Computes sample weightings and texture coordinate offsets
        /// for one pass of a separable gaussian blur filter.
        /// </summary>
        void SetBlurEffectParameters(float dx, float dy)
        {
            EffectParameter weightsParameter = blurEffect.Parameters["SampleWeights"];
            EffectParameter offsetsParameter = blurEffect.Parameters["SampleOffsets"];

            // Look up how many samples our gaussian blur effect supports.
            int sampleCount = weightsParameter.Elements.Count;

            // Create temporary arrays for computing our filter settings.
            float[] sampleWeights = new float[sampleCount];
            Vector2[] sampleOffsets = new Vector2[sampleCount];

            sampleWeights[0] = ComputeGaussian(0);
            sampleOffsets[0] = new Vector2(0);

            float totalWeights = sampleWeights[0];

            // Add pairs of additional sample taps, positioned
            // along a line in both directions from the center.
            for (int i = 0; i < sampleCount / 2; i++)
            {
                // Store weights for the positive and negative taps.
                float weight = ComputeGaussian(i + 1);

                sampleWeights[i * 2 + 1] = weight;
                sampleWeights[i * 2 + 2] = weight;

                totalWeights += weight * 2;

                // To get the maximum amount of blurring from a limited number of
                // pixel shader samples, we take advantage of the bilinear filtering
                // hardware inside the texture fetch unit. If we position our texture
                // coordinates exactly halfway between two texels, the filtering unit
                // will average them for us, giving two samples for the price of one.
                // This allows us to step in units of two texels per sample, rather
                // than just one at a time. The 1.5 offset kicks things off by
                // positioning us nicely in between two texels.
                float sampleOffset = i * 2 + 1.5f;

                Vector2 delta = new Vector2(dx, dy) * sampleOffset;

                // Store texture coordinate offsets for the positive and negative taps.
                sampleOffsets[i * 2 + 1] = delta;
                sampleOffsets[i * 2 + 2] = -delta;
            }

            // Normalize the list of sample weightings, so they will always sum to one.
            for (int i = 0; i < sampleWeights.Length; i++)
            {
                sampleWeights[i] /= totalWeights;
            }

            weightsParameter.SetValue(sampleWeights);
            offsetsParameter.SetValue(sampleOffsets);
        }


        /// <summary>
        /// Evaluates a single point on the gaussian falloff curve.
        /// Used for setting up the blur filter weightings.
        /// </summary>
        float ComputeGaussian(float n)
        {
            float theta = 4.0f;

            return (float)((1.0 / Math.Sqrt(2 * Math.PI * theta)) *
                           Math.Exp(-(n * n) / (2 * theta * theta)));
        }
    }
}
