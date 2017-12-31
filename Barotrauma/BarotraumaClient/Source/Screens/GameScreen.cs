using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;

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

            DrawMap(graphics, spriteBatch);

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
