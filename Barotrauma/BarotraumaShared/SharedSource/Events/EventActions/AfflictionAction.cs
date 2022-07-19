using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class AfflictionAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Affliction { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes)]
        public float Strength { get; set; }

        [Serialize(LimbType.None, IsPropertySaveable.Yes)]
        public LimbType LimbType { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        public AfflictionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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
            var afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(p => p.Identifier == Affliction);
            if (afflictionPrefab != null)
            {
                var targets = ParentEvent.GetTargets(TargetTag);
                foreach (var target in targets)
                {
                    if (target != null && target is Character character)
                    {
                        if (LimbType != LimbType.None)
                        {
                            var limb = character.AnimController.GetLimb(LimbType);
                            if (Strength > 0.0f)
                            {
                                character.CharacterHealth.ApplyAffliction(limb, afflictionPrefab.Instantiate(Strength));
                            }
                            else if (Strength < 0.0f)
                            {
                                character.CharacterHealth.ReduceAfflictionOnLimb(limb, Affliction, -Strength);
                            }
                        }
                        else
                        {
                            if (Strength > 0.0f)
                            {
                                character.CharacterHealth.ApplyAffliction(null, afflictionPrefab.Instantiate(Strength));
                            }
                            else if (Strength < 0.0f)
                            {
                                character.CharacterHealth.ReduceAfflictionOnAllLimbs(Affliction, -Strength);
                            }
                        }
                    }
                }
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(AfflictionAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Affliction: {Affliction.ColorizeObject()}, Strength: {Strength.ColorizeObject()}, " +
                   $"LimbType: {LimbType.ColorizeObject()})";
        }
    }
}