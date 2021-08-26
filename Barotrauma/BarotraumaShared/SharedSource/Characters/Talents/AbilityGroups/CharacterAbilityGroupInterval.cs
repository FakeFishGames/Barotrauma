using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupInterval : CharacterAbilityGroup
    {
        private float interval { get; set; }
        public float TimeSinceLastUpdate { get; private set; }

        private float effectDelay;
        private float effectDelayTimer;

        public CharacterAbilityGroupInterval(CharacterTalent characterTalent, XElement abilityElementGroup) : base(characterTalent, abilityElementGroup)
        {            
            // too many overlapping intervals could cause hitching? maybe randomize a little
            interval = abilityElementGroup.GetAttributeFloat("interval", 0f);
            effectDelay = abilityElementGroup.GetAttributeFloat("effectdelay", 0f);
        }
        public void UpdateAbilityGroup(float deltaTime)
        {
            if (!IsActive) { return; }
            TimeSinceLastUpdate += deltaTime;
            if (TimeSinceLastUpdate >= interval)
            {
                bool conditionsMatched = IsApplicable();
                effectDelayTimer = conditionsMatched ? effectDelayTimer + TimeSinceLastUpdate : 0f;
                conditionsMatched &= effectDelayTimer >= effectDelay;

                foreach (var characterAbility in characterAbilities)
                {
                    if (characterAbility.IsViable())
                    {
                        characterAbility.UpdateCharacterAbility(conditionsMatched, TimeSinceLastUpdate);
                    }
                }
                TimeSinceLastUpdate = 0;
            }
        }
        private bool IsApplicable()
        {
            return abilityConditions.All(c => c.MatchesCondition());
        }
    }
}
