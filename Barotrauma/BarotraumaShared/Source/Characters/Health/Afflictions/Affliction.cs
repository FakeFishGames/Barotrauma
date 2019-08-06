using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class Affliction : ISerializableEntity
    {
        public readonly AfflictionPrefab Prefab;

        public string Name => ToString();

        public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }

        [Serialize(0f, true), Editable]
        public float Strength { get; set; }

        [Serialize("", true), Editable]
        public string Identifier { get; private set; }

        public float DamagePerSecond;
        public float DamagePerSecondTimer;
        public float PreviousVitalityDecrease;

        public float StrengthDiminishMultiplier = 1.0f;
        public Affliction MultiplierSource;

        /// <summary>
        /// Probability for the affliction to be applied. Used by attacks.
        /// </summary>
        public float ApplyProbability = 1.0f;

        /// <summary>
        /// Which character gave this affliction
        /// </summary>
        public Character Source;

        public Affliction(AfflictionPrefab prefab, float strength)
        {
            Prefab = prefab;
            Strength = strength;
            Identifier = prefab.Identifier;
        }

        public void Serialize(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
        }

        public void Deserialize(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public Affliction CreateMultiplied(float multiplier)
        {
            return Prefab.Instantiate(Strength * multiplier, Source);
        }

        public override string ToString()
        {
            return "Affliction (" + Prefab.Name + ")";
        }

        public float GetVitalityDecrease(CharacterHealth characterHealth)
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxStrength - currentEffect.MinStrength <= 0.0f) return 0.0f;

            float currVitalityDecrease = MathHelper.Lerp(
                currentEffect.MinVitalityDecrease, 
                currentEffect.MaxVitalityDecrease, 
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));

            if (currentEffect.MultiplyByMaxVitality) currVitalityDecrease *= characterHealth == null ? 100.0f : characterHealth.MaxVitality;

            return currVitalityDecrease;
        }

        public float GetScreenDistortStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxScreenDistortStrength - currentEffect.MinScreenDistortStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinScreenDistortStrength,
                currentEffect.MaxScreenDistortStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public float GetRadialDistortStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxRadialDistortStrength - currentEffect.MinRadialDistortStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinRadialDistortStrength,
                currentEffect.MaxRadialDistortStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public float GetChromaticAberrationStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxChromaticAberrationStrength - currentEffect.MinChromaticAberrationStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinChromaticAberrationStrength,
                currentEffect.MaxChromaticAberrationStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public float GetScreenBlurStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxScreenBlurStrength - currentEffect.MinScreenBlurStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinScreenBlurStrength,
                currentEffect.MaxScreenBlurStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public void CalculateDamagePerSecond(float currentVitalityDecrease)
        {
            DamagePerSecond = Math.Max(DamagePerSecond, currentVitalityDecrease - PreviousVitalityDecrease);
            if (DamagePerSecondTimer >= 1.0f)
            {
                DamagePerSecond = currentVitalityDecrease - PreviousVitalityDecrease;
                PreviousVitalityDecrease = currentVitalityDecrease;
                DamagePerSecondTimer = 0.0f;
            }
        }

        public float GetResistance(string afflictionId)
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxResistance - currentEffect.MinResistance <= 0.0f) return 0.0f;
            if (afflictionId != currentEffect.ResistanceFor) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinResistance,
                currentEffect.MaxResistance,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }    

        public float GetSpeedMultiplier()
        {
            if (Strength < Prefab.ActivationThreshold) return 1.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 1.0f;
            if (currentEffect.MaxSpeedMultiplier - currentEffect.MinSpeedMultiplier <= 0.0f) return 1.0f;

            return MathHelper.Lerp(
                currentEffect.MinSpeedMultiplier,
                currentEffect.MaxSpeedMultiplier,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public virtual void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return;

            if (currentEffect.StrengthChange < 0) // Reduce diminishing of buffs if boosted
            {
                Strength += currentEffect.StrengthChange * deltaTime * StrengthDiminishMultiplier;
            }
            else // Reduce strengthening of afflictions if resistant
            {
                Strength += currentEffect.StrengthChange * deltaTime * (1f - characterHealth.GetResistance(Prefab.Identifier));
            }

            foreach (StatusEffect statusEffect in currentEffect.StatusEffects)
            {
                statusEffect.SetUser(Source);
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
                if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) || 
                    statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                {
                    var targets = new List<ISerializableEntity>();
                    statusEffect.GetNearbyTargets(characterHealth.Character.WorldPosition, targets);
                    statusEffect.Apply(ActionType.OnActive, deltaTime, targetLimb.character, targets);
                }
            }
        }
    }
}
