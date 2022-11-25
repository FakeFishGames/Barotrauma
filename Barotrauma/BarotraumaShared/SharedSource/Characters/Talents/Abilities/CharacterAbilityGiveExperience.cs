namespace Barotrauma.Abilities;

internal sealed class CharacterAbilityGiveExperience : CharacterAbility
{
    public override bool AppliesEffectOnIntervalUpdate => true;

    private readonly int amount;
    private readonly int level;

    public CharacterAbilityGiveExperience(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
    {
        amount = abilityElement.GetAttributeInt("amount", 0);
        level = abilityElement.GetAttributeInt("level", 0);

        if (amount == 0 && level == 0)
        {
            DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier} - no exp amount or level defined in {nameof(CharacterAbilityGiveExperience)}.");
        }
        if (amount > 0 && level > 0)
        {
            DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier} - {nameof(CharacterAbilityGiveExperience)} defines both an exp amount and a level.");
        }
    }

    private void ApplyEffectSpecific(Character targetCharacter)
    {
        if (amount != 0)
        {
            targetCharacter.Info?.GiveExperience(amount);
        }
        if (level > 0)
        {
            targetCharacter.Info?.GiveExperience(targetCharacter.Info.GetExperienceRequiredForLevel(level));            
        }
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