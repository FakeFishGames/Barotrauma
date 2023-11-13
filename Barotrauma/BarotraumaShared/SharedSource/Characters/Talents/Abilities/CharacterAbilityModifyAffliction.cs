namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyAffliction : CharacterAbility
    {
        private readonly Identifier[] afflictionIdentifiers;

        private readonly Identifier replaceWith;

        private readonly float addedMultiplier;

        public CharacterAbilityModifyAffliction(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            afflictionIdentifiers = abilityElement.GetAttributeIdentifierArray("afflictionidentifiers", System.Array.Empty<Identifier>());
            replaceWith = abilityElement.GetAttributeIdentifier("replacewith", Identifier.Empty);
            addedMultiplier = abilityElement.GetAttributeFloat("addedmultiplier", 0f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            var abilityAffliction = abilityObject as IAbilityAffliction;
            if (abilityAffliction?.Affliction is Affliction affliction)
            {
                foreach (Identifier afflictionIdentifier in afflictionIdentifiers)
                {
                    if (affliction.Identifier != afflictionIdentifier) { continue; }
                    AfflictionPrefab afflictionPrefab = affliction.Prefab;
                    if (!replaceWith.IsEmpty)
                    {
                        AfflictionPrefab.Prefabs.TryGet(replaceWith, out afflictionPrefab);
                    }
                    abilityAffliction.Affliction = new Affliction(afflictionPrefab, affliction.Strength * (1 + addedMultiplier));
                }
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
