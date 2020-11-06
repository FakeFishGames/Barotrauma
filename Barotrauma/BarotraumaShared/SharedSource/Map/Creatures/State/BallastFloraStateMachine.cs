#nullable enable
namespace Barotrauma.MapCreatures.Behavior
{
    class BallastFloraStateMachine
    {
        private readonly BallastFloraBehavior parent;

        public BallastFloraStateMachine(BallastFloraBehavior parent)
        {
            this.parent = parent;
        }

        private IBallastFloraState? lastState;
        public IBallastFloraState? State;

        public void EnterState(IBallastFloraState newState)
        {
            lastState = State;
            State?.Exit();
            newState.Enter();
            State = newState;
        }

        public void Update(float deltaTime)
        {
            if (State == null)
            {
                EnterState(new GrowIdleState(parent));
                return;
            }

            State.Update(deltaTime);

            switch (State.GetState())
            {
                case ExitState.Running:
                    break;

                case ExitState.ReturnLast when lastState != null && lastState.GetState() == ExitState.Running:
                    EnterState(lastState);
                    break;

                default:
                    EnterState(new GrowIdleState(parent));
                    break;
            }
        }
    }
}