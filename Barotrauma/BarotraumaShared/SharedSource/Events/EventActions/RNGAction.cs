using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class RNGAction : BinaryOptionAction
    {
        [Serialize(0.0f, true)]
        public float Chance { get; set; }

        public RNGAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        private bool isFinished;

        protected override bool? DetermineSuccess()
        {
            isFinished = true;
            return Rand.Range(0.0, 1.0) <= Chance;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(RNGAction)} -> (Chance: {Chance.ColorizeObject()}, "+
                   $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}