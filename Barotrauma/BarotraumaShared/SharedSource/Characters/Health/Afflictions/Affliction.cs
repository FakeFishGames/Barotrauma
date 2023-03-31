using Barotrauma.Extensions;
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

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

        public float PendingGrainEffectStrength { get; set; }
        public float GrainEffectStrength { get; set; }

        private float fluctuationTimer;

        private AfflictionPrefab.Effect activeEffect;
        private float prevActiveEffectStrength;
        protected bool activeEffectDirty = true;

        protected float _strength;

        [Serialize(0f, IsPropertySaveable.Yes), Editable]
        public virtual float Strength
        {
            get { return _strength; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
#if DEBUG
                    DebugConsole.ThrowError($"Attempted to set an affliction to an invalid strength ({value})\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    return;
                }

                if (_nonClampedStrength < 0 && value > 0)
                {
                    _nonClampedStrength = value;
                }
                float newValue = MathHelper.Clamp(value, 0.0f, Prefab.MaxStrength);
                if (newValue > _strength)
                {
                    PendingGrainEffectStrength = Prefab.GrainBurst;
                    Duration = Prefab.Duration;
                }
                _strength = newValue;
                activeEffectDirty = true;
            }
        }

        private float _nonClampedStrength = -1;
        public float NonClampedStrength => _nonClampedStrength > 0 ? _nonClampedStrength : _strength;

        [Serialize("", IsPropertySaveable.Yes), Editable]
        public Identifier Identifier { get; private set; }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The probability for the affliction to be applied."), Editable(minValue: 0f, maxValue: 1f)]
        public float Probability { get; set; } = 1.0f;

        [Serialize(true, IsPropertySaveable.Yes, description: "Explosion damage is applied per each affected limb. Should this affliction damage be divided by the count of affected limbs (1-15) or applied in full? Default: true. Only affects explosions."), Editable]
        public bool DivideByLimbCount { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Is the damage relative to the max vitality (percentage) or absolute (normal)"), Editable]
        public bool MultiplyByMaxVitality { get; private set; }

        public float DamagePerSecond;
        public float DamagePerSecondTimer;
        public float PreviousVitalityDecrease;

        public (float Value, Affliction Source) StrengthDiminishMultiplier = (1.0f, null);

        public readonly Dictionary<AfflictionPrefab.PeriodicEffect, float> PeriodicEffectTimers = new Dictionary<AfflictionPrefab.PeriodicEffect, float>();

        public double AppliedAsSuccessfulTreatmentTime, AppliedAsFailedTreatmentTime;

        public float Duration;

        /// <summary>
        /// Which character gave this affliction
        /// </summary>
        public Character Source;

        private readonly static LocalizedString[] strengthTexts = new LocalizedString[]
        {
            TextManager.Get("AfflictionStrengthLow"),
            TextManager.Get("AfflictionStrengthMedium"),
            TextManager.Get("AfflictionStrengthHigh")
        };

        public Affliction(AfflictionPrefab prefab, float strength)
        {
#if CLIENT
            prefab?.ReloadSoundsIfNeeded();
#endif
            Prefab = prefab;
            PendingGrainEffectStrength = Prefab.GrainBurst;
            _strength = strength;
            Identifier = prefab.Identifier;

            Duration = prefab.Duration;

            foreach (var periodicEffect in prefab.PeriodicEffects)
            {
                PeriodicEffectTimers[periodicEffect] = Rand.Range(periodicEffect.MinInterval, periodicEffect.MaxInterval);
            }
        }

        /// <summary>
        /// Copy properties here instead of using SerializableProperties (with reflection).
        /// </summary>
        public void CopyProperties(Affliction source)
        {
            Probability = source.Probability;
            DivideByLimbCount = source.DivideByLimbCount;
            MultiplyByMaxVitality = source.MultiplyByMaxVitality;
        }

        public void Serialize(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
        }

        public void Deserialize(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public Affliction CreateMultiplied(float multiplier, Affliction affliction)
        {
            var instance = Prefab.Instantiate(NonClampedStrength * multiplier, Source);
            instance.CopyProperties(affliction);
            return instance;
        }

        public override string ToString() => Prefab == null ? "Affliction (Invalid)" : $"Affliction ({Prefab.Name})";

        public LocalizedString GetStrengthText()
        {
            return GetStrengthText(Strength, Prefab.MaxStrength);
        }

        public static LocalizedString GetStrengthText(float strength, float maxStrength)
        {
            return strengthTexts[
                MathHelper.Clamp((int)Math.Floor(strength / maxStrength * strengthTexts.Length), 0, strengthTexts.Length - 1)];
        }

        public AfflictionPrefab.Effect GetActiveEffect()
        {
            if (activeEffectDirty)
            {
                activeEffect = Prefab.GetActiveEffect(_strength);
                prevActiveEffectStrength = _strength;
                activeEffectDirty = false;
            }
            return activeEffect;
        }

        public float GetVitalityDecrease(CharacterHealth characterHealth)
        {
            return GetVitalityDecrease(characterHealth, Strength);
        }

        public float GetVitalityDecrease(CharacterHealth characterHealth, float strength)
        {
            if (strength < Prefab.ActivationThreshold) { return 0.0f; }
            strength = MathHelper.Clamp(strength, 0.0f, Prefab.MaxStrength);
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxStrength - currentEffect.MinStrength <= 0.0f) { return 0.0f; }

            float currVitalityDecrease = MathHelper.Lerp(
                currentEffect.MinVitalityDecrease,
                currentEffect.MaxVitalityDecrease,
                currentEffect.GetStrengthFactor(this));

            if (currentEffect.MultiplyByMaxVitality)
            {
                currVitalityDecrease *= characterHealth == null ? 100.0f : characterHealth.MaxVitality;
            }

            return currVitalityDecrease;
        }


        public float GetScreenGrainStrength()
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (MathUtils.NearlyEqual(currentEffect.MaxGrainStrength, 0f)) { return 0.0f; }

            float amount = MathHelper.Lerp(
                currentEffect.MinGrainStrength,
                currentEffect.MaxGrainStrength,
                currentEffect.GetStrengthFactor(this)) * GetScreenEffectFluctuation(currentEffect);

            if (Prefab.GrainBurst > 0 && GrainEffectStrength > amount)
            {
                return Math.Min(GrainEffectStrength, 1.0f);
            }

            return amount;
        }

        public float GetScreenDistortStrength()
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxScreenDistort - currentEffect.MinScreenDistort < 0.0f) { return 0.0f; }

            return MathHelper.Lerp(
                currentEffect.MinScreenDistort,
                currentEffect.MaxScreenDistort,
                currentEffect.GetStrengthFactor(this)) * GetScreenEffectFluctuation(currentEffect);
        }

        public float GetRadialDistortStrength()
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxRadialDistort - currentEffect.MinRadialDistort < 0.0f) { return 0.0f; }

            return MathHelper.Lerp(
                currentEffect.MinRadialDistort,
                currentEffect.MaxRadialDistort,
                currentEffect.GetStrengthFactor(this)) * GetScreenEffectFluctuation(currentEffect);
        }

        public float GetChromaticAberrationStrength()
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxChromaticAberration - currentEffect.MinChromaticAberration < 0.0f) { return 0.0f; }

            return MathHelper.Lerp(
                currentEffect.MinChromaticAberration,
                currentEffect.MaxChromaticAberration,
                currentEffect.GetStrengthFactor(this)) * GetScreenEffectFluctuation(currentEffect);
        }

        public float GetAfflictionOverlayMultiplier()
        {
            //If the overlay's alpha progresses linearly, then don't worry about affliction effects.
            if (Prefab.AfflictionOverlayAlphaIsLinear) { return (Strength / Prefab.MaxStrength); }
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxAfflictionOverlayAlphaMultiplier - currentEffect.MinAfflictionOverlayAlphaMultiplier < 0.0f) { return 0.0f; }

            return MathHelper.Lerp(
                currentEffect.MinAfflictionOverlayAlphaMultiplier,
                currentEffect.MaxAfflictionOverlayAlphaMultiplier,
                currentEffect.GetStrengthFactor(this));
        }

        public Color GetFaceTint()
        {
            if (Strength < Prefab.ActivationThreshold) { return Color.TransparentBlack; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return Color.TransparentBlack; }

            return Color.Lerp(
                currentEffect.MinFaceTint,
                currentEffect.MaxFaceTint,
                currentEffect.GetStrengthFactor(this));
        }

        public Color GetBodyTint()
        {
            if (Strength < Prefab.ActivationThreshold) { return Color.TransparentBlack; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return Color.TransparentBlack; }

            return Color.Lerp(
                currentEffect.MinBodyTint,
                currentEffect.MaxBodyTint,
                currentEffect.GetStrengthFactor(this));
        }

        public float GetScreenBlurStrength()
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxScreenBlur - currentEffect.MinScreenBlur < 0.0f) { return 0.0f; }

            return MathHelper.Lerp(
                currentEffect.MinScreenBlur,
                currentEffect.MaxScreenBlur,
                currentEffect.GetStrengthFactor(this)) * GetScreenEffectFluctuation(currentEffect);
        }

        private float GetScreenEffectFluctuation(AfflictionPrefab.Effect currentEffect)
        {
            if (currentEffect == null || currentEffect.ScreenEffectFluctuationFrequency <= 0.0f) { return 1.0f; }
            return ((float)Math.Sin(fluctuationTimer * MathHelper.TwoPi) + 1.0f) * 0.5f;
        }

        public float GetSkillMultiplier()
        {
            if (Strength < Prefab.ActivationThreshold) { return 1.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 1.0f; }

            float amount = MathHelper.Lerp(
                currentEffect.MinSkillMultiplier,
                currentEffect.MaxSkillMultiplier,
                currentEffect.GetStrengthFactor(this));

            return amount;
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

        public float GetResistance(Identifier afflictionId)
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            var affliction = AfflictionPrefab.Prefabs[afflictionId];
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 0.0f; }
            if (!currentEffect.ResistanceFor.Any(r =>
                r == affliction.Identifier ||
                r == affliction.AfflictionType))
            {
                return 0.0f;
            }
            return MathHelper.Lerp(
                currentEffect.MinResistance,
                currentEffect.MaxResistance,
                currentEffect.GetStrengthFactor(this));
        }    

        public float GetSpeedMultiplier()
        {
            if (Strength < Prefab.ActivationThreshold) { return 1.0f; }
            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return 1.0f; }
            return MathHelper.Lerp(
                currentEffect.MinSpeedMultiplier,
                currentEffect.MaxSpeedMultiplier,
                currentEffect.GetStrengthFactor(this));
        }

        public float GetStatValue(StatTypes statType)
        {
            if (GetViableEffect() is not AfflictionPrefab.Effect currentEffect) { return 0.0f; }

            if (!currentEffect.AfflictionStatValues.TryGetValue(statType, out var appliedStat)) { return 0.0f; }

            return MathHelper.Lerp(appliedStat.MinValue, appliedStat.MaxValue, currentEffect.GetStrengthFactor(this));
        }

        public bool HasFlag(AbilityFlags flagType)
        {
            if (GetViableEffect() is not AfflictionPrefab.Effect currentEffect) { return false; }
            return currentEffect.AfflictionAbilityFlags.HasFlag(flagType);
        }

        private AfflictionPrefab.Effect GetViableEffect()
        {
            if (Strength < Prefab.ActivationThreshold) { return null; }
            return GetActiveEffect();
        }

        public virtual void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            foreach (AfflictionPrefab.PeriodicEffect periodicEffect in Prefab.PeriodicEffects)
            {
                PeriodicEffectTimers[periodicEffect] -= deltaTime;
                if (PeriodicEffectTimers[periodicEffect] <= 0.0f)
                {
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                    {
                        PeriodicEffectTimers[periodicEffect] = 0.0f;
                    }
                    else
                    {
                        foreach (StatusEffect statusEffect in periodicEffect.StatusEffects)
                        {
                            ApplyStatusEffect(ActionType.OnActive, statusEffect, 1.0f, characterHealth, targetLimb);
                            PeriodicEffectTimers[periodicEffect] = Rand.Range(periodicEffect.MinInterval, periodicEffect.MaxInterval);
                        }
                    }
                }
            }

            AfflictionPrefab.Effect currentEffect = GetActiveEffect();
            if (currentEffect == null) { return; }

            fluctuationTimer += deltaTime * currentEffect.ScreenEffectFluctuationFrequency;
            fluctuationTimer %= 1.0f;

            if (currentEffect.StrengthChange < 0) // Only apply StrengthDiminish.Multiplier if affliction is being weakened
            {
                float stat = characterHealth.Character.GetStatValue(
                    Prefab.IsBuff
                        ? StatTypes.BuffDurationMultiplier
                        : StatTypes.DebuffDurationMultiplier);

                float durationMultiplier = 1f / (1f + stat);

                _strength += currentEffect.StrengthChange * deltaTime * StrengthDiminishMultiplier.Value * durationMultiplier;
            }
            else if (currentEffect.StrengthChange > 0) // Reduce strengthening of afflictions if resistant
            {
                _strength += currentEffect.StrengthChange * deltaTime * (1f - characterHealth.GetResistance(Prefab));
            }
            // Don't use the property, because it's virtual and some afflictions like husk overload it for external use.
            _strength = MathHelper.Clamp(_strength, 0.0f, Prefab.MaxStrength);
            activeEffectDirty |= !MathUtils.NearlyEqual(prevActiveEffectStrength, _strength);

            foreach (StatusEffect statusEffect in currentEffect.StatusEffects)
            {
                ApplyStatusEffect(ActionType.OnActive, statusEffect, deltaTime, characterHealth, targetLimb);
            }

            float amount = deltaTime;
            if (Prefab.GrainBurst > 0)
            {
                amount /= Prefab.GrainBurst;
            }
            if (PendingGrainEffectStrength >= 0)
            {
                GrainEffectStrength += amount;
                PendingGrainEffectStrength -= deltaTime;
            } 
            else if (GrainEffectStrength > 0)
            {
                GrainEffectStrength -= amount;
            }
        }

        public void ApplyStatusEffects(ActionType type, float deltaTime, CharacterHealth characterHealth, Limb targetLimb)
        {
            var currentEffect = GetActiveEffect();
            if (currentEffect != null)
            {
                foreach (var statusEffect in currentEffect.StatusEffects)
                {
                    ApplyStatusEffect(type, statusEffect, deltaTime, characterHealth, targetLimb);
                }
            }
        }

        private readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();
        public void ApplyStatusEffect(ActionType type, StatusEffect statusEffect, float deltaTime, CharacterHealth characterHealth, Limb targetLimb)
        {
            if (type == ActionType.OnDamaged && !statusEffect.HasRequiredAfflictions(characterHealth.Character.LastDamage)) { return; }

            statusEffect.SetUser(Source);
            if (statusEffect.HasTargetType(StatusEffect.TargetType.Character))
            {
                statusEffect.Apply(type, deltaTime, characterHealth.Character, characterHealth.Character);
            }
            if (targetLimb != null && statusEffect.HasTargetType(StatusEffect.TargetType.Limb))
            {
                statusEffect.Apply(type, deltaTime, characterHealth.Character, targetLimb);
            }
            if (characterHealth?.Character?.AnimController?.Limbs != null && statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
            {
                statusEffect.Apply(type, deltaTime, characterHealth.Character, targets: characterHealth.Character.AnimController.Limbs);
            }
            if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
            {
                targets.Clear();
                statusEffect.AddNearbyTargets(characterHealth.Character.WorldPosition, targets);
                statusEffect.Apply(type, deltaTime, characterHealth.Character, targets);
            }
        }

        /// <summary>
        /// Use this method to skip clamping and additional logic of the setters.
        /// Ideally we would keep this private, but doing so would require too much refactoring.
        /// </summary>
        public void SetStrength(float strength)
        {
            _nonClampedStrength = strength;
            _strength = _nonClampedStrength;
            activeEffectDirty |= !MathUtils.NearlyEqual(_strength, prevActiveEffectStrength);
        }

        public bool ShouldShowIcon(Character afflictedCharacter)
        {
            return Strength >= (afflictedCharacter == Character.Controlled ? Prefab.ShowIconThreshold : Prefab.ShowIconToOthersThreshold);
        }
    }
}
