using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyAttackData : CharacterAbility
    {
        private readonly List<Affliction> afflictions = new List<Affliction>();

        private readonly float addedDamageMultiplier;
        private readonly float addedPenetration;
        private readonly bool implode;

        public CharacterAbilityModifyAttackData(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            if (abilityElement.GetChildElement("afflictions") is ContentXElement afflictionElements)
            {
                afflictions = CharacterAbilityGroup.ParseAfflictions(CharacterTalent, afflictionElements);
            }
            addedDamageMultiplier = abilityElement.GetAttributeFloat("addeddamagemultiplier", 0f);
            addedPenetration = abilityElement.GetAttributeFloat("addedpenetration", 0f);
            implode = abilityElement.GetAttributeBool("implode", false);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityAttackData attackData)
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

                attackData.ShouldImplode = implode;
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
