namespace Barotrauma.Abilities;

internal sealed class CharacterAbilityGiveExperience : CharacterAbility
{
    public override bool AppliesEffectOnIntervalUpdate => true;

    private readonly int amount;

    public CharacterAbilityGiveExperience(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
    {
        amount = abilityElement.GetAttributeInt("amount", 0);
    }

    private void ApplyEffectSpecific(Character targetCharacter)
    {
        targetCharacter.Info?.GiveExperience(amount);
    }

    protected override void ApplyEffect(AbilityObject abilityObject)
    {
        if ((abilityObject as IAbilityCharacter)?.Character is { } targetCharacter)
        {
            ApplyEffectSpecific(targetCharacter);
        }
        else
        {
            ApplyEffectSpecific(Character);
        }
    }

    protected override void ApplyEffect()
    {
        ApplyEffectSpecific(Character);
    }
}