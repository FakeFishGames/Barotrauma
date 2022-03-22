using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class GodModeAction : EventAction
    {
        [Serialize(true, true)]
        public bool Enabled { get; set; }

        [Serialize("", true)]
        public string TargetTag { get; set; }

        public GodModeAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

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
            var targets = ParentEvent.GetTargets(TargetTag);
            foreach (var target in targets)
            {
                if (target != null && target is Character character)
                {
                    character.GodMode = Enabled;
                }
            }            
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(GodModeAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   (Enabled ? "Enable godmode" : "Disable godmode");
        }
    }
}