#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityRemoveRandomIngredient : CharacterAbility
    {
        private readonly AbilityConditionItem? condition;

        public CharacterAbilityRemoveRandomIngredient(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement) 
        {
            var conditionElement = abilityElement.GetChildElement(nameof(AbilityConditionItem));
            if (conditionElement != null)
            {
                condition = new AbilityConditionItem(CharacterTalent, conditionElement);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is not Fabricator.AbilityFabricationItemIngredients { Items.Count: > 0 } ingredients) { return; }

            List<Item> applicableIngredients = condition == null ?
                ingredients.Items.ToList() :
                ingredients.Items.Where(it => condition.MatchesItem(it.Prefab)).ToList();
            if (applicableIngredients.None()) { return; }

            ingredients.Items.Remove(applicableIngredients.GetRandom(Rand.RandSync.Unsynced));
        }
    }
}