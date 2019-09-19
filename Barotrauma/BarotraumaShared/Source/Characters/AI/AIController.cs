using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public enum AIState { Idle, Attack, Escape, Eat }

    abstract partial class AIController : ISteerable
    {
        public bool Enabled;

        public readonly Character Character;

        private AIState state;

        protected AITarget _previousAiTarget;
        protected AITarget _selectedAiTarget;
        public AITarget SelectedAiTarget
        {
            get { return _selectedAiTarget; }
            protected set
            {
                _previousAiTarget = _selectedAiTarget;
                _selectedAiTarget = value;
            }
        }

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
