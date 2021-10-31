using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyAttackData : CharacterAbility
    {
        private readonly List<Affliction> afflictions;

        private readonly float addedDamageMultiplier;
        private readonly float addedPenetration;
        private readonly bool implode;

        public CharacterAbilityModifyAttackData(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            if (abilityElement.GetChildElement("afflictions") is XElement afflictionElements)
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

                if (implode)
                {
                    // might have issues, as the method used to be private and only used for pressure death
                    attackData.Character?.Implode();
                }

            }
            else
            {
                LogabilityObjectMismatch();
            }
        }
    }
}
