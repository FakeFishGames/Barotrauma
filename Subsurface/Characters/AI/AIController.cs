using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Subsurface
{
    class AIController : ISteerable
    {

        public enum AiState { None, Attack, GoTo, Escape }
        public enum SteeringState { Wander, Seek, Escape }

        public Character character;
        
        protected AiState state;

        protected SteeringManager steeringManager;

        public Vector2 Steering
        {
            get { return character.animController.TargetMovement; }
            set { character.animController.TargetMovement = value; }
        }
        
        public Vector2 Position
        {
            get { return character.animController.limbs[0].SimPosition; }
        }

        public Vector2 Velocity
        {
            get { return character.animController.limbs[0].LinearVelocity; }
        }

        public AiState State
        {
            get { return state; }
        }

        public AIController (Character c)
        {
            character = c;

            steeringManager = new SteeringManager(this);
        }

        public virtual void Update(float deltaTime) { }

        //protected Structure lastStructurePicked;

        public virtual void FillNetworkData(NetOutgoingMessage message) { }
        public virtual void ReadNetworkData(NetIncomingMessage message) { }
        
    }
}
