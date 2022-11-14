using System.Collections.Immutable;
using Microsoft.Xna.Framework;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToAllies : CharacterAbilityApplyStatusEffects
    {
        private readonly bool allowSelf;
        private readonly float maxDistance = float.MaxValue;
        private readonly bool inSameRoom;
        private readonly ImmutableHashSet<Identifier> jobIdentifiers;

        public CharacterAbilityApplyStatusEffectsToAllies(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            allowSelf = abilityElement.GetAttributeBool("allowself", true);
            maxDistance = abilityElement.GetAttributeFloat("maxdistance", float.MaxValue);
            inSameRoom = abilityElement.GetAttributeBool("insameroom", false);
            jobIdentifiers = abilityElement.GetAttributeIdentifierImmutableHashSet("jobs", ImmutableHashSet<Identifier>.Empty);
        }


        protected override void ApplyEffect()
        {
            foreach (Character character in Character.GetFriendlyCrew(Character))
            {
                if (!allowSelf && character == Character) { continue; }

                if (!jobIdentifiers.IsEmpty)
                {
                    bool hadJob = false;
                    foreach (Identifier job in jobIdentifiers)
                    {
                        if (character.HasJob(job.Value))
                        {
                            hadJob = true;
                            break;
                        }
                    }

                    if (!hadJob) { continue;  }
                }

                if (inSameRoom && !character.IsInSameRoomAs(Character))
                {
                    continue;
                }

                if (maxDistance < float.MaxValue)
                {
                    if (Vector2.DistanceSquared(character.WorldPosition, Character.WorldPosition) > maxDistance * maxDistance) { continue; }
                }
                ApplyEffectSpecific(character);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }

    }
}
