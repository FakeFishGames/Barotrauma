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

        protected override bool? DetermineSuccess()
        {
            return Rand.Range(0.0, 1.0) <= Chance;
        }

        public override string ToDebugString()
        {
            string subActionStr = "";
            if (succeeded.HasValue)
            {
                subActionStr = $"\n            Sub action: {(succeeded.Value ? Success : Failure)?.CurrentSubAction.ColorizeObject()}";
            }
            return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(RNGAction)} -> (Chance: {Chance.ColorizeObject()}, "+
                   $"Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})" +
                   subActionStr;
        }
    }
}