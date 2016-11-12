using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Lights;
using System.Diagnostics;
using Microsoft.Xna.Framework.Content;
using System.Collections.Generic;

namespace Barotrauma
{
    class GameScreen : Screen
    {
        Camera cam;

        Color waterColor = new Color(0.75f, 0.8f, 0.9f, 1.0f);

        readonly RenderTarget2D renderTargetBackground;
        readonly RenderTarget2D renderTarget;
        readonly RenderTarget2D renderTargetWater;
        readonly RenderTarget2D renderTargetAir;

        private BlurEffect lightBlur;

        private Effect damageEffect;

        private Texture2D damageStencil;

        public BackgroundCreatureManager BackgroundCreatureManager;

        public override Camera Cam
        {
            get { return cam; }
        }

        public GameScreen(GraphicsDevice graphics, ContentManager content)
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));

            renderTargetBackground  = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTarget            = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetWater       = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetAir         = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.BackgroundCreaturePrefabs);
            if(files.Count > 0)
                BackgroundCreatureManager = new BackgroundCreatureManager(files);
            else
                BackgroundCreatureManager = new BackgroundCreatureManager("Content/BackgroundSprites/BackgroundCreaturePrefabs.xml");

#if LINUX
            var blurEffect = content.Load<Effect>("blurshader_opengl");
            damageEffect = content.Load<Effect>("damageshader_opengl");
#else
            var blurEffect = content.Load<Effect>("blurshader");
            damageEffect = content.Load<Effect>("damageshader");
#endif

            damageStencil = TextureLoader.FromFile("Content/Map/walldamage.png");
            damageEffect.Parameters["xStencil"].SetValue(damageStencil);
            damageEffect.Parameters["aMultiplier"].SetValue(50.0f);
            damageEffect.Parameters["cMultiplier"].SetValue(200.0f);

            lightBlur = new BlurEffect(blurEffect, 0.001f, 0.001f);
        }

        public override void Select()
        {
            base.Select();

            if (Character.Controlled!=null)
            {
                cam.Position = Character.Controlled.WorldPosition;
            }
            else if (Submarine.MainSub != null)
            {
                cam.Position = Submarine.MainSub.WorldPosition;
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

#if DEBUG
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null && GameMain.GameSession.Submarine != null &&
                !DebugConsole.IsOpen)
            {
                var closestSub = Submarine.GetClosest(cam.WorldViewCenter);
                if (closestSub == null) closestSub = GameMain.GameSession.Submarine;

                Vector2 targetMovement = Vector2.Zero;
                if (PlayerInput.KeyDown(Keys.I)) targetMovement.Y += 1.0f;
                if (PlayerInput.KeyDown(Keys.K)) targetMovement.Y -= 1.0f;
                if (PlayerInput.KeyDown(Keys.J)) targetMovement.X -= 1.0f;
                if (PlayerInput.KeyDown(Keys.L)) targetMovement.X += 1.0f;

                if (targetMovement != Vector2.Zero)
                    closestSub.ApplyForce(targetMovement * closestSub.SubBody.Body.Mass * 100.0f);
            }
#endif

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.IsHighlighted = false;
            }

            if (GameMain.GameSession != null) GameMain.GameSession.Update((float)deltaTime);

            if (Level.Loaded != null) Level.Loaded.Update((float)deltaTime);

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null)
            {
                if (Character.Controlled.SelectedConstruction == Character.Controlled.ClosestItem)
                {
                    Character.Controlled.SelectedConstruction.UpdateHUD(cam, Character.Controlled);
                }
            }
            Character.UpdateAll(cam, (float)deltaTime);
            
            BackgroundCreatureManager.Update(cam, (float)deltaTime);

            GameMain.ParticleManager.Update((float)deltaTime);

            StatusEffect.UpdateAll((float)deltaTime);

            if (Character.Controlled != null && Lights.LightManager.ViewTarget != null)
            {
                cam.TargetPos = Lights.LightManager.ViewTarget.WorldPosition;
            }

            GameMain.LightManager.Update((float)deltaTime);
            cam.MoveCamera((float)deltaTime);
                
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.SetPrevTransform(sub.Position);
            }

            foreach (PhysicsBody pb in PhysicsBody.list)
            {
                pb.SetPrevTransform(pb.SimPosition, pb.Rotation);
            }

            MapEntity.UpdateAll(cam, (float)deltaTime);

            Character.UpdateAnimAll((float)deltaTime);

            Ragdoll.UpdateAll(cam, (float)deltaTime);

            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.Update((float)deltaTime);
            }
                
            GameMain.World.Step((float)deltaTime);

            if (!PlayerInput.LeftButtonHeld())
            {
                Inventory.draggingSlot = null;
                Inventory.draggingItem = null;
            }
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform(true);
            Submarine.CullEntities(cam);

            DrawMap(graphics, spriteBatch);

            spriteBatch.Begin();

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null)
            {
                if (Character.Controlled.SelectedConstruction == Character.Controlled.ClosestItem)
                {
                    Character.Controlled.SelectedConstruction.DrawHUD(spriteBatch, cam, Character.Controlled);
                }
                else if (!Character.Controlled.SelectedConstruction.IsInPickRange(Character.Controlled.WorldPosition))
                {
                    Character.Controlled.SelectedConstruction = null;
                }
            }

            if (Character.Controlled != null && cam != null) Character.Controlled.DrawHUD(spriteBatch, cam);

            if (GameMain.GameSession != null) GameMain.GameSession.Draw(spriteBatch);

            if (Character.Controlled == null && Submarine.MainSub != null) DrawSubmarineIndicator(spriteBatch, Submarine.MainSub);
            
            GUI.Draw((float)deltaTime, spriteBatch, cam);
                        
            spriteBatch.End();
        }

        public void DrawMap(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {

            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.UpdateTransform();
            }

            GameMain.ParticleManager.UpdateTransforms();

            GameMain.LightManager.ObstructVision = Character.Controlled != null && Character.Controlled.ObstructVision;

            GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam, lightBlur.Effect);
            if (Character.Controlled != null)
            {
                GameMain.LightManager.UpdateObstructVision(graphics, spriteBatch, cam, Character.Controlled.CursorWorldPosition);
            }

            List<Submarine> visibleSubs = new List<Submarine>();
            foreach (Submarine sub in Submarine.Loaded)
            {
                Rectangle worldBorders = new Rectangle(
                    sub.Borders.X + (int)sub.WorldPosition.X - 500,
                    sub.Borders.Y + (int)sub.WorldPosition.Y + 500,
                    sub.Borders.Width + 1000,
                    sub.Borders.Height + 1000);
                    

                if (Submarine.RectsOverlap(worldBorders, cam.WorldView))
                {
                    visibleSubs.Add(sub);
                }
            }          

            //----------------------------------------------------------------------------------------
            //1. draw the background, characters and the parts of the submarine that are behind them
            //----------------------------------------------------------------------------------------

            graphics.SetRenderTarget(renderTargetBackground);
            
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

            Submarine.DrawBack(spriteBatch, false, s => s is Structure);

            spriteBatch.End();

            graphics.SetRenderTarget(renderTarget);

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Opaque);
            spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);

            Submarine.DrawBack(spriteBatch, false, s => !(s is Structure));

            foreach (Character c in Character.CharacterList) c.Draw(spriteBatch);

            spriteBatch.End();

            //----------------------------------------------------------------------------------------
            //draw the rendertarget and particles that are only supposed to be drawn in water into renderTargetWater
            graphics.SetRenderTarget(renderTargetWater);

            spriteBatch.Begin(SpriteSortMode.Deferred,
                BlendState.Opaque);
            spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), waterColor);
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

            if (Character.Controlled != null && GameMain.LightManager.LosEnabled)
            {
                graphics.SetRenderTarget(renderTarget);
                spriteBatch.Begin(SpriteSortMode.Deferred,
                    BlendState.Opaque, null, null, null, lightBlur.Effect);

                spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);

                spriteBatch.End();

                spriteBatch.Begin(SpriteSortMode.BackToFront,
                    BlendState.AlphaBlend, SamplerState.LinearWrap,
                    null, null, null,
                    cam.Transform);

                Submarine.DrawDamageable(spriteBatch, null, false);
                Submarine.DrawFront(spriteBatch, false, s => s is Structure);

                spriteBatch.End();
                
                GameMain.LightManager.DrawLOS(spriteBatch, lightBlur.Effect, true);
            }

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

            Submarine.DrawFront(spriteBatch, false, null);
                        
            spriteBatch.End();
            
            spriteBatch.Begin(SpriteSortMode.Immediate,
                BlendState.NonPremultiplied, SamplerState.LinearWrap,
                null, null, 
                damageEffect,
                cam.Transform);

            Submarine.DrawDamageable(spriteBatch, damageEffect, false);
                        
            spriteBatch.End();

            GameMain.LightManager.DrawLightMap(spriteBatch, lightBlur.Effect);

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend, SamplerState.LinearWrap,
                null, null, null,
                cam.Transform);

            if (Level.Loaded != null) Level.Loaded.DrawFront(spriteBatch);            

            foreach (Character c in Character.CharacterList) c.DrawFront(spriteBatch,cam);

            spriteBatch.End();

            if (Character.Controlled != null && GameMain.LightManager.LosEnabled)
            {
                GameMain.LightManager.DrawLOS(spriteBatch, lightBlur.Effect,false);

                spriteBatch.Begin(SpriteSortMode.Immediate,
                    BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null);

                float r = Math.Min(CharacterHUD.damageOverlayTimer * 0.5f, 0.5f);
                spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                    Color.Lerp(GameMain.LightManager.AmbientLight*0.5f, Color.Red, r));

                spriteBatch.End();
            }

        }

        private void DrawSubmarineIndicator(SpriteBatch spriteBatch, Submarine submarine)
        {
            Vector2 subDiff = submarine.WorldPosition - cam.WorldViewCenter;

            if (Math.Abs(subDiff.X) > cam.WorldView.Width || Math.Abs(subDiff.Y) > cam.WorldView.Height)
            {
                Vector2 normalizedSubDiff = Vector2.Normalize(subDiff);

                Vector2 iconPos =
                    cam.WorldToScreen(cam.WorldViewCenter) +
                    new Vector2(normalizedSubDiff.X * GameMain.GraphicsWidth * 0.4f, -normalizedSubDiff.Y * GameMain.GraphicsHeight * 0.4f);

                GUI.SubmarineIcon.Draw(spriteBatch, iconPos, Color.LightBlue * 0.5f);

                Vector2 arrowOffset = normalizedSubDiff * GUI.SubmarineIcon.size.X * 0.7f;
                arrowOffset.Y = -arrowOffset.Y;
                GUI.Arrow.Draw(spriteBatch, iconPos + arrowOffset, Color.LightBlue * 0.5f, MathUtils.VectorToAngle(arrowOffset) + MathHelper.PiOver2);
            }
        }

    }
}
