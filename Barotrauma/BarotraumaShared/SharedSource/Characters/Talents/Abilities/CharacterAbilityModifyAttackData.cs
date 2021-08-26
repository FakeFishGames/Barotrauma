using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyAttackData : CharacterAbility
    {
        private readonly List<Affliction> afflictions;

        float addedDamageMultiplier;
        float addedPenetration;

        public CharacterAbilityModifyAttackData(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            if (abilityElement.GetChildElement("afflictions") is XElement afflictionElements)
            {
                afflictions = CharacterAbilityGroup.ParseAfflictions(CharacterTalent, afflictionElements);
            }
            addedDamageMultiplier = abilityElement.GetAttributeFloat("addeddamagemultiplier", 0f);
            addedPenetration = abilityElement.GetAttributeFloat("addedpenetration", 0f);
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is AttackData attackData)
            {
                if (attackData.Afflictions == null)
                {
                    attackData.Afflictions = afflictions;
                }
                else
                {
                    attackData.Afflictions.AddRange(afflictions);
                }
                attackData.DamageMultiplier += addedDamageMultiplier;
                attackData.AddedPenetration += addedPenetration;
            }
            else
            {
                LogAbilityDataMismatch();
            }
        }
    }
}
