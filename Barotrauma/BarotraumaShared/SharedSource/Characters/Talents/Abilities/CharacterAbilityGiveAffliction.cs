using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveAffliction : CharacterAbility
    {
        private readonly Identifier afflictionId;
        private readonly float strength;
        private readonly string multiplyStrengthBySkill;
        private readonly bool setValue;

        public CharacterAbilityGiveAffliction(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            afflictionId = abilityElement.GetAttributeIdentifier("afflictionid", abilityElement.GetAttributeIdentifier("affliction", Identifier.Empty));
            strength = abilityElement.GetAttributeFloat("strength", 0f);
            multiplyStrengthBySkill = abilityElement.GetAttributeString("multiplystrengthbyskill", string.Empty);
            setValue = abilityElement.GetAttributeBool("setvalue", false);

            if (afflictionId.IsEmpty)
            {
                DebugConsole.ThrowError("Error in CharacterAbilityGiveAffliction - affliction identifier not set.");
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityCharacter character)
            {
                var afflictionPrefab = AfflictionPrefab.Prefabs.Find(a => a.Identifier == afflictionId);
                if (afflictionPrefab == null)
                {
                    DebugConsole.ThrowError($"Error in CharacterAbilityGiveAffliction - could not find an affliction with the identifier \"{afflictionId}\".");
                    return;
                }
                float strength = this.strength;
                if (!string.IsNullOrEmpty(multiplyStrengthBySkill))
                {
                    strength *= Character.GetSkillLevel(multiplyStrengthBySkill);
                }
                character.Character.CharacterHealth.ApplyAffliction(null, afflictionPrefab.Instantiate(strength), allowStacking: !setValue);
            }
        }
    }
}
