#nullable enable
namespace Barotrauma.Abilities;

internal class CharacterAbilityUpgradeSubmarine : CharacterAbility
{
    private readonly UpgradePrefab? upgradePrefab;
    private readonly UpgradeCategory? upgradeCategory;
    public readonly int level;

    public override bool AllowClientSimulation => true;

    public CharacterAbilityUpgradeSubmarine(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
    {
        var prefabIdentifier = abilityElement.GetAttributeIdentifier(nameof(upgradePrefab), Identifier.Empty);
        var categoryIdentifier = abilityElement.GetAttributeIdentifier(nameof(upgradeCategory), Identifier.Empty);
        
        if (UpgradePrefab.Find(prefabIdentifier) is not { } foundUpgradePrefab)
        {
            DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, {nameof(CharacterAbilityUpgradeSubmarine)} - {nameof(upgradePrefab)} not found.",
                contentPackage: abilityElement.ContentPackage);
        }
        else
        {
            upgradePrefab = foundUpgradePrefab;
        }

        if (UpgradeCategory.Find(categoryIdentifier) is not { } foundUpgradeCategory)
        {
            DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, {nameof(CharacterAbilityUpgradeSubmarine)} - {nameof(upgradeCategory)} not found.",
                contentPackage: abilityElement.ContentPackage);
        }
        else
        {
            upgradeCategory = foundUpgradeCategory;
        }

        level = abilityElement.GetAttributeInt(nameof(level), 1);
    }

    protected override void ApplyEffect(AbilityObject abilityObject)
    {
        ApplyEffectSpecific();
    }

    protected override void ApplyEffect()
    {
        ApplyEffectSpecific();
    }

    private void ApplyEffectSpecific()
    {
        if (upgradePrefab == null || upgradeCategory == null) { return; }
        if (GameMain.GameSession?.Campaign?.UpgradeManager is not { } upgradeManager) { return; }

        upgradeManager.AddUpgradeExternally(upgradePrefab, upgradeCategory, level);
    }
}