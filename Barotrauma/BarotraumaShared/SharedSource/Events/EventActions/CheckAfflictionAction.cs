#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    internal class CheckAfflictionAction : BinaryOptionAction
    {
        [Serialize("", true)]
        public string Identifier { get; set; } = "";

        [Serialize("", true)]
        public string TargetTag { get; set; } = "";

        [Serialize(LimbType.None, true, "Only check afflictions on the specified limb type")]
        public LimbType TargetLimb { get; set; }

        [Serialize(true, true, "When set to false when TargetLimb is not specified prevent checking limb-specific afflictions")]
        public bool AllowLimbAfflictions { get; set; }

        public CheckAfflictionAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            if (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(TargetTag)) { return false; }
            List<Character> targets = ParentEvent.GetTargets(TargetTag).OfType<Character>().ToList();

            foreach (var target in targets)
            {
                if (target.CharacterHealth == null) { continue; }
                if (TargetLimb == LimbType.None)
                {
                    if (target.CharacterHealth.GetAffliction(Identifier, AllowLimbAfflictions) != null) { return true; }
                }
                IEnumerable<Affliction> afflictions = target.CharacterHealth.GetAllAfflictions().Where(affliction =>
                {
                    LimbType? limbType = target.CharacterHealth.GetAfflictionLimb(affliction)?.type;
                    if (limbType == null) { return false; }

                    return limbType == TargetLimb || true;
                });

                if (afflictions.Any(a => a.Identifier.Equals(Identifier, StringComparison.OrdinalIgnoreCase))) { return true; }
            }
            return false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckAfflictionAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                $"AfflictionIdentifier: {Identifier.ColorizeObject()}, " +
                $"TargetLimb: {TargetLimb.ColorizeObject()}, " +
                $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}