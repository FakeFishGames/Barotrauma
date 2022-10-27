using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupInterval : CharacterAbilityGroup
    {
        private float interval { get; set; }
        public float TimeSinceLastUpdate { get; private set; }

        private float effectDelay;
        private float effectDelayTimer;


        public CharacterAbilityGroupInterval(AbilityEffectType abilityEffectType, CharacterTalent characterTalent, ContentXElement abilityElementGroup) :
            base(abilityEffectType, characterTalent, abilityElementGroup)
        {
            // too many overlapping intervals could cause hitching? maybe randomize a little
            interval = abilityElementGroup.GetAttributeFloat("interval", 0f);
            effectDelay = abilityElementGroup.GetAttributeFloat("effectdelay", 0f);
        }

        public void UpdateAbilityGroup(float deltaTime)
        {
            if (!IsActive) { return; }

            TimeSinceLastUpdate += deltaTime;
            if (TimeSinceLastUpdate < interval) { return; }

            bool shouldApplyDelayedEffect;
            bool conditionsDidntMatch;

            if (AllConditionsMatched())
            {
                effectDelayTimer += TimeSinceLastUpdate;
                shouldApplyDelayedEffect = effectDelayTimer >= effectDelay;
                conditionsDidntMatch = false;
            }
            else
            {
                effectDelayTimer = 0f;
                shouldApplyDelayedEffect = false;
                conditionsDidntMatch = true;
            }

            bool hasFallbacks = fallbackAbilities.Count > 0;

            List<CharacterAbility> abilitiesToRun =
                conditionsDidntMatch && hasFallbacks
                    ? fallbackAbilities
                    : characterAbilities;

            foreach (var characterAbility in abilitiesToRun)
            {
                if (!characterAbility.IsViable()) { continue; }

                characterAbility.UpdateCharacterAbility(
                    shouldApplyDelayedEffect || conditionsDidntMatch,
                    TimeSinceLastUpdate);
            }

            if (shouldApplyDelayedEffect || (conditionsDidntMatch && hasFallbacks))
            {
                timesTriggered++;
            }

            TimeSinceLastUpdate = 0;
        }

        private bool AllConditionsMatched()
        {
            if (timesTriggered >= maxTriggerCount) { return false; }

            foreach (var abilityCondition in abilityConditions)
            {
                if (!abilityCondition.MatchesCondition()) { return false; }
            }

            return true;
        }
    }
}