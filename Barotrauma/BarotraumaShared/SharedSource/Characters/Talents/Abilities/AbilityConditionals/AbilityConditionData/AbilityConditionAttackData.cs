using System;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

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
        private readonly string[] tags;
        private readonly WeaponType weapontype;
        private readonly bool ignoreNonHarmfulAttacks;
        public AbilityConditionAttackData(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            itemIdentifier = conditionElement.GetAttributeString("itemidentifier", string.Empty);
            tags = conditionElement.GetAttributeStringArray("tags", Array.Empty<string>(), convertToLowerInvariant: true);
            ignoreNonHarmfulAttacks = conditionElement.GetAttributeBool("ignorenonharmfulattacks", false);

            string weaponTypeStr = conditionElement.GetAttributeString("weapontype", "Any");
            if (!Enum.TryParse(weaponTypeStr, ignoreCase: true, out weapontype))
            {
                DebugConsole.ThrowError($"Error in talent \"{characterTalent.DebugIdentifier}\": \"{weaponTypeStr}\" is not a valid weapon type.");
            }
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityAttackData attackData)
            {
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
                            // it is possible that an item that has both a melee and a projectile component will return true
                            // even when not used as a melee/ranged weapon respectively
                            // attackdata should contain data regarding whether the attack is melee or not
                            case WeaponType.Melee:
                                if (item?.GetComponent<MeleeWeapon>() != null) { return true; }
                                break;
                            case WeaponType.Ranged:
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
