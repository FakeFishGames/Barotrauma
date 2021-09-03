using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityStonewall : CharacterAbility
    {
        private readonly List<StatusEffect> statusEffects;
        private readonly List<StatusEffect> statusEffectsReset;
        private readonly int maxEnemyCount;
        private readonly float squaredDistance;

        public CharacterAbilityStonewall(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statusEffects = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffects"));
            statusEffectsReset = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffectsreset"));
            maxEnemyCount = abilityElement.GetAttributeInt("maxenemycount", 0);
            squaredDistance = MathF.Pow(abilityElement.GetAttributeFloat("distance", 0), 2);
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            int numberOfEnemiesInRange = Character.CharacterList.Count(c => !HumanAIController.IsFriendly(Character, c) && !c.IsDead && Vector2.DistanceSquared(Character.WorldPosition, c.WorldPosition) < squaredDistance);

            foreach (var statusEffect in statusEffectsReset)
            {
                statusEffect.Apply(ActionType.OnAbility, 1f, Character, Character);
            }

            if (conditionsMatched && numberOfEnemiesInRange > 0)
            {
                foreach (var statusEffect in statusEffects)
                {
                    statusEffect.Apply(ActionType.OnAbility, Math.Min(numberOfEnemiesInRange, maxEnemyCount), Character, Character);
                }
            }
        }
    }
}
