using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Subsurface.Lights;
using FarseerPhysics;

namespace Subsurface
{
    class GameScreen : Screen
    {
        Camera cam;

        readonly RenderTarget2D renderTarget;
        readonly RenderTarget2D renderTargetWater;
        readonly RenderTarget2D renderTargetAir;

        readonly Sprite background, backgroundTop;

        public Camera Cam
        {
            get { return cam; }
        }

        public GameScreen(GraphicsDevice graphics)
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));
            
            renderTarget = new RenderTarget2D(graphics, Game1.GraphicsWidth, Game1.GraphicsHeight);
            renderTargetWater = new RenderTarget2D(graphics, Game1.GraphicsWidth, Game1.GraphicsHeight);
            renderTargetAir = new RenderTarget2D(graphics, Game1.GraphicsWidth, Game1.GraphicsHeight);

            background = new Sprite("Content/Map/background.png", Vector2.Zero);
            backgroundTop = new Sprite("Content/Map/background2.png", Vector2.Zero);
        }

        public override void Select()
        {
            base.Select();

            if (Game1.gameSession == null) Game1.gameSession = new GameSession("",false, TimeSpan.Zero);

            foreach (MapEntity entity in MapEntity.mapEntityList)
                entity.IsHighlighted = false;
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

            AmbientSoundManager.Update();

            Game1.gameSession.Update((float)deltaTime);
            //EventManager.Update(gameTime);

            Character.UpdateAll(cam, (float)deltaTime);

            Game1.particleManager.Update((float)deltaTime);

            StatusEffect.UpdateAll((float)deltaTime);
            //Physics.updated = false;

            cam.MoveCamera((float)deltaTime);

            Physics.accumulator = Math.Min(Physics.accumulator, Physics.step * 4);
            while (Physics.accumulator >= Physics.step)
            {
                foreach (PhysicsBody pb in PhysicsBody.list)
                {
                    pb.SetPrevTransform(pb.Position, pb.Rotation);
                }
                    
                MapEntity.UpdateAll(cam, (float)Physics.step);

                Character.UpdateAnimAll((float)Physics.step);

                Ragdoll.UpdateAll((float)Physics.step);

                Game1.world.Step((float)Physics.step);

                Physics.accumulator -= Physics.step;
            }


            Physics.Alpha = Physics.accumulator / Physics.step;

        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            //if (!Physics.updated) return;

            DrawMap(graphics, spriteBatch);

            //----------------------------------------------------------------------------------------
            //2. draw the HUD on top of everything
            //----------------------------------------------------------------------------------------
            
            spriteBatch.Begin();
            if (Game1.gameSession != null) Game1.gameSession.Draw(spriteBatch);

            //EventManager.DrawInfo(spriteBatch);

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null)
            {
                if (Character.Controlled.SelectedConstruction == Character.Controlled.ClosestItem)
                {
                    Character.Controlled.SelectedConstruction.DrawHUD(spriteBatch, Character.Controlled);
                }
                else
                {
                    Character.Controlled.SelectedConstruction = null;
                }
            }

            GUI.Draw((float)deltaTime, spriteBatch, cam);
                        
            if (PlayerInput.GetMouseState.LeftButton != ButtonState.Pressed) Inventory.draggingItem = null;

            spriteBatch.End();
        }

        public void DrawMap(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            //----------------------------------------------------------------------------------------
            //1. draw the characters and the parts of the map that are behind them
            //----------------------------------------------------------------------------------------

            //cam.UpdateTransform();

            //----------------------------------------------------------------------------------------
            //draw the map and characters to a rendertarget
            graphics.SetRenderTarget(renderTarget);
            graphics.Clear(new Color(11, 18, 26, 255));


            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearWrap);

            int x = (int)(cam.Position.X / 20.0f);
            int y = (int)((-cam.Position.Y+5000) / 20.0f);
            if (y<1024)
            {
                if (y>-1024)
                {
                    background.SourceRect = new Rectangle(x, Math.Max(y,0), 1024, 1024);
                    background.DrawTiled(spriteBatch, (y<0) ? new Vector2(0.0f, -y) : Vector2.Zero, new Vector2(Game1.GraphicsWidth, 1024 - y),
                        Vector2.Zero, Color.White);
                }

                if (y<0)
                {
                    backgroundTop.SourceRect = new Rectangle(x, y, 1024, 1024);
                    backgroundTop.DrawTiled(spriteBatch, Vector2.Zero, new Vector2(Game1.GraphicsWidth, Math.Min(1024 - y, Game1.GraphicsHeight)),
                        Vector2.Zero, Color.White);
                }
            }

            spriteBatch.End();


            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);
                        
            Map.DrawBack(spriteBatch);

            foreach (Character c in Character.characterList) c.Draw(spriteBatch);

            spriteBatch.End();

            //----------------------------------------------------------------------------------------
            //draw the rendertarget and particles that are only supposed to be drawn in water into renderTargetWater

            graphics.SetRenderTarget(renderTargetWater);

            spriteBatch.Begin(SpriteSortMode.Immediate,
                BlendState.AlphaBlend);
            spriteBatch.Draw(renderTarget, new Rectangle(0, 0, Game1.GraphicsWidth, Game1.GraphicsHeight), Color.White);
            spriteBatch.End();

            BlendState blend = new BlendState();
            blend.AlphaSourceBlend = Blend.One;
            blend.AlphaDestinationBlend = Blend.InverseSourceAlpha;

            spriteBatch.Begin(SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                null, DepthStencilState.DepthRead, null, null,
                cam.Transform);
            Game1.particleManager.Draw(spriteBatch, true);

            spriteBatch.End();

            //----------------------------------------------------------------------------------------
            //draw the rendertarget and particles that are only supposed to be drawn in air into renderTargetAir

            graphics.SetRenderTarget(renderTargetAir);
            spriteBatch.Begin(SpriteSortMode.Immediate,
                BlendState.AlphaBlend);
            spriteBatch.Draw(renderTarget, new Rectangle(0, 0, Game1.GraphicsWidth, Game1.GraphicsHeight), Color.White);
            spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                null, DepthStencilState.DepthRead, null, null,
                cam.Transform);

            Game1.particleManager.Draw(spriteBatch, false);
            spriteBatch.End();

            graphics.SetRenderTarget(null);

            //----------------------------------------------------------------------------------------
            //2. pass the renderTarget to the water shader to do the water effect
            //----------------------------------------------------------------------------------------

            Hull.renderer.RenderBack(graphics, renderTargetWater, Cam.ShaderTransform);

            Array.Clear(Hull.renderer.vertices, 0, Hull.renderer.vertices.Length);
            Hull.renderer.positionInBuffer = 0;
            foreach (Hull hull in Hull.hullList)
            {
                hull.Render(graphics, cam);
            }

            Hull.renderer.Render(graphics, cam, renderTargetAir, Cam.ShaderTransform);

            //----------------------------------------------------------------------------------------
            //3. draw the sections of the map that are on top of the water
            //----------------------------------------------------------------------------------------

            spriteBatch.Begin(SpriteSortMode.BackToFront,
                BlendState.AlphaBlend,
                null, null, null, null,
                cam.Transform);

            Map.DrawFront(spriteBatch);

            spriteBatch.End();

            LightManager.DrawFow(graphics,cam); 
        }
    }
}
