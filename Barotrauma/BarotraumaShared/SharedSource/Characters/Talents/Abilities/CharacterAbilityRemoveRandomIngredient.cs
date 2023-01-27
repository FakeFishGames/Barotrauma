#nullable enable

using Barotrauma.Items.Components;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityRemoveRandomIngredient : CharacterAbility
    {
        public CharacterAbilityRemoveRandomIngredient(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement) { }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is not Fabricator.AbilityFabricationItemIngredients { Items.Count: > 0 } ingredients) { return; }

            int randomIndex = Rand.Int(ingredients.Items.Count, Rand.RandSync.Unsynced);
            ingredients.Items.RemoveAt(randomIndex);
        }
    }
}