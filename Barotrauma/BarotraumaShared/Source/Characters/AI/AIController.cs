using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract partial class AIController : ISteerable
    {
        public enum AIState { Idle, Attack, GoTo, Escape, Eat }

        public bool Enabled;

        public readonly Character Character;

        private AIState state;

        protected AITarget selectedAiTarget;

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
            get { return Character.AnimController.Collider.LinearVelocity; }
        }

        public virtual bool CanEnterSubmarine
        {
            get { return true; }
        }

        public virtual bool CanFlip
        {
            get { return true; }
        }

        public virtual AIObjectiveManager ObjectiveManager
        {
            get { return null; }
        }

        public AITarget SelectedAiTarget
        {
            get { return selectedAiTarget; }
        }

        public AIState State
        {
            get { return state; }
            set
            {
                if (state == value) return;
                OnStateChanged(state, value);
                state = value;
            }
        }

        public AIController (Character c)
        {
            Character = c;

            Enabled = true;
        }

        public virtual void OnAttacked(Character attacker, AttackResult attackResult) { }

        public virtual void SelectTarget(AITarget target) { }

        public virtual void Update(float deltaTime) { }

        protected virtual void OnStateChanged(AIState from, AIState to) { }
             
    }
}
