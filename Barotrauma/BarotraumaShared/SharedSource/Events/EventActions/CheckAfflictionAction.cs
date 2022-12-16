#nullable enable
using System.Collections.Generic;
using System.Linq;

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

        [Serialize(0.0f, IsPropertySaveable.Yes, "Minimum strength of the affliction")]
        public float MinStrength { get; set; }

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
                    var affliction = target.CharacterHealth.GetAffliction(Identifier, AllowLimbAfflictions);
                    if (affliction != null && affliction.Strength >= MinStrength) { return true; }
                }
                IEnumerable<Affliction> afflictions = target.CharacterHealth.GetAllAfflictions().Where(affliction =>
                {
                    if (affliction.Prefab.LimbSpecific)
                    {
                        LimbType? limbType = target.CharacterHealth.GetAfflictionLimb(affliction)?.type;
                        if (limbType == null || limbType != TargetLimb) { return false; }
                    }
                    return affliction.Strength >= MinStrength;
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