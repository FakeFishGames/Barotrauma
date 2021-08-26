using System;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyFlag : CharacterAbility
    {
        private AbilityFlags abilityFlag;

        private bool lastState;

        public CharacterAbilityModifyFlag(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            abilityFlag = ParseFlagType(abilityElement.GetAttributeString("flagtype", ""), CharacterTalent.DebugIdentifier);
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched != lastState)
            {
                if (conditionsMatched) 
                {
                    Character.AddAbilityFlag(abilityFlag);
                }
                else 
                {
                    Character.RemoveAbilityFlag(abilityFlag);
                }  

                lastState = conditionsMatched;
            }
        }
    }
}
