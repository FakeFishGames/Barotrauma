using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class WaitAction : EventAction
    {
        [Serialize(0.0f, true)]
        public float Time { get; set; }

        private float timeRemaining;

        public WaitAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            timeRemaining = Time;
        }

        public override bool IsFinished(ref string goTo)
        {
            return timeRemaining <= 0;
        }
        public override void Reset()
        {
            timeRemaining = Time;
        }

        public override void Update(float deltaTime)
        {
            timeRemaining -= deltaTime;
            if (timeRemaining < 0.0f) { timeRemaining = 0.0f; }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(timeRemaining <= 0)} {nameof(WaitAction)} -> (Remaining: {timeRemaining.ColorizeObject()}, Time: {Time.ColorizeObject()})";
        }
    }
}