using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class AIController : ISteerable
    {

        public enum AiState { None, Attack, GoTo, Escape }
        public enum SteeringState { Wander, Seek, Escape }

        public Character Character;
        
        protected AiState state;

        protected SteeringManager steeringManager;

        public Vector2 Steering
        {
            get { return Character.AnimController.TargetMovement; }
            set { Character.AnimController.TargetMovement = value; }
        }
        
        public Vector2 SimPosition
        {
            get { return Character.AnimController.Limbs[0].SimPosition; }
        }

        public Vector2 Velocity
        {
            get { return Character.AnimController.Limbs[0].LinearVelocity; }
        }

        public AiState State
        {
            get { return state; }
            set { state = value; }
        }

        public AIController (Character c)
        {
            Character = c;

            steeringManager = new SteeringManager(this);
        }

        public virtual void DebugDraw(SpriteBatch spriteBatch)
        {

        }

        public virtual void OnAttacked(IDamageable attacker, float amount) { }

        public virtual void SelectTarget(AITarget target) { }

        public virtual void Update(float deltaTime) { }

        //protected Structure lastStructurePicked;

        public virtual void FillNetworkData(NetOutgoingMessage message) { }
        public virtual void ReadNetworkData(NetIncomingMessage message) { }
        
    }
}
