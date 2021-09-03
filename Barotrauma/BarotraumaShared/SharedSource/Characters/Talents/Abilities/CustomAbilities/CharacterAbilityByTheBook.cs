using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityByTheBook : CharacterAbility
    {
        private int moneyAmount;
        private int max;

        public CharacterAbilityByTheBook(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            moneyAmount = abilityElement.GetAttributeInt("moneyamount", 0);
            max = abilityElement.GetAttributeInt("max", 0);
        }

        protected override void ApplyEffect()
        {
            IEnumerable<Character> enemyCharacters = Character.CharacterList.Where(c => c.TeamID == CharacterTeamType.None);

            int timesGiven = 0;
            foreach (Character enemyCharacter in enemyCharacters)
            {
                if (!enemyCharacter.IsHuman) { continue; }
                if (enemyCharacter.Submarine == null || enemyCharacter.Submarine != Submarine.MainSub) { continue; }
                if (enemyCharacter.IsDead) { continue; }
                if (!enemyCharacter.LockHands) { continue; }
                if (timesGiven > max) { continue; }
                Character.GiveMoney(moneyAmount);
                timesGiven++;
            }

        }
    }
}
