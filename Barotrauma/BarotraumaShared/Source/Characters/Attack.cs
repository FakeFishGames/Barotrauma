using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;


namespace Barotrauma
{
    enum CauseOfDeath
    {
        Damage, Bloodloss, Pressure, Suffocation, Drowning, Burn, Husk, Disconnected
    }

    [Flags]
    public enum DamageType
    {
        None = 0,
        Blunt = 1,
        Slash = 2,
        Burn = 4,
        Any = Blunt | Slash | Burn
    }

    public enum HitDetection
    {
        Distance,
        Contact
    }

    struct AttackResult
    {
        public readonly float Damage;
        public readonly float Bleeding;

        public readonly List<DamageModifier> AppliedDamageModifiers;

        public AttackResult(float damage, float bleeding, List<DamageModifier> appliedDamageModifiers = null)
        {
            this.Damage = damage;
            this.Bleeding = bleeding;

            this.AppliedDamageModifiers = appliedDamageModifiers;
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

        [Serialize(DamageType.None, false)]
        public DamageType DamageType { get; private set; }

        [Serialize(0.0f, false)]
        public float StructureDamage { get; private set; }

        [Serialize(0.0f, false)]
        public float Damage { get; private set; }

        [Serialize(0.0f, false)]
        public float BleedingDamage { get; private set; }

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
        
        private readonly List<StatusEffect> statusEffects;

        public float GetDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? Damage : Damage * deltaTime;
        }

        public float GetBleedingDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? BleedingDamage : BleedingDamage * deltaTime;
        }

        public float GetStructureDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? StructureDamage : StructureDamage * deltaTime;
        }

        public Attack(float damage, float structureDamage, float bleedingDamage, float range = 0.0f)
        {
            Range = range;
            DamageRange = range;
            this.Damage = damage;
            this.StructureDamage = structureDamage;
            this.BleedingDamage = bleedingDamage;
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
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Character) && target is Character)
                {
                    effect.Apply(effectType, deltaTime, (Character)target, (Character)target);
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
            }

            return attackResult;
        }

        partial void DamageParticles(float deltaTime, Vector2 worldPosition);
    }
}
