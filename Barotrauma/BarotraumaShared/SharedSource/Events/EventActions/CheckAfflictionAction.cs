#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    internal class CheckAfflictionAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; } = Identifier.Empty;

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; } = Identifier.Empty;

        [Serialize(LimbType.None, IsPropertySaveable.Yes, "Only check afflictions on the specified limb type")]
        public LimbType TargetLimb { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, "When set to false when TargetLimb is not specified prevent checking limb-specific afflictions")]
        public bool AllowLimbAfflictions { get; set; }

        public CheckAfflictionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            if (Identifier.IsEmpty || TargetTag.IsEmpty) { return false; }
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

                if (afflictions.Any(a => a.Identifier == Identifier)) { return true; }
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