using System;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyFlag : CharacterAbility
    {
        private readonly AbilityFlags abilityFlag;

        private bool lastState;
        public override bool AllowClientSimulation => true;

        public CharacterAbilityModifyFlag(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            abilityFlag = CharacterAbilityGroup.ParseFlagType(abilityElement.GetAttributeString("flagtype", ""), CharacterTalent.DebugIdentifier);
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
