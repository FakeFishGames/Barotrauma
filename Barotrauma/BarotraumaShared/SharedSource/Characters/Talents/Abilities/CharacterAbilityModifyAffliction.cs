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
                    affliction.Strength *= 1 + addedMultiplier;
                    if (!replaceWith.IsEmpty)
                    {
                        if (AfflictionPrefab.Prefabs.TryGet(replaceWith, out AfflictionPrefab afflictionPrefab))
                        {
                            abilityAffliction.Affliction = new Affliction(afflictionPrefab, abilityAffliction.Affliction.Strength);
                        }
                    }                    
                }
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
