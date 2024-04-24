using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Gives an affliction to a specific character.
    /// </summary>
    class AfflictionAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the affliction.")]
        public Identifier Affliction { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Strength of the affliction.")]
        public float Strength { get; set; }

        [Serialize(LimbType.None, IsPropertySaveable.Yes, description: "Type of the limb(s) to apply the affliction on. Only valid if the affliction is limb-specific.")]
        public LimbType LimbType { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character to apply the affliction on.")]
        public Identifier TargetTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the strength be multiplied by the maximum vitality of the target?")]
        public bool MultiplyByMaxVitality { get; set; }

        public AfflictionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (Affliction.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in {nameof(AfflictionAction)}: affliction not defined (use the attribute \"{nameof(Affliction)}\").", 
                    contentPackage: element.ContentPackage);
            }
        }

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
            if (AfflictionPrefab.Prefabs.TryGet(Affliction, out var afflictionPrefab))
            {
                var targets = ParentEvent.GetTargets(TargetTag);
                foreach (var target in targets)
                {
                    if (target != null && target is Character character)
                    {
                        float strength = Strength;
                        if (MultiplyByMaxVitality)
                        {
                            strength *= character.MaxVitality;
                        }
                        if (LimbType != LimbType.None)
                        {
                            var limb = character.AnimController.GetLimb(LimbType);
                            if (strength > 0.0f)
                            {
                                character.CharacterHealth.ApplyAffliction(limb, afflictionPrefab.Instantiate(strength), ignoreUnkillability: true);
                            }
                            else if (strength < 0.0f)
                            {
                                character.CharacterHealth.ReduceAfflictionOnLimb(limb, Affliction, -strength);
                            }
                        }
                        else
                        {
                            if (strength > 0.0f)
                            {
                                character.CharacterHealth.ApplyAffliction(null, afflictionPrefab.Instantiate(strength), ignoreUnkillability: true);
                            }
                            else if (strength < 0.0f)
                            {
                                character.CharacterHealth.ReduceAfflictionOnAllLimbs(Affliction, -strength);
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