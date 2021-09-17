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

        private readonly WeaponType weapontype;
        public AbilityConditionIsAiming(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
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
            bool aimingCorrectItem = false;
            if (character.AnimController is HumanoidAnimController animController)
            {
                foreach (Item item in character.HeldItems)
                {
                    switch (weapontype)
                    {
                        case WeaponType.Melee:
                            aimingCorrectItem |= item.GetComponent<MeleeWeapon>() != null && animController.IsAimingMelee;
                            break;
                        case WeaponType.Ranged:
                            aimingCorrectItem |= item.GetComponent<RangedWeapon>() != null && animController.IsAiming;
                            break;
                        default:
                            aimingCorrectItem |= animController.IsAiming || animController.IsAimingMelee;
                            break;
                    }
                }
            }

            return aimingCorrectItem;
        }
    }
}
