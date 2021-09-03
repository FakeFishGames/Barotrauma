using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionReduceAffliction : AbilityConditionData
    {
        private readonly string[] allowedTypes;
        private readonly string identifier;

        public AbilityConditionReduceAffliction(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            allowedTypes = conditionElement.GetAttributeStringArray("allowedtypes", new string[0], convertToLowerInvariant: true);
            identifier = conditionElement.GetAttributeString("identifier", "");
        }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is IAbilityAffliction abilityAffliction)
            {
                if (allowedTypes.Find(c => c == abilityAffliction.Affliction.Prefab.AfflictionType) == null) { return false; }

                if (!string.IsNullOrEmpty(identifier) && abilityAffliction.Affliction.Prefab.Identifier != identifier) { return false; }

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(IAbilityAffliction));
                return false;
            }
        }
    }
}
