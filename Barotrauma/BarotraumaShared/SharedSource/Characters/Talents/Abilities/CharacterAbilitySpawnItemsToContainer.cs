using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilitySpawnItemsToContainer : CharacterAbility
    {
        // currently used only for spawning items to containers

        private readonly List<StatusEffect> statusEffects;
        private readonly List<Item> openedContainers = new List<Item>();
        private readonly float randomChance;

        public CharacterAbilitySpawnItemsToContainer(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statusEffects = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffects"));
            randomChance = abilityElement.GetAttributeFloat("randomchance", 1f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityItem)?.Item is Item item)
            {
                if (openedContainers.Contains(item)) { return; }
                openedContainers.Add(item);
                if (randomChance < Rand.Range(0f, 1f, Rand.RandSync.Unsynced)) { return; }

                foreach (var statusEffect in statusEffects)
                {
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, item, item);
                }
            }
            else
            {
                LogabilityObjectMismatch();
            }
        }
    }
}
