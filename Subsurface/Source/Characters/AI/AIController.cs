using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class AIController : ISteerable
    {

        public enum AiState { None, Attack, GoTo, Escape }
        public enum SteeringState { Wander, Seek, Escape }

        public bool Enabled;

        public readonly Character Character;
        
        protected AiState state;

        protected SteeringManager steeringManager;

        public SteeringManager SteeringManager
        {
            get { return steeringManager; }
        }

        public Vector2 Steering
        {
            get { return Character.AnimController.TargetMovement; }
            set { Character.AnimController.TargetMovement = value; }
        }
        
        public Vector2 SimPosition
        {
            get { return Character.SimPosition; }
        }

        public Vector2 WorldPosition
        {
            get { return Character.WorldPosition; }
        }

        public Vector2 Velocity
        {
            get { return Character.AnimController.RefLimb.LinearVelocity; }
        }

        public AiState State
        {
            get { return state; }
            set { state = value; }
        }

        public AIController (Character c)
        {
            Character = c;

            Enabled = true;
        }

        public virtual void DebugDraw(SpriteBatch spriteBatch) { }

        public virtual void OnAttacked(IDamageable attacker, float amount) { }

        public virtual void SelectTarget(AITarget target) { }

        public virtual void Update(float deltaTime) { }

        //protected Structure lastStructurePicked;
        
    }
}
