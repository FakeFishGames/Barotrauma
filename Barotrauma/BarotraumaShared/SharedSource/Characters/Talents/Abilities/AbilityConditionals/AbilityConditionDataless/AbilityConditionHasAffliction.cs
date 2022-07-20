using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasAffliction : AbilityConditionDataless
    {
        private string afflictionIdentifier;
        private float minimumPercentage;


        public AbilityConditionHasAffliction(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            afflictionIdentifier = conditionElement.GetAttributeString("afflictionidentifier", "");
            minimumPercentage = conditionElement.GetAttributeFloat("minimumpercentage", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (!string.IsNullOrEmpty(afflictionIdentifier))
            {
                var affliction = character.CharacterHealth.GetAffliction(afflictionIdentifier);

                if (affliction == null) { return false; }

                return minimumPercentage <= affliction.Strength / affliction.Prefab.MaxStrength;
            }
            return false;
        }
    }
}
