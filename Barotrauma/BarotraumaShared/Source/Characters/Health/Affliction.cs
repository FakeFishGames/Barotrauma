using System;
using System.Collections.Generic;
using System.Text;

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
            return Prefab.MaxVitalityDecrease * (Strength / 100.0f);
        }

        public virtual void Update(CharacterHealth characterHealth, float deltaTime)
        {
            Strength += Prefab.StrengthChange * deltaTime;
        }
    }
}
