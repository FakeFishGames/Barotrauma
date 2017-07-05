using Microsoft.Xna.Framework;
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

            if (Character.Controlled!=null)
            {
                cam.Position = Character.Controlled.WorldPosition;
                cam.UpdateTransform();
            }
            else if (Submarine.MainSub != null)
            {
                cam.Position = Submarine.MainSub.WorldPosition;
                cam.UpdateTransform();
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

            foreach (MapEntity e in MapEntity.mapEntityList)
            {
                e.IsHighlighted = false;
            }

#if CLIENT
            if (GameMain.GameSession != null) GameMain.GameSession.Update((float)deltaTime);

            BackgroundCreatureManager.Update((float)deltaTime, cam);

            GameMain.ParticleManager.Update((float)deltaTime);
            
            GameMain.LightManager.Update((float)deltaTime);
#endif

            if (Level.Loaded != null) Level.Loaded.Update((float)deltaTime);

#if CLIENT
            if (Character.Controlled != null && Character.Controlled.SelectedConstruction != null && Character.Controlled.CanInteractWith(Character.Controlled.SelectedConstruction))
            {
                Character.Controlled.SelectedConstruction.UpdateHUD(cam, Character.Controlled);                
            }
#endif

            Character.UpdateAll((float)deltaTime, cam);

            StatusEffect.UpdateAll((float)deltaTime);

#if CLIENT
            if (Character.Controlled != null && Lights.LightManager.ViewTarget != null)
            {
                cam.TargetPos = Lights.LightManager.ViewTarget.WorldPosition;
            }
#endif

            cam.MoveCamera((float)deltaTime);
                
            foreach (Submarine sub in Submarine.Loaded)
            {
                sub.SetPrevTransform(sub.Position);
            }

            foreach (PhysicsBody pb in PhysicsBody.list)
            {
                pb.SetPrevTransform(pb.SimPosition, pb.Rotation);
            }

            MapEntity.UpdateAll((float)deltaTime, cam);

            Character.UpdateAnimAll((float)deltaTime);

            Ragdoll.UpdateAll((float)deltaTime, cam);

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
    }
}
