using System.Xml.Linq;

namespace Barotrauma
{
    class TriggerEventAction : EventAction
    {
        [Serialize("", true)] 
        public string Identifier { get; set; }

        private bool isFinished;

        public TriggerEventAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            if (GameMain.GameSession?.EventManager != null)
            {
                var eventPrefab = EventSet.GetEventPrefab(Identifier);
                if (eventPrefab == null)
                {
                    DebugConsole.ThrowError($"Error in TriggerEventAction - could not find an event with the identifier {Identifier}.");
                }
                else
                {
                    var ev = eventPrefab.CreateInstance();
                    if (ev != null)
                    {
                        GameMain.GameSession.EventManager.QueuedEvents.Enqueue(ev);
                    }
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(TriggerEventAction)} -> (EventPrefab: {Identifier.ColorizeObject()})";
        }
    }
}