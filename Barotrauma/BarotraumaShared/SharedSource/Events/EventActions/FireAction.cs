using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class FireAction : EventAction
    {
        [Serialize(10.0f, true)]
        public float Size { get; set; }

        [Serialize("", true)]
        public string TargetTag { get; set; }

        public FireAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

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
                Vector2 pos = target.WorldPosition;

                var newFire = new FireSource(pos);
                newFire.Size = new Vector2(Size, Size);
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(FireAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Size: {Size.ColorizeObject()})";
        }
    }
}