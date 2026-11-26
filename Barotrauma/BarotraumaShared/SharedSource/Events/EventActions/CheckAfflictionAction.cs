#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Check whether a target has a specific affliction.
    /// </summary>
    internal class CheckAfflictionAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the affliction.")]
        public Identifier Identifier { get; set; } = Identifier.Empty;

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to check.")]
        public Identifier TargetTag { get; set; } = Identifier.Empty;

        [Serialize("", IsPropertySaveable.Yes, description: "Tag referring to the character who caused the affliction. Can be used to require the affliction to be caused by a specific character.")]
        public Identifier SourceCharacter { get; set; } = Identifier.Empty;

        [Serialize(LimbType.None, IsPropertySaveable.Yes, "Only check afflictions on the specified limb type.")]
        public LimbType TargetLimb { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, "When set to false, limb-specific afflictions are ignored when not checking a specific limb.")]
        public bool AllowLimbAfflictions { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, 
            "Minimum strength of the affliction. "+
            "If 0, the affliction must have a strength above 0 (= the affliction must exist). "+
            "If larger than 0, the affliction must be equal to or larger than this.")]
        public float MinStrength { get; set; }

        public CheckAfflictionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            if (Identifier.IsEmpty || TargetTag.IsEmpty) { return false; }
            List<Character> targets = ParentEvent.GetTargets(TargetTag).OfType<Character>().ToList();

            foreach (var target in targets)
            {
                if (target.CharacterHealth == null) { continue; }
                //no limb type specified, just calculate the strength of the affliction on the character as a whole
                if (TargetLimb == LimbType.None)
                {
                    float currentStrength = target.CharacterHealth.GetAfflictionStrengthByIdentifier(Identifier, AllowLimbAfflictions);
                    if (CheckAfflictionStrength(currentStrength)) { return true; }
                }
                //otherwise go through each affliction individually
                IEnumerable<Affliction> afflictions = target.CharacterHealth.GetAllAfflictions().Where(affliction =>
                {
                    if (affliction.Prefab.LimbSpecific)
                    {
                        LimbType? limbType = target.CharacterHealth.GetAfflictionLimb(affliction)?.type;
                        if (limbType == null || limbType != TargetLimb) { return false; }
                    }
                    if (!SourceCharacter.IsEmpty)
                    {
                        if (!ParentEvent.GetTargets(SourceCharacter).Contains(affliction.Source)) { return false; }
                    }
                    return CheckAfflictionStrength(affliction.Strength);
                });
                if (afflictions.Any(a => a.Identifier == Identifier)) { return true; }
            }

            return false;
        }

        private bool CheckAfflictionStrength(float strength)
        {
            if (MinStrength <= 0.0f)
            {
                //if MinStrength is zero, it's exclusive: the strength of the affliction must be above zero
                //(makes no sense to check if the affliction is "equal to or larger than 0" - it always is)
                if (strength > MinStrength) { return true; }
            }
            else
            {
                //otherwise the check is inclusive (equal to or larger).
                if (strength >= MinStrength) { return true; }
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