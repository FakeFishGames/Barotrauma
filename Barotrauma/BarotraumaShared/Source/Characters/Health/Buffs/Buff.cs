using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class Buff
    {
        public readonly BuffPrefab Prefab;
        public float Strength;

        /// <summary>
        /// Which character gave this buff
        /// </summary>
        public Character Source;

        public Buff(BuffPrefab prefab, float strength)
        {
            Prefab = prefab;
            Strength = strength;
        }

        public Buff CreateMultiplied(float multiplier)
        {
            return Prefab.Instantiate(Strength * multiplier, Source);
        }

        public override string ToString()
        {
            return "Buff (" + Prefab.Name + ")";
        }

        public virtual void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            BuffPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return;

            Strength += currentEffect.StrengthChange * deltaTime;
            foreach (StatusEffect statusEffect in currentEffect.StatusEffects)
            {
                if (statusEffect.HasTargetType(StatusEffect.TargetType.Character))
                {
                    statusEffect.Apply(ActionType.OnActive, deltaTime, characterHealth.Character, characterHealth.Character);
                }
                if (targetLimb != null && statusEffect.HasTargetType(StatusEffect.TargetType.Limb))
                {
                    statusEffect.Apply(ActionType.OnActive, deltaTime, characterHealth.Character, targetLimb);
                }
                if (targetLimb != null && statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                {
                    statusEffect.Apply(ActionType.OnActive, deltaTime, targetLimb.character, targetLimb.character.AnimController.Limbs.Cast<ISerializableEntity>().ToList());
                }
            }
        }
    }
}
