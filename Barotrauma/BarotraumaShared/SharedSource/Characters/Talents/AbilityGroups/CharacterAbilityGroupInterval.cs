using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupInterval : CharacterAbilityGroup
    {
        private readonly float interval;
        public float TimeSinceLastUpdate { get; private set; }

        private readonly float effectDelay;
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

            bool conditionsMatched;

            if (AllConditionsMatched())
            {
                effectDelayTimer += TimeSinceLastUpdate;
                bool shouldApplyDelayedEffect = effectDelayTimer >= effectDelay;
                conditionsMatched = shouldApplyDelayedEffect;
            }
            else
            {
                effectDelayTimer = 0f;
                conditionsMatched = false;
            }

            bool hasFallbacks = fallbackAbilities.Count > 0;

            List<CharacterAbility> abilitiesToRun =
                !conditionsMatched && hasFallbacks
                    ? fallbackAbilities
                    : characterAbilities;

            if (hasFallbacks)
            {
                conditionsMatched = true;
            }

            foreach (var characterAbility in abilitiesToRun)
            {
                if (!characterAbility.IsViable()) { continue; }

                characterAbility.UpdateCharacterAbility(conditionsMatched, TimeSinceLastUpdate);
            }

            if (conditionsMatched)
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