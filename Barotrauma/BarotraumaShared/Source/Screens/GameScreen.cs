using Microsoft.Xna.Framework;
#if DEBUG && CLIENT
using Microsoft.Xna.Framework.Input;
#endif

namespace Barotrauma
{
    partial class GameScreen : Screen
    {
        private readonly Camera cam;

        public override Camera Cam
        {
            get { return cam; }
        }

        public double GameTime
        {
            get;
            private set;
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
            GameMain.Config.SaveNewPlayerConfig();
            GameMain.SoundManager.SetCategoryMuffle("default", false);
            GUI.ClearMessages();
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
                !DebugConsole.IsOpen && GUI.KeyboardDispatcher.Subscriber == null)
            {
                var closestSub = Submarine.FindClosest(cam.WorldViewCenter);
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

            GameTime += deltaTime;

            foreach (PhysicsBody body in PhysicsBody.List)
            {
                if (body.Enabled) { body.Update(); }               
            }
            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.IsHighlighted = false;
            }

            if (GameMain.GameSession != null) GameMain.GameSession.Update((float)deltaTime);
#if CLIENT     
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            GameMain.ParticleManager.Update((float)deltaTime); 
            
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("ParticleUpdate", sw.ElapsedTicks);
            sw.Restart();  
            
            GameMain.LightManager.Update((float)deltaTime);

            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("LightUpdate", sw.ElapsedTicks);
            sw.Restart();  
#endif

            if (Level.Loaded != null) Level.Loaded.Update((float)deltaTime, cam);

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("LevelUpdate", sw.ElapsedTicks);

            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.UpdateHUD(cam, Character.Controlled, (float)deltaTime);                
            }
            sw.Restart();              
#endif

            Character.UpdateAll((float)deltaTime, cam);

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("CharacterUpdate", sw.ElapsedTicks);
            sw.Restart(); 
#endif

            StatusEffect.UpdateAll((float)deltaTime);

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("StatusEffectUpdate", sw.ElapsedTicks);
            sw.Restart(); 

            if (Character.Controlled != null && 
                Lights.LightManager.ViewTarget != null)
            {
                Vector2 targetPos = Lights.LightManager.ViewTarget.DrawPosition;
                if (Lights.LightManager.ViewTarget == Character.Controlled && CharacterHealth.OpenHealthWindow != null)
                {
                    Vector2 screenTargetPos = CharacterHealth.OpenHealthWindow.Alignment == Alignment.Left ?
                        new Vector2(GameMain.GraphicsWidth * 0.75f, GameMain.GraphicsHeight * 0.5f) :
                        new Vector2(GameMain.GraphicsWidth * 0.25f, GameMain.GraphicsHeight * 0.5f);
                    Vector2 screenOffset = screenTargetPos - new Vector2(GameMain.GraphicsWidth / 2, GameMain.GraphicsHeight / 2);
                    screenOffset.Y = -screenOffset.Y;
                    targetPos -= screenOffset / cam.Zoom;
                }
                cam.TargetPos = targetPos;
            }
#endif

            cam.MoveCamera((float)deltaTime);
                
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.SetPrevTransform(sub.Position);
            }

            foreach (PhysicsBody body in PhysicsBody.List)
            {
                if (body.Enabled) { body.SetPrevTransform(body.SimPosition, body.Rotation); }
            }
            
            MapEntity.UpdateAll((float)deltaTime, cam);
#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("MapEntityUpdate", sw.ElapsedTicks);
            sw.Restart(); 
#endif
            Character.UpdateAnimAll((float)deltaTime);

            Ragdoll.UpdateAll((float)deltaTime, cam);

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("AnimUpdate", sw.ElapsedTicks);
            sw.Restart(); 
#endif

            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.Update((float)deltaTime);
            }

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("SubmarineUpdate", sw.ElapsedTicks);
            sw.Restart(); 
#endif

            GameMain.World.Step((float)deltaTime);

#if CLIENT
            sw.Stop();
            GameMain.PerformanceCounter.AddElapsedTicks("Physics", sw.ElapsedTicks);
#endif

#if CLIENT
            if (!PlayerInput.PrimaryMouseButtonHeld())
            {
                Inventory.draggingSlot = null;
                Inventory.draggingItem = null;
            }
#endif
        }
    }
}
