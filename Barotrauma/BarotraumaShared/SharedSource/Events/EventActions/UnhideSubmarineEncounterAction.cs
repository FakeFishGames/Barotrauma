using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Linq;

namespace Barotrauma
{
    class UnhideSubmarineEncounterAction : EventAction
    {
        public UnhideSubmarineEncounterAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession is { } gameSession)
            {
                gameSession.Missions.OfType<SubmarineEncounterMission>()
                    .Where(x => x.ShouldSubmarineStartHidden)
                    .ForEach(x => x.UnhideSubmarine());
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(UnhideSubmarineEncounterAction)}";
        }
    }
}