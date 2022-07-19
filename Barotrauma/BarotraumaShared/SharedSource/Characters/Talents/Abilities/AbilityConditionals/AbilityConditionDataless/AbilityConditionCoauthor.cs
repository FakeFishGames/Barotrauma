using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionCoauthor : AbilityConditionDataless
    {
        private readonly string jobIdentifier;

        public AbilityConditionCoauthor(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            jobIdentifier = conditionElement.GetAttributeString("jobidentifier", string.Empty);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (character.SelectedCharacter is Character otherCharacter)
            {
                if (!otherCharacter.HasJob(jobIdentifier)) { return false; }
                if (!(character.SelectedBy == otherCharacter)) { return false; }
                return true;
            }
            return false;
        }
    }
}
