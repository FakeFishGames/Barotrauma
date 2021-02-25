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

            if (!(targets.FirstOrDefault() is { } target)) { return false; }

            if (TargetLimb == LimbType.None)
            {
                Affliction? affliction = target.CharacterHealth?.GetAffliction(Identifier, AllowLimbAfflictions);
                return affliction != null;
            }

            if (target.CharacterHealth == null) { return false; }

            IEnumerable<Affliction> afflictions = target.CharacterHealth.GetAllAfflictions().Where(affliction =>
            {
                LimbType? limbType = target.CharacterHealth.GetAfflictionLimb(affliction)?.type;
                if (limbType == null) { return false; }

                return limbType == TargetLimb || true;
            });

            return afflictions.Any(a => a.Identifier.Equals(Identifier, StringComparison.OrdinalIgnoreCase));
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