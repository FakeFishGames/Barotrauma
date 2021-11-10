using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionLocation : AbilityConditionData
    {
        private readonly bool? hasOutpost;
        private readonly string[] locationIdentifiers;

        public AbilityConditionLocation(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            if (conditionElement.Attribute("hasoutpost") != null)
            {
                hasOutpost = conditionElement.GetAttributeBool("hasoutpost", false);
            }
            locationIdentifiers = conditionElement.GetAttributeStringArray("locationtype", new string[0]);
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityLocation abilityLocation)
            {
                if (locationIdentifiers.Any())
                {
                    if (!locationIdentifiers.Contains(abilityLocation.Location.Type.Identifier)) { return false; }
                }
                if (hasOutpost.HasValue)
                {
                    if (hasOutpost.Value != abilityLocation.Location.HasOutpost()) { return false; }
                }
                return true;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItemPrefab));
                return false;
            }
        }
    }
}
