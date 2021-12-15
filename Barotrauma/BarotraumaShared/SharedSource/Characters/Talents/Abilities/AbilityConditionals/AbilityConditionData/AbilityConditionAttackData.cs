using Barotrauma.Items.Components;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionAttackData : AbilityConditionData
    {
        private enum WeaponType
        {
            Any = 0, 
            Melee = 1, 
            Ranged = 2,
            HandheldRanged = 3,
            Turret = 4
        };

        private readonly string itemIdentifier;
        private readonly string[] tags;
        private readonly WeaponType weapontype;
        private readonly bool ignoreNonHarmfulAttacks;
        public AbilityConditionAttackData(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            itemIdentifier = conditionElement.GetAttributeString("itemidentifier", string.Empty);
            tags = conditionElement.GetAttributeStringArray("tags", new string[0], convertToLowerInvariant: true);
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
                    if (item?.prefab.Identifier != itemIdentifier)
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
                    switch (weapontype)
                    {
                        // it is possible that an item that has both a melee and a projectile component will return true
                        // even when not used as a melee/ranged weapon respectively
                        // attackdata should contain data regarding whether the attack is melee or not
                        case WeaponType.Melee:
                            return item?.GetComponent<MeleeWeapon>() != null;
                        case WeaponType.Ranged:
                            return item?.GetComponent<Projectile>() != null;
                        case WeaponType.HandheldRanged:
                            {
                                var projectile = item?.GetComponent<Projectile>();
                                return projectile?.Launcher?.GetComponent<Holdable>() != null;
                            }
                        case WeaponType.Turret:
                            {
                                var projectile = item?.GetComponent<Projectile>();
                                return projectile?.Launcher?.GetComponent<Turret>() != null;
                            }
                    }
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
