using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    /// <summary>
    /// A special affliction type that increases the duration of buffs (afflictions of the type "buff"). The increase is defined using the 
    /// <see cref="AfflictionPrefab.Effect.MinBuffMultiplier"/> and <see cref="AfflictionPrefab.Effect.MaxBuffMultiplier"/> attributes of the affliction effect.
    /// </summary>
    class BuffDurationIncrease : Affliction
    {
        public BuffDurationIncrease(AfflictionPrefab prefab, float strength) : base(prefab, strength)
        {

        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            base.Update(characterHealth, targetLimb, deltaTime);

            var afflictions = characterHealth.GetAllAfflictions();

            if (Strength <= 0)
            {
                foreach (Affliction affliction in afflictions)
                {
                    if (!affliction.Prefab.IsBuff || affliction == this || affliction.StrengthDiminishMultiplier.Source != this) { continue; }
                    affliction.StrengthDiminishMultiplier.Source = null;
                    affliction.StrengthDiminishMultiplier.Value = 1f;
                }
            }
            else
            {
                foreach (Affliction affliction in afflictions)
                {
                    if (!affliction.Prefab.IsBuff || affliction == this) { continue; }
                    float multiplier = GetDiminishMultiplier();
                    if (affliction.StrengthDiminishMultiplier.Value < multiplier && affliction.StrengthDiminishMultiplier.Source != this) { continue; }

                    affliction.StrengthDiminishMultiplier.Source = this;
                    affliction.StrengthDiminishMultiplier.Value = multiplier;
                }
            }
        }

        private float GetDiminishMultiplier()
        {
            if (Strength < Prefab.ActivationThreshold) { return 1.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 1.0f; }

            float multiplier = MathHelper.Lerp(
                currentEffect.MinBuffMultiplier,
                currentEffect.MaxBuffMultiplier,
                currentEffect.GetStrengthFactor(this));
            return 1.0f / Math.Max(multiplier, 0.001f);
        }
    }
}
