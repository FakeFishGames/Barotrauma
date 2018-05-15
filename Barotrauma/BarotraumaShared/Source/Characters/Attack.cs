using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;


namespace Barotrauma
{
    enum CauseOfDeathType
    {
        Unknown, Pressure, Suffocation, Drowning, Affliction, Disconnected
    }
    
    public enum HitDetection
    {
        Distance,
        Contact
    }

    struct AttackResult
    {
        public readonly float Damage;
        public readonly List<Affliction> Afflictions;

        public readonly Limb HitLimb;

        public readonly List<DamageModifier> AppliedDamageModifiers;
        
        public AttackResult(List<Affliction> afflictions, Limb hitLimb, List<DamageModifier> appliedDamageModifiers = null)
        {
            HitLimb = hitLimb;
            Afflictions = new List<Affliction>();

            foreach (Affliction affliction in afflictions)
            {
                Afflictions.Add(affliction.Prefab.Instantiate(affliction.Strength));
            }
            AppliedDamageModifiers = appliedDamageModifiers;
            Damage = Afflictions.Sum(a => a.GetVitalityDecrease(null));
        }

        public AttackResult(float damage, List<DamageModifier> appliedDamageModifiers = null)
        {
            Damage = damage;
            HitLimb = null;

            Afflictions = null;

            AppliedDamageModifiers = appliedDamageModifiers;
        }
    }
    
    partial class Attack
    {
        [Serialize(HitDetection.Distance, false)]
        public HitDetection HitDetectionType { get; private set; }

        [Serialize(0.0f, false)]
        public float Range { get; private set; }

        [Serialize(0.0f, false)]
        public float DamageRange { get; private set; }

        [Serialize(0.0f, false)]
        public float Duration { get; private set; }
        
        [Serialize(0.0f, false)]
        public float StructureDamage { get; private set; }

        [Serialize(0.0f, false)]
        public float ItemDamage { get; private set; }

        [Serialize(0.0f, false)]
        public float Stun { get; private set; }

        [Serialize(false, false)]
        public bool OnlyHumans { get; private set; }

        [Serialize(0.0f, false)]
        public float Force { get; private set; }

        [Serialize(0.0f, false)]
        public float Torque { get; private set; }

        [Serialize(0.0f, false)]
        public float TargetForce { get; private set; }

        [Serialize(0.0f, false)]
        public float SeverLimbsProbability { get; set; }

        [Serialize(0.0f, false)]
        public float Priority { get; private set; }

        //the indices of the limbs Force is applied on 
        //(if none, force is applied only to the limb the attack is attached to)
        public readonly List<int> ApplyForceOnLimbs;

        public readonly List<Affliction> Afflictions = new List<Affliction>();

        private readonly List<StatusEffect> statusEffects;
        
        public List<Affliction> GetMultipliedAfflictions(float multiplier)
        {
            List<Affliction> multipliedAfflictions = new List<Affliction>();
            foreach (Affliction affliction in Afflictions)
            {
                multipliedAfflictions.Add(affliction.Prefab.Instantiate(affliction.Strength * multiplier));
            }
            return multipliedAfflictions;
        }

        public float GetStructureDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? StructureDamage : StructureDamage * deltaTime;
        }

        public float GetItemDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? ItemDamage : ItemDamage * deltaTime;
        }

        public float GetTotalDamage(bool includeStructureDamage = false)
        {
            float totalDamage = includeStructureDamage ? StructureDamage : 0.0f;
            foreach (Affliction affliction in Afflictions)
            {
                totalDamage += affliction.GetVitalityDecrease(null);
            }
            return totalDamage;
        }

        public Attack(float damage, float bleedingDamage, float burnDamage, float structureDamage, float range = 0.0f)
        {
            if (damage > 0.0f) Afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damage));
            if (bleedingDamage > 0.0f) Afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamage));
            if (burnDamage > 0.0f) Afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamage));

            Range = range;
            DamageRange = range;
            StructureDamage = structureDamage;
        }

        public Attack(XElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);
                                                            
            DamageRange = element.GetAttributeFloat("damagerange", Range);
            
            InitProjSpecific(element);

            string limbIndicesStr = element.GetAttributeString("applyforceonlimbs", "");
            if (!string.IsNullOrWhiteSpace(limbIndicesStr))
            {
                ApplyForceOnLimbs = new List<int>();
                foreach (string limbIndexStr in limbIndicesStr.Split(','))
                {
                    int limbIndex;
                    if (int.TryParse(limbIndexStr, out limbIndex))
                    {
                        ApplyForceOnLimbs.Add(limbIndex);
                    }
                }
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        if (statusEffects == null)
                        {
                            statusEffects = new List<StatusEffect>();
                        }
                        statusEffects.Add(StatusEffect.Load(subElement));
                        break;
                    case "affliction":
                        string afflictionName = subElement.GetAttributeString("name", "").ToLowerInvariant();
                        float afflictionStrength = subElement.GetAttributeFloat("strength", 1.0f);

                        AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Name.ToLowerInvariant() == afflictionName);
                        if (afflictionPrefab == null)
                        {
                            DebugConsole.ThrowError("Affliction prefab \"" + afflictionName + "\" not found.");
                        }
                        else
                        {
                            Afflictions.Add(afflictionPrefab.Instantiate(afflictionStrength));
                        }
                        break;
                }

            }
        }
        partial void InitProjSpecific(XElement element);
        
        public AttackResult DoDamage(Character attacker, IDamageable target, Vector2 worldPosition, float deltaTime, bool playSound = true)
        {
            if (OnlyHumans)
            {
                Character character = target as Character;
                if (character != null && character.ConfigPath != Character.HumanConfigFile) return new AttackResult();
            }

            DamageParticles(deltaTime, worldPosition);

            var attackResult = target.AddDamage(attacker, worldPosition, this, deltaTime, playSound);
            var effectType = attackResult.Damage > 0.0f ? ActionType.OnUse : ActionType.OnFailure;
            if (statusEffects == null) return attackResult;

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.Targets.HasFlag(StatusEffect.TargetType.This))
                {
                    effect.Apply(effectType, deltaTime, attacker, attacker);
                }
                if (target is Character)
                {
                    if (effect.Targets.HasFlag(StatusEffect.TargetType.Character))
                    {
                        effect.Apply(effectType, deltaTime, (Character)target, (Character)target);
                    }
                    if (effect.Targets.HasFlag(StatusEffect.TargetType.Limb))
                    {
                        effect.Apply(effectType, deltaTime, (Character)target, attackResult.HitLimb);
                    }                    
                    if (effect.Targets.HasFlag(StatusEffect.TargetType.AllLimbs))
                    {
                        effect.Apply(effectType, deltaTime, (Character)target, ((Character)target).AnimController.Limbs.Cast<ISerializableEntity>().ToList());
                    }
                }
            }

            return attackResult;
        }

        public AttackResult DoDamageToLimb(Character attacker, Limb targetLimb, Vector2 worldPosition, float deltaTime, bool playSound = true)
        {
            if (targetLimb == null) return new AttackResult();

            if (OnlyHumans)
            {
                if (targetLimb.character != null && targetLimb.character.ConfigPath != Character.HumanConfigFile) return new AttackResult();
            }

            DamageParticles(deltaTime, worldPosition);

            var attackResult = targetLimb.character.ApplyAttack(attacker, worldPosition, this, deltaTime, playSound, targetLimb);
            var effectType = attackResult.Damage > 0.0f ? ActionType.OnUse : ActionType.OnFailure;
            if (statusEffects == null) return attackResult;            

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.Targets.HasFlag(StatusEffect.TargetType.This))
                {
                    effect.Apply(effectType, deltaTime, attacker, attacker);
                }
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Character))
                {
                    effect.Apply(effectType, deltaTime, targetLimb.character, targetLimb.character);
                }
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Limb))
                {
                    effect.Apply(effectType, deltaTime, targetLimb.character, targetLimb);
                }
                if (effect.Targets.HasFlag(StatusEffect.TargetType.AllLimbs))
                {
                    effect.Apply(effectType, deltaTime, targetLimb.character, targetLimb.character.AnimController.Limbs.Cast<ISerializableEntity>().ToList());
                }

            }

            return attackResult;
        }

        partial void DamageParticles(float deltaTime, Vector2 worldPosition);
    }
}
