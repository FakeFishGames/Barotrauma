using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class GameScreen : Screen
    {
        //public Color waterColor = GameMain.NilMod.WaterColour;

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

            if (Character.Spied != null && Character.Spied.SelectedConstruction != null && Character.Spied.CanInteractWith(Character.Spied.SelectedConstruction))
            {
                Character.Spied.SelectedConstruction.DrawHUD(spriteBatch, cam, Character.Spied);
            }
            else if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.DrawHUD(spriteBatch, cam, Character.Controlled);
            }

            if (Character.Spied != null && cam != null)
            {
                Character.Spied.DrawHUD(spriteBatch, cam);
            }
            else if (Character.Controlled != null && cam != null)
            {
                Character.Controlled.DrawHUD(spriteBatch, cam);
            }

            if (GameMain.GameSession != null) GameMain.GameSession.Draw(spriteBatch);

            if(GameMain.NilMod.ShowObjectiveIndicatorsAsPlayer && !GUI.DisableHUD)
            {
                DrawIndicators(spriteBatch);
            }
            else if (Character.Controlled == null && !GUI.DisableHUD)
            {
                DrawIndicators(spriteBatch);
            }

            GUI.Draw((float)deltaTime, spriteBatch, cam);

            spriteBatch.End();
        }

        public void DrawIndicators(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < Submarine.MainSubs.Length; i++)
            {
                if (Submarine.MainSubs[i] == null) continue;
                if (Level.Loaded != null && Submarine.MainSubs[i].WorldPosition.Y < Level.MaxEntityDepth) continue;

                Color indicatorColor = i == 0 ? Color.LightBlue * 0.5f : Color.Red * 0.5f;
                DrawSubmarineIndicator(spriteBatch, Submarine.MainSubs[i], indicatorColor);
            }

            if (GameMain.NilMod.ShowCreatureIndicators)
            {
                for (int i = 0; i < Character.CharacterList.Count; i++)
                {
                    if (Character.CharacterList[i].Removed) continue;
                    if (Character.CharacterList[i].AIController == null || Character.CharacterList[i].AIController is HumanAIController) continue;
                    if (Character.CharacterList[i].IsRemotePlayer) continue;

                    if (Level.Loaded != null && Character.CharacterList[i].WorldPosition.Y < Level.MaxEntityDepth) continue;

                    Color indicatorColor = Character.CharacterList[i].IsDead ? Color.White * 0.3f : Color.White * 0.3f;

                    DrawCreatureIndicator(spriteBatch, Character.CharacterList[i], indicatorColor);
                }
            }

            if (GameMain.NilMod.ShowRespawnIndicators)
            {
                if (GameMain.NetworkMember != null)
                {
                    if (GameMain.NetworkMember.respawnManager != null)
                    {
                        if (GameMain.NetworkMember.respawnManager.respawnShuttle != null)
                        {
                            if (GameMain.NetworkMember.RespawnManager.Submarine.Position.Y < Level.ShaftHeight - 100f && GameMain.NetworkMember.RespawnManager.CurrentState != Networking.RespawnManager.State.Waiting)
                            {
                                DrawRespawnIndicator(spriteBatch, GameMain.NetworkMember.respawnManager.respawnShuttle, Color.Orange);
                            }
                        }
                    }
                }
            }

            if (GameMain.NilMod.ShowShuttleIndicators)
            {
                List<Submarine> Shuttles = new List<Submarine>(Submarine.Loaded);
                for (int i = Shuttles.Count - 1; i >= 0; i--)
                {
                    if (Submarine.MainSubs[0] != null)
                    {
                        if (Submarine.MainSubs[0] == Shuttles[i] || Shuttles[i].DockedTo.Contains(Submarine.MainSubs[0])) Shuttles.RemoveAt(i);
                        else if (Submarine.MainSubs[1] != null) if (Submarine.MainSubs[1] == Shuttles[i] || Shuttles[i].DockedTo.Contains(Submarine.MainSubs[1])) Shuttles.RemoveAt(i);
                    }
                }

                //Do not count the respawn shuttle as a shuttle
                if (GameMain.NetworkMember != null)
                {
                    if (GameMain.NetworkMember.respawnManager != null)
                    {
                        if (GameMain.NetworkMember.respawnManager.respawnShuttle != null)
                        {
                            Shuttles.Remove(GameMain.NetworkMember.respawnManager.respawnShuttle);
                        }
                    }
                }

                for (int i = 0; i < Shuttles.Count; i++)
                {
                    DrawShuttleIndicator(spriteBatch, Shuttles[i], Shuttles[i].TeamID == 1 ? Color.SteelBlue : Color.Firebrick);
                }
            }

            if (GameMain.NilMod.ShowObjectiveIndicators)
            {
                if (GameMain.GameSession != null && GameMain.GameSession.GameMode != null && GameMain.GameSession.GameMode.Mission != null)
                {
                    if (GameMain.GameSession.GameMode.Mission.ToString() == "Barotrauma.SalvageMission")
                    {
                        SalvageMission salvagemission = (SalvageMission)GameMain.GameSession.GameMode.Mission;
                        DrawObjectIndicator(spriteBatch, salvagemission.item, new Color(255, 40, 40, 175));
                    }
                    else if (GameMain.GameSession.GameMode.Mission.ToString() == "Barotrauma.MonsterMission")
                    {
                        MonsterMission monstermission = (MonsterMission)GameMain.GameSession.GameMode.Mission;
                        DrawCreatureIndicator(spriteBatch, monstermission.monster, new Color(255, 40, 40, 175));
                    }
                    else if (GameMain.GameSession.GameMode.Mission.ToString() == "Barotrauma.CargoMission")
                    {
                        CargoMission cargomission = (CargoMission)GameMain.GameSession.GameMode.Mission;

                        for (int i = cargomission.items.Count - 1; i >= 0; i--)
                        {
                            DrawObjectIndicator(spriteBatch, cargomission.items[i], new Color(255, 40, 40, 175));
                        }
                    }
                }
            }
        }

        public void DrawMap(GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.UpdateTransform();
            }

            GameMain.ParticleManager.UpdateTransforms();

            GameMain.LightManager.ObstructVision = Character.Controlled != null && Character.Controlled.ObstructVision;

            if (GameMain.NilMod.RenderOther)
            {
                GameMain.LightManager.UpdateLightMap(graphics, spriteBatch, cam, lightBlur.Effect);
                if (Character.Controlled != null)
                {
                    GameMain.LightManager.UpdateObstructVision(graphics, spriteBatch, cam, Character.Controlled.CursorWorldPosition);
                }
            }

            graphics.SetRenderTarget(renderTargetBackground);
            if (Level.Loaded == null)
            {
                graphics.Clear(new Color(11, 18, 26, 255));
            }
            else
            {
                //graphics.Clear(new Color(255, 255, 255, 255));
                if (GameMain.NilMod.RenderLevel) Level.Loaded.DrawBack(graphics, spriteBatch, cam);
            }

            if (!GameMain.NilMod.RenderLevel)
            {
                graphics.Clear(new Color(25, 25, 25, 255));
            }

            if (GameMain.NilMod.RenderOther)
            {
                //draw alpha blended particles that are in water and behind subs
#if LINUX
			    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.None, null, null, cam.Transform);
#else
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
#endif
                GameMain.ParticleManager.Draw(spriteBatch, true, false, Particles.ParticleBlendState.AlphaBlend);
                spriteBatch.End();
            }

            if (GameMain.NilMod.RenderOther)
            {
                //draw additive particles that are in water and behind subs
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.None, null, null, cam.Transform);
                GameMain.ParticleManager.Draw(spriteBatch, true, false, Particles.ParticleBlendState.Additive);
                spriteBatch.End();
            }

            if (GameMain.NilMod.RenderStructure)
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
                Submarine.DrawBack(spriteBatch, false, s => s is Structure && ((Structure)s).ResizeVertical && ((Structure)s).ResizeHorizontal);
                foreach (Structure s in Structure.WallList)
                {
                    if ((s.ResizeVertical != s.ResizeHorizontal) && s.CastShadow)
                    {
                        GUI.DrawRectangle(spriteBatch, new Vector2(s.DrawPosition.X - s.WorldRect.Width / 2, -s.DrawPosition.Y - s.WorldRect.Height / 2), new Vector2(s.WorldRect.Width, s.WorldRect.Height), Color.Black, true);
                    }
                }
                spriteBatch.End();
            }

            graphics.SetRenderTarget(renderTarget);

			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, null);
			spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
			spriteBatch.End();

            spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, null, DepthStencilState.None, null, null, cam.Transform);
            if (GameMain.NilMod.RenderStructure)
            { 
                Submarine.DrawBack(spriteBatch, false, s => !(s is Structure));
                Submarine.DrawBack(spriteBatch, false, s => s is Structure && !(((Structure)s).ResizeVertical && ((Structure)s).ResizeHorizontal));
            }
            else if(GameMain.NilMod.RenderLevel)
            {
                Submarine.DrawBackDoorsOnly(spriteBatch, false, s => !(s is Structure));
                Submarine.DrawBack(spriteBatch, false, s => s is Structure && !(((Structure)s).ResizeVertical && ((Structure)s).ResizeHorizontal));
            }
            if (GameMain.NilMod.RenderCharacter)
            {
                foreach (Character c in Character.CharacterList) c.Draw(spriteBatch);
            }
                spriteBatch.End();

            if (GameMain.NilMod.RenderStructure || GameMain.NilMod.RenderLevel)
            {
                spriteBatch.Begin(SpriteSortMode.BackToFront, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, null, null, cam.Transform);
                if (GameMain.NilMod.RenderStructure)
                {
                    Submarine.DrawFront(spriteBatch, false, null);
                }
                else if (GameMain.NilMod.RenderLevel)
                {
                    Submarine.DrawFront(spriteBatch, false, s => s is Structure && !(((Structure)s).ResizeVertical && ((Structure)s).ResizeHorizontal));
                    Submarine.DrawFrontDoorsOnly(spriteBatch, false, s => !(s is Structure));
                }
                spriteBatch.End();
            }

            //draw the rendertarget and particles that are only supposed to be drawn in water into renderTargetWater
            graphics.SetRenderTarget(renderTargetWater);

            if(!GameMain.NilMod.RenderOther) graphics.Clear(new Color(0, 0, 0, 0));

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
            if (GameMain.NilMod.RenderOther)
            {
                spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), GameMain.NilMod.WaterColour);
            }
            else
            {
                spriteBatch.Draw(renderTarget, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), new Color(255, 255, 255, 0));
            }
                spriteBatch.End();

            if (GameMain.NilMod.RenderOther)
            {
                //draw alpha blended particles that are inside a sub
#if LINUX
			    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#else
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, null, DepthStencilState.DepthRead, null, null, cam.Transform);
#endif
                GameMain.ParticleManager.Draw(spriteBatch, true, true, Particles.ParticleBlendState.AlphaBlend);
                spriteBatch.End();
            }

            if (GameMain.NilMod.RenderOther)
            {
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
            }

			graphics.SetRenderTarget(renderTargetFinal);
            //if (GameMain.NilMod.RenderStructure)
            //{
                Hull.renderer.RenderBack(spriteBatch, renderTargetWater);
            //}

			Array.Clear(Hull.renderer.vertices, 0, Hull.renderer.vertices.Length);
			Hull.renderer.PositionInBuffer = 0;
            if (GameMain.NilMod.RenderStructure)
            {
                foreach (Hull hull in Hull.hullList)
                {
                    hull.Render(graphics, cam);
                }
            }

                Hull.renderer.Render(graphics, cam, renderTarget, Cam.ShaderTransform);

                spriteBatch.Begin(SpriteSortMode.Immediate,
                    BlendState.NonPremultiplied, SamplerState.LinearWrap,
                    null, null,
                    damageEffect,
                    cam.Transform);
            if (GameMain.NilMod.RenderStructure || GameMain.NilMod.RenderLevel)
            {
                Submarine.DrawDamageable(spriteBatch, damageEffect, false);
            }
            spriteBatch.End();

            if (GameMain.NilMod.RenderOther)
            {
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
            }

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearWrap, DepthStencilState.None, null, null, cam.Transform);
            if (GameMain.NilMod.RenderCharacter)
            {
                foreach (Character c in Character.CharacterList) c.DrawFront(spriteBatch, cam);
            }
            if (GameMain.NilMod.RenderLevel)
            {
                if (Level.Loaded != null) Level.Loaded.DrawFront(spriteBatch);
            }
            spriteBatch.End();

            graphics.SetRenderTarget(null);

			spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, null, null, null);
			spriteBatch.Draw(renderTargetFinal, new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.White);
			spriteBatch.End();


            if (GameMain.NilMod.RenderOther)
            {
                if (GameMain.LightManager.LosEnabled && Character.Controlled != null)
                {
                    GameMain.LightManager.LosEffect.CurrentTechnique = GameMain.LightManager.LosEffect.Techniques["LosShader"];
#if LINUX
                GameMain.LightManager.LosEffect.Parameters["TextureSampler+xTexture"].SetValue(renderTargetBackground); 
                GameMain.LightManager.LosEffect.Parameters["LosSampler+xLosTexture"].SetValue(GameMain.LightManager.losTexture); 
#else
                    GameMain.LightManager.LosEffect.Parameters["xTexture"].SetValue(renderTargetBackground);
                    GameMain.LightManager.LosEffect.Parameters["xLosTexture"].SetValue(GameMain.LightManager.losTexture);
#endif

                    //convert the los color to HLS and make sure the luminance of the color is always the same regardless 
                    //of the ambient light color and the luminance of the damage overlight color 
                    float r = Math.Min(CharacterHUD.damageOverlayTimer * 0.5f, 0.5f);
                    Vector3 losColorHls = Color.Lerp(GameMain.LightManager.AmbientLight, Color.Red, r).RgbToHLS();
                    losColorHls.Y = 0.1f;
                    Color losColor = ToolBox.HLSToRGB(losColorHls);

                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, null, null, GameMain.LightManager.LosEffect, null);
                    spriteBatch.Draw(renderTargetBackground, new Rectangle(0, 0, spriteBatch.GraphicsDevice.Viewport.Width, spriteBatch.GraphicsDevice.Viewport.Height), losColor);
                    spriteBatch.End();
                }
            }
        }
    }
}
