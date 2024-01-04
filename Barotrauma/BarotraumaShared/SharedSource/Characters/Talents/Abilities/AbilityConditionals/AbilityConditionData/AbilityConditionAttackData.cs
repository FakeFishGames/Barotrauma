using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionAttackData : AbilityConditionData
    {
        [Flags]
        private enum WeaponType
        {
            Any = 0, 
            Melee = 1, 
            Ranged = 2,
            HandheldRanged = 4,
            Turret = 8,
            NoWeapon = 16
        };

        private static readonly List<WeaponType> WeaponTypeValues = Enum.GetValues(typeof(WeaponType)).Cast<WeaponType>().ToList();

        private readonly string itemIdentifier;
        private readonly Identifier[] tags;
        private readonly WeaponType weapontype;
        private readonly bool ignoreNonHarmfulAttacks;

        private readonly bool ignoreOwnAttacks;

        public AbilityConditionAttackData(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            itemIdentifier = conditionElement.GetAttributeString(nameof(itemIdentifier), string.Empty);
            tags = conditionElement.GetAttributeIdentifierArray(nameof(tags), Array.Empty<Identifier>());
            ignoreNonHarmfulAttacks = conditionElement.GetAttributeBool(nameof(ignoreNonHarmfulAttacks), false);
            ignoreOwnAttacks = conditionElement.GetAttributeBool(nameof(ignoreOwnAttacks), false);

            string weaponTypeStr = conditionElement.GetAttributeString("weapontype", "Any");
            if (!Enum.TryParse(weaponTypeStr, ignoreCase: true, out weapontype))
            {
                DebugConsole.ThrowError($"Error in talent \"{characterTalent.DebugIdentifier}\": \"{weaponTypeStr}\" is not a valid weapon type.",
                    contentPackage: conditionElement.ContentPackage);
            }
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityAttackData attackData)
            {
                if (ignoreOwnAttacks && attackData.Attacker == character) { return false; }

                if (ignoreNonHarmfulAttacks && attackData.SourceAttack != null)
                {
                    if (attackData.SourceAttack.Stun <= 0.0f && (attackData.SourceAttack.Afflictions?.All(a => a.Key.Prefab.IsBuff) ?? true)) 
                    { 
                        return false;
                    }
                }

                Item item = attackData?.SourceAttack?.SourceItem;

                if (!string.IsNullOrEmpty(itemIdentifier))
                {
                    if (item?.Prefab.Identifier != itemIdentifier)
                    {
                        return false;
                    }
                }

                if (tags.Any())
                {
                    if (!tags.All(t => item?.HasTag(t) ?? false))
                    {
                        return false;
                    }
                }

                if (weapontype != WeaponType.Any)
                {
                    foreach (WeaponType wt in WeaponTypeValues)
                    {
                        if (wt == WeaponType.Any || !weapontype.HasFlag(wt)) { continue; }
                        switch (wt)
                        {
                            case WeaponType.Melee:
                                //if the item has an active projectile component (has been fired), don't consider it a melee weapon
                                if (item?.GetComponent<Projectile>() is { IsActive: true }) { continue; }
                                if (item?.GetComponent<MeleeWeapon>() != null) { return true; }
                                break;
                            case WeaponType.Ranged:
                                //if the item has a melee weapon component that's being used now, don't consider it a projectile
                                if (item?.GetComponent<MeleeWeapon>() is { Hitting: true }) { continue; }
                                if (item?.GetComponent<Projectile>() != null) { return true; }
                                break;
                            case WeaponType.HandheldRanged:
                                {
                                    var projectile = item?.GetComponent<Projectile>();
                                    if (projectile?.Launcher?.GetComponent<Holdable>() != null) { return true; }
                                }
                                break;
                            case WeaponType.Turret:
                                {
                                    var projectile = item?.GetComponent<Projectile>();
                                    if (projectile?.Launcher?.GetComponent<Turret>() != null) { return true; }
                                }
                                break;
                            case WeaponType.NoWeapon:
                                if (item == null) { return true; }
                                break;
                        }
                    }
                    return false;
                }                

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(AbilityAttackData));
                return false;
            }
        }
    }
}
