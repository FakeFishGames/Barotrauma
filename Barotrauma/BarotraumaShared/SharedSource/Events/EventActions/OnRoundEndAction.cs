#nullable enable

namespace Barotrauma
{
    /// <summary>
    /// Executes all the child actions when the round ends.
    /// </summary>
    class OnRoundEndAction : EventAction
    {
        private readonly SubactionGroup subActions;

        public OnRoundEndAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            subActions = new SubactionGroup(parentEvent, element);
        }

        public override bool IsFinished(ref string goToLabel)
        {
            return false;
        }

        public override void Update(float deltaTime)
        {
            int remainingTries = 100;
            string? throwaway = null;
            //normally the ref string goTo passed to IsFinished should be used to jump another place in the event,
            //but in this case we don't want that (the subactions should just run once when the round ends)
            while (remainingTries > 0 && !subActions.IsFinished(ref throwaway))
            {
                subActions.Update(deltaTime);
                Entity.Spawner?.Update(createNetworkEvents: false);
                remainingTries--;
            }
        }

        public override void Reset()
        {
        }

        public override string ToDebugString()
        {
            return nameof(OnRoundEndAction);
        }
    }
}
