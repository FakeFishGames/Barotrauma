using Lidgren.Network;
using System;
using System.Linq;

namespace Barotrauma
{
    class Affliction
    {
        public readonly AfflictionPrefab Prefab;

        public float Strength;

        public Affliction(AfflictionPrefab prefab, float strength)
        {
            Prefab = prefab;
            Strength = strength;
        }

        public Affliction CreateMultiplied(float multiplier)
        {
            return Prefab.Instantiate(Strength * multiplier);
        }

        public void Merge(Affliction affliction)
        {
            Strength = Math.Min(Strength + affliction.Strength, Prefab.MaxStrength);
        }

        public float GetVitalityDecrease()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            return currentEffect.MaxVitalityDecrease * (Strength / Prefab.MaxStrength);
        }

        public virtual void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return;

            Strength += currentEffect.StrengthChange * deltaTime;
            foreach (StatusEffect statusEffect in currentEffect.StatusEffects)
            {
                if (statusEffect.Targets.HasFlag(StatusEffect.TargetType.Character))
                {
                    statusEffect.Apply(ActionType.OnActive, deltaTime, characterHealth.Character, characterHealth.Character);
                }
                if (targetLimb != null && statusEffect.Targets.HasFlag(StatusEffect.TargetType.Limb))
                {
                    statusEffect.Apply(ActionType.OnActive, deltaTime, characterHealth.Character, targetLimb);
                }
                if (targetLimb != null && statusEffect.Targets.HasFlag(StatusEffect.TargetType.AllLimbs))
                {
                    statusEffect.Apply(ActionType.OnActive, deltaTime, targetLimb.character, targetLimb.character.AnimController.Limbs.Cast<ISerializableEntity>().ToList());
                }
            }
        }
    }
}
