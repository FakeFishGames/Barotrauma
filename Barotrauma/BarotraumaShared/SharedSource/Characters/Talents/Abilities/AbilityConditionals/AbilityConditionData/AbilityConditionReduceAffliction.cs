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
            if (abilityData is (Affliction affliction, float reduceAmount))
            {
                if (allowedTypes.Find(c => c == affliction.Prefab.AfflictionType) == null) { return false; }

                if (!string.IsNullOrEmpty(identifier) && affliction.Prefab.Identifier != identifier) { return false; }

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof((Affliction, float)));
                return false;
            }
        }
    }
}
