using Barotrauma.Items.Components;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionIsAiming : AbilityConditionDataless
    {
        private enum WeaponType
        {
            Any = 0,
            Melee = 1,
            Ranged = 2
        };

        private readonly bool hittingCountsAsAiming;

        private readonly WeaponType weapontype;
        public AbilityConditionIsAiming(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            hittingCountsAsAiming = conditionElement.GetAttributeBool("hittingcountsasaiming", false);
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

        protected override bool MatchesConditionSpecific()
        {
            if (character.AnimController is HumanoidAnimController animController)
            {
                foreach (Item item in character.HeldItems)
                {
                    switch (weapontype)
                    {
                        case WeaponType.Melee:
                            var meleeWeapon = item.GetComponent<MeleeWeapon>();
                            if (meleeWeapon != null)
                            {
                                if (animController.IsAimingMelee || (meleeWeapon.Hitting && hittingCountsAsAiming)) { return true; }
                            }
                            break;
                        case WeaponType.Ranged:
                            if (animController.IsAiming && item.GetComponent<RangedWeapon>() != null) { return true; }
                            break;
                        default:
                            if (animController.IsAiming || animController.IsAimingMelee) { return true; }
                            break;
                    }
                }
            }

            return false;
        }
    }
}
