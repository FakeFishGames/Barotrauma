using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using FarseerPhysics;

namespace Barotrauma
{
    partial class GameScreen : Screen
    {
        private Color waterColor = new Color(0.75f, 0.8f, 0.9f, 1.0f);

        private BlurEffect lightBlur;
        
        readonly RenderTarget2D renderTargetBackground;
        readonly RenderTarget2D renderTarget;
        readonly RenderTarget2D renderTargetWater;
        readonly RenderTarget2D renderTargetFinal;

        private Effect damageEffect;

        private Texture2D damageStencil;

        
        public GameScreen(GraphicsDevice graphics, ContentManager content)
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));

            renderTargetBackground = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTarget = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            renderTargetWater = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            renderTargetFinal = new RenderTarget2D(graphics, GameMain.GraphicsWidth, GameMain.GraphicsHeight, false, SurfaceFormat.Color, DepthFormat.None);


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
        
        public override void AddToGUIUpdateList()
        {
            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.AddToGUIUpdateList();
            }

            if (GameMain.GameSession != null) GameMain.GameSession.AddToGUIUpdateList();

            Character.AddAllToGUIUpdateList();
        }
        
        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            cam.UpdateTransform(true);
            Submarine.CullEntities(cam);

            DrawMap(graphics, spriteBatch, deltaTime);

            spriteBatch.Begin(SpriteSortMode.Immediate, null, null, null, GameMain.ScissorTestEnable);

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.DrawHUD(spriteBatch, cam, Character.Controlled);
            }

            if (Character.Controlled != null && cam != null) Character.Controlled.DrawHUD(spriteBatch, cam);

            if (GameMain.GameSession != null) GameMain.GameSession.Draw(spriteBatch);

            if (Character.Controlled == null && !GUI.DisableHUD)
            {
                for (int i = 0; i < Submarine.MainSubs.Length; i++)
                {
                    if (Submarine.MainSubs[i] == null) continue;
                    if (Level.Loaded != null && Submarine.MainSubs[i].WorldPosition.Y < Level.MaxEntityDepth) continue;
                    
                    Color indicatorColor = i == 0 ? Color.LightBlue * 0.5f : Color.Red * 0.5f;
                    DrawSubmarineIndicator(spriteBatch, Submarine.MainSubs[i], indicatorColor);                    
                }
            }

            GUI.Draw((float)deltaTime, spriteBatch, cam);

            spriteBatch.End();
        }

        public void DrawMap(GraphicsDevice graphics, SpriteBatch spriteBatch, double deltaTime)
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

            graphics.SetRenderTarget(renderTargetBackground);
            if (Level.Loaded == null)
            {
                graphics.Clear(new Color(11, 18, 26, 255));
            }
            else
            {
                //graphics.Clear(new Color(255, 255, 255, 255));
                Level.Loaded.DrawBack(graphics, spriteBatch, cam);
            }

			//draw alpha blended particles that are in water and behind subs
#if LINUX
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.None, null, null, cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
#endif
			GameMain.ParticleManager.Draw(spriteBatch, true, false, Particles.ParticleBlendState.AlphaBlend);
			spriteBatch.End();

			//draw additive particles that are in water and behind subs
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.None, null, null, cam.Transform);
			GameMain.ParticleManager.Draw(spriteBatch, true, false, Particles.ParticleBlendState.Additive);
			spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
			Submarine.DrawBack(spriteBatch, false, s => s is Structure && ((Structure)s).ResizeVertical && ((Structure)s).ResizeHorizontal);
			foreach (Structure s in Structure.WallList)
			{
				if ((s.ResizeVertical != s.ResizeHorizontal) && s.CastShadow)
				{
					GUI.DrawRectangle(spriteBatch, new Vector2(s.DrawPosition.X-s.WorldRect.Width/2,-s.DrawPosition.Y-s.WorldRect.Height/2), new Vector2(s.WorldRect.Width, s.WorldRect.Height), Color.Black, true);
				}
			}
			spriteBatch.End();
            graphics.SetRenderTarget(renderTarget);
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, null);
			spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
			spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
			Submarine.DrawBack(spriteBatch, false, s => !(s is Structure));
			Submarine.DrawBack(spriteBatch, false, s => s is Structure && !(((Structure)s).ResizeVertical && ((Structure)s).ResizeHorizontal));
            foreach (Character c in Character.CharacterList) c.Draw(spriteBatch);
            spriteBatch.End();

            //--

            GameMain.spineEffect.Parameters["World"].SetValue(Matrix.Identity);
            GameMain.spineEffect.Parameters["View"].SetValue(Matrix.Identity);
            GameMain.spineEffect.Parameters["Projection"].SetValue(cam.Transform *
            Matrix.CreateOrthographicOffCenter(0, spriteBatch.GraphicsDevice.Viewport.Width, spriteBatch.GraphicsDevice.Viewport.Height, 0, 1, 0));
            GameMain.skeletonRenderer.Begin();
            spriteBatch.Begin(SpriteSortMode.BackToFront);

            foreach (Character c in Character.CharacterList)
            {
                if (c.AnimController.skeleton == null) continue;
                c.AnimController.skeleton.X = c.DrawPosition.X;
                c.AnimController.skeleton.Y = -c.DrawPosition.Y + 120.0f;
                c.AnimController.skeleton.SetToSetupPose();
                c.AnimController.skeleton.UpdateWorldTransform();
                foreach (var bone in c.AnimController.skeleton.Bones)
                {
                    PhysicsBody target = null;
                    //bone.SetToSetupPose();
                    switch (bone.Data.Name)
                    {
                        case "spine1":
                            target = c.AnimController.GetLimb(LimbType.Waist).body;
                            break;
                        case "spine2":
                            target = c.AnimController.GetLimb(LimbType.Torso).body;
                            break;
                        case "head":
                            //target = c.AnimController.GetLimb(LimbType.Head).body;
                            break;
                        case "back-leg-ik-target":
                            target = c.AnimController.GetLimb(LimbType.LeftFoot).body;
                            break;
                        case "front-leg-ik-target":
                            target = c.AnimController.GetLimb(LimbType.RightFoot).body;
                            break;
                        case "back-arm4":
                            target = c.AnimController.GetLimb(LimbType.LeftHand).body;
                            break;
                        case "front-arm4":
                            target = c.AnimController.GetLimb(LimbType.RightHand).body;
                            break;
                    }

                    if (target != null)
                    {
                        bone.WorldToLocal(target.DrawPosition.X, -target.DrawPosition.Y, out float x, out float y);
                        bone.X = x;
                        bone.Y = y;
                        bone.Rotation = MathHelper.ToDegrees(target.Rotation) + 45;

                        // Draw forward vectors for ragdoll bones
                        Vector2 forward = new Vector2((float)Math.Cos(target.Rotation), (float)Math.Sin(target.Rotation));
                        forward.Normalize();
                        var start = cam.WorldToScreen(target.DrawPosition);
                        var end = start + forward;
                        GUI.DrawLine(spriteBatch, start, end, Color.Red, width: 20);

                        // Draw ragdoll bone positions
                        var size = new Vector2(4, 4);
                        GUI.DrawRectangle(spriteBatch, start - size / 2, size, Color.Red, isFilled: true);

                        // Draw forward vector for spine bones (couldn't get to work)
                        float rot = MathHelper.ToRadians(bone.Rotation);
                        forward = new Vector2((float)Math.Cos(rot), (float)Math.Sin(rot));
                        forward.Normalize();
                        // Not right?
                        float worldX = bone.WorldX;
                        float worldY = bone.WorldY;
                        start = cam.WorldToScreen(new Vector2(worldX, worldY));
                        end = start + forward;
                        GUI.DrawLine(spriteBatch, start, end, Color.White, width: 20);

                        // Draw spine bone positions (start pos is wrong?)
                        size = new Vector2(4, 4);
                        GUI.DrawRectangle(spriteBatch, start - size / 2, size, Color.White, isFilled: true);
                    }
                }
                c.AnimController.skeleton.UpdateWorldTransform();
                GameMain.skeletonRenderer.Draw(c.AnimController.skeleton);
            }
            spriteBatch.End();
            GameMain.skeletonRenderer.End();

            // Test the spine animation
            //GameMain.skeletonRenderer.Begin();
            //foreach (Character c in Character.CharacterList)
            //{
            //    if (c.AnimController.skeleton == null) continue;
            //    c.AnimController.skeleton.X = c.DrawPosition.X;
            //    c.AnimController.skeleton.Y = -c.DrawPosition.Y + 120.0f;
            //    c.AnimController.skeleton.SetToSetupPose();
            //    c.AnimController.animationState.Update((float)(deltaTime));
            //    c.AnimController.animationState.Apply(c.AnimController.skeleton);
            //    c.AnimController.skeleton.UpdateWorldTransform();
            //    GameMain.skeletonRenderer.Draw(c.AnimController.skeleton);
            //}
            //GameMain.skeletonRenderer.End();

            //---

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, null, null, cam.Transform);

            Submarine.DrawFront(spriteBatch, false, null);
            spriteBatch.End();

			//draw the rendertarget and particles that are only supposed to be drawn in water into renderTargetWater
			graphics.SetRenderTarget(renderTargetWater);

			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
			spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), waterColor);
			spriteBatch.End();

			//draw alpha blended particles that are inside a sub
#if LINUX
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#endif
			GameMain.ParticleManager.Draw(spriteBatch, true, true, Particles.ParticleBlendState.AlphaBlend);
			spriteBatch.End();

			graphics.SetRenderTarget(renderTarget);

			//draw alpha blended particles that are not in water
#if LINUX
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#else
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#endif
			GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.AlphaBlend);
			spriteBatch.End();

			//draw additive particles that are not in water
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.None, null, null, cam.Transform);
			GameMain.ParticleManager.Draw(spriteBatch, false, null, Particles.ParticleBlendState.Additive);
			spriteBatch.End();

			graphics.SetRenderTarget(renderTargetFinal);
			Hull.renderer.RenderBack(spriteBatch, renderTargetWater);

			Array.Clear(Hull.renderer.vertices, 0, Hull.renderer.vertices.Length);
			Hull.renderer.PositionInBuffer = 0;
			foreach (Hull hull in Hull.hullList)
			{
				hull.Render(graphics, cam);
			}

			Hull.renderer.Render(graphics, cam, renderTarget, Cam.ShaderTransform);

			spriteBatch.Begin(SpriteSortMode.Immediate,
				BlendState.NonPremultiplied, SamplerState.LinearWrap,
				null, null,
				damageEffect,
				cam.Transform);
			Submarine.DrawDamageable(spriteBatch, damageEffect, false);
			spriteBatch.End();

			//draw additive particles that are inside a sub
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.Default, null, null, cam.Transform);
			GameMain.ParticleManager.Draw(spriteBatch, true, true, Particles.ParticleBlendState.Additive);
			spriteBatch.End();
			if (GameMain.LightManager.LightingEnabled)
			{
				spriteBatch.Begin(SpriteSortMode.Deferred, Lights.CustomBlendStates.Multiplicative, null, DepthStencilState.None, null, null, null);
				spriteBatch.Draw(GameMain.LightManager.lightMap, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
				spriteBatch.End();
			}

			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.None, null, null, cam.Transform);
			foreach (Character c in Character.CharacterList) c.DrawFront(spriteBatch, cam);

			if (Level.Loaded != null) Level.Loaded.DrawFront(spriteBatch);
			spriteBatch.End();

			graphics.SetRenderTarget(null);
			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, null, null, null);
			if (GameMain.LightManager.LosEnabled && Character.Controlled!=null)
			{
				float r = Math.Min(CharacterHUD.damageOverlayTimer * 0.5f, 0.5f);
				spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
				                 Color.Lerp(GameMain.LightManager.AmbientLight * 0.5f, Color.Red, r));
				spriteBatch.End();
                GameMain.LightManager.losEffect.CurrentTechnique = GameMain.LightManager.losEffect.Techniques["LosShader"];
                GameMain.LightManager.losEffect.Parameters["xTexture"].SetValue(renderTargetFinal);
                GameMain.LightManager.losEffect.Parameters["xLosTexture"].SetValue(GameMain.LightManager.losTexture);
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, null, GameMain.LightManager.losEffect, null);
			}
			spriteBatch.Draw(renderTargetFinal, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
			spriteBatch.End();
        }
    }
}
