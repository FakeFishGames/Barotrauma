using Microsoft.Xna.Framework;
using System;
#if DEBUG && CLIENT
using Microsoft.Xna.Framework.Input;
#endif

namespace Barotrauma
{
    partial class GameScreen : Screen
    {
        private Camera cam;

        public override Camera Cam
        {
            get { return cam; }
        }
        
        public GameScreen()
        {
            cam = new Camera();
            cam.Translate(new Vector2(-10.0f, 50.0f));
        }

        public override void Select()
        {
            base.Select();

            if (Character.Controlled != null)
            {
                cam.Position = Character.Controlled.WorldPosition;
                cam.UpdateTransform(true);
            }
            else if (Submarine.MainSub != null)
            {
                cam.Position = Submarine.MainSub.WorldPosition;
                cam.UpdateTransform(true);
            }

            foreach (MapEntity entity in MapEntity.mapEntityList)
                entity.IsHighlighted = false;
        }

        public override void Deselect()
        {
            base.Deselect();

#if CLIENT
            Sounds.SoundManager.LowPassHFGain = 1.0f;
#endif
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        public override void Update(double deltaTime)
        {

#if DEBUG && CLIENT
            if (GameMain.GameSession != null && GameMain.GameSession.Level != null && GameMain.GameSession.Submarine != null &&
                !DebugConsole.IsOpen)
            {
                /*
                var closestSub = Submarine.FindClosest(cam.WorldViewCenter);
                if (closestSub == null) closestSub = GameMain.GameSession.Submarine;

                Vector2 targetMovement = Vector2.Zero;
                if (PlayerInput.KeyDown(Keys.I)) targetMovement.Y += 1.0f;
                if (PlayerInput.KeyDown(Keys.K)) targetMovement.Y -= 1.0f;
                if (PlayerInput.KeyDown(Keys.J)) targetMovement.X -= 1.0f;
                if (PlayerInput.KeyDown(Keys.L)) targetMovement.X += 1.0f;

                if (targetMovement != Vector2.Zero)
                    closestSub.ApplyForce(targetMovement * closestSub.SubBody.Body.Mass * 100.0f);
                    */
            }
#endif

#if CLIENT
            GameMain.NilModProfiler.SWMapEntityUpdate.Start();
#endif

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.IsHighlighted = false;
            }

#if CLIENT
            GameMain.NilModProfiler.SWMapEntityUpdate.Stop();

            if (GameMain.GameSession != null)
            {
                GameMain.NilModProfiler.SWGameSessionUpdate.Start();
                GameMain.GameSession.Update((float)deltaTime);
                GameMain.NilModProfiler.RecordGameSessionUpdate();
            }

            GameMain.NilModProfiler.SWParticleManager.Start();

            GameMain.ParticleManager.Update((float)deltaTime);

            GameMain.NilModProfiler.RecordParticleManager();
            GameMain.NilModProfiler.SWLightManager.Start();

            GameMain.LightManager.Update((float)deltaTime);

            GameMain.NilModProfiler.RecordLightManager();
#endif

            if (Level.Loaded != null)
            {
#if CLIENT
                GameMain.NilModProfiler.SWLevelUpdate.Start();
#endif
                Level.Loaded.Update((float)deltaTime);
#if CLIENT
                GameMain.NilModProfiler.RecordLevelUpdate();
#endif
            }

#if CLIENT

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.UpdateHUD(cam, Character.Controlled);                
            }
            GameMain.NilModProfiler.SWCharacterUpdate.Start();
#endif
            Character.UpdateAll((float)deltaTime, cam);

#if CLIENT
            GameMain.NilModProfiler.SWCharacterUpdate.Stop();
            GameMain.NilModProfiler.RecordCharacterUpdate();
            GameMain.NilModProfiler.SWStatusEffect.Start();
#endif
            StatusEffect.UpdateAll((float)deltaTime);

#if CLIENT
            GameMain.NilModProfiler.RecordStatusEffect();
            if (Character.Controlled != null && Lights.LightManager.ViewTarget != null)
            {
                cam.TargetPos = Lights.LightManager.ViewTarget.WorldPosition;
            }
#endif

            cam.MoveCamera((float)deltaTime);
#if CLIENT
            GameMain.NilModProfiler.SWSetTransforms.Start();
#endif
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.SetPrevTransform(sub.Position);
            }

            foreach (PhysicsBody pb in PhysicsBody.List)
            {
                pb.SetPrevTransform(pb.SimPosition, pb.Rotation);
            }
#if CLIENT
            GameMain.NilModProfiler.RecordSetTransforms();
            GameMain.NilModProfiler.SWMapEntityUpdate.Start();
#endif
            MapEntity.UpdateAll((float)deltaTime, cam);
#if CLIENT
            GameMain.NilModProfiler.RecordMapEntityUpdate();
            GameMain.NilModProfiler.SWCharacterAnimUpdate.Start();
#endif
            Character.UpdateAnimAll((float)deltaTime);
#if CLIENT
            GameMain.NilModProfiler.RecordCharacterAnimUpdate();
            GameMain.NilModProfiler.SWRagdollUpdate.Start();
#endif
            Ragdoll.UpdateAll((float)deltaTime, cam);
#if CLIENT
            GameMain.NilModProfiler.RecordRagdollUpdate();
            GameMain.NilModProfiler.SWSubmarineUpdate.Start();
#endif
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.Update((float)deltaTime);
            }

#if CLIENT
            GameMain.NilModProfiler.RecordSubmarineUpdate();
            GameMain.NilModProfiler.SWCharacterUpdate.Start();
#endif
            //Process this updates character information
            if (GameMain.NilMod.UseCharStatOptimisation)
            {
                for (int z = GameMain.NilMod.ModifiedCharacterValues.Count - 1; z >= 0; z--)
                {
                    if (GameMain.NilMod.ModifiedCharacterValues[z].character != null && !GameMain.NilMod.ModifiedCharacterValues[z].character.Removed)
                    {
                        Character chartoupdate = GameMain.NilMod.ModifiedCharacterValues[z].character;

                        if(GameMain.NilMod.ModifiedCharacterValues[z].UpdateHealth)
                        {
                            chartoupdate.SetHealth(GameMain.NilMod.ModifiedCharacterValues[z].newhealth);
                        }
                        if (GameMain.NilMod.ModifiedCharacterValues[z].UpdateBleed)
                        {
                            chartoupdate.SetBleed(GameMain.NilMod.ModifiedCharacterValues[z].newbleed);
                        }
                        if (GameMain.NilMod.ModifiedCharacterValues[z].UpdateOxygen)
                        {
                            chartoupdate.SetOxygen(GameMain.NilMod.ModifiedCharacterValues[z].newoxygen);
                        }

                        GameMain.NilMod.ModifiedCharacterValues.RemoveAt(z);

                        if (GameMain.Server != null)
                        {
                            if (Math.Abs(chartoupdate.Health - chartoupdate.lastSentHealth) > (chartoupdate.MaxHealth - chartoupdate.MinHealth) / 255.0f || Math.Sign(chartoupdate.Health) != Math.Sign(chartoupdate.lastSentHealth))
                            {
                                GameMain.Server.CreateEntityEvent(chartoupdate, new object[] { Networking.NetEntityEvent.Type.Status });
                                chartoupdate.lastSentHealth = chartoupdate.Health;
                            }
                            else if (Math.Abs(chartoupdate.Oxygen - chartoupdate.lastSentOxygen) > (100f - -100f) / 255.0f || Math.Sign(chartoupdate.Oxygen) != Math.Sign(chartoupdate.lastSentOxygen))
                            {
                                GameMain.Server.CreateEntityEvent(chartoupdate, new object[] { Networking.NetEntityEvent.Type.Status });
                                chartoupdate.lastSentOxygen = chartoupdate.Oxygen;
                            }
                            else if (chartoupdate.Bleeding > 0f)
                            {
                                GameMain.Server.CreateEntityEvent(chartoupdate, new object[] { Networking.NetEntityEvent.Type.Status });
                            }
                        }
                    }
                }
            }


#if CLIENT
            GameMain.NilModProfiler.RecordCharacterUpdate();
            GameMain.NilModProfiler.SWPhysicsWorldStep.Start();
#endif
            GameMain.World.Step((float)deltaTime);
#if CLIENT
            GameMain.NilModProfiler.RecordPhysicsWorldStep();

            if (!PlayerInput.LeftButtonHeld())
            {
                Inventory.draggingSlot = null;
                Inventory.draggingItem = null;
            }
#endif
        }
    }
}
