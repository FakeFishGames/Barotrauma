using Barotrauma.Items.Components;
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
            Ranged = 2
        };

        private readonly string itemIdentifier;
        private readonly string[] tags;
        private WeaponType weapontype;
        public AbilityConditionAttackData(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            itemIdentifier = conditionElement.GetAttributeString("itemidentifier", "");
            tags = conditionElement.GetAttributeStringArray("tags", new string[0], convertToLowerInvariant: true);
            switch (conditionElement.GetAttributeString("weapontype", ""))
            {
                case "melee":
                    weapontype = WeaponType.Melee;
                    break;
                case "ranged":
                    weapontype = WeaponType.Ranged;
                    break;
            }
        }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is AttackData attackData)
            {
                Item item = attackData?.SourceAttack?.SourceItem;

                if (item == null)
                {
                    DebugConsole.AddWarning($"Source Item was not found in {this} for talent {characterTalent.DebugIdentifier}!");
                    return false;
                }

                if (!string.IsNullOrEmpty(itemIdentifier))
                {
                    if (item.prefab.Identifier != itemIdentifier)
                    {
                        return false;
                    }
                }

                if (tags.Any())
                {
                    if (!tags.All(t => item.HasTag(t)))
                    {
                        return false;
                    }
                }

                switch (weapontype)
                {
                    case WeaponType.Melee:
                        return item.GetComponent<MeleeWeapon>() != null;
                    case WeaponType.Ranged:
                        return item.GetComponent<RangedWeapon>() != null;
                }

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(AttackData));
                return false;
            }
        }
    }
}
