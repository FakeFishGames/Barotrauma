using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public enum AIState { Idle, Attack, Escape, Eat, Flee }

    abstract partial class AIController : ISteerable
    {
        public bool Enabled;

        public readonly Character Character;

        private AIState state;
        private AIState previousState;

        // Update only when the value changes, not when it keeps the same.
        protected AITarget _lastAiTarget;
        // Updated each time the value is updated (also when the value is the same).
        protected AITarget _previousAiTarget;
        protected AITarget _selectedAiTarget;
        public AITarget SelectedAiTarget
        {
            get { return _selectedAiTarget; }
            protected set
            {
                _previousAiTarget = _selectedAiTarget;
                _selectedAiTarget = value;
                if (_selectedAiTarget != _previousAiTarget)
                {
                    if (_previousAiTarget != null)
                    {
                        _lastAiTarget = _previousAiTarget;
                    }
                }
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
                if (state == value) { return; }
                previousState = state;
                OnStateChanged(state, value);
                state = value;
            }
        }

        public AIState PreviousState => previousState;

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
