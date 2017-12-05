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
        public readonly float Range;
        public readonly float DamageRange;
        public readonly float Duration;

        public readonly DamageType DamageType;

        private readonly float structureDamage;
        private readonly float damage;
        private readonly float bleedingDamage;

        private readonly bool onlyHumans;

        private readonly List<StatusEffect> statusEffects;

        public readonly float Force;

        public readonly float Torque;

        public readonly float TargetForce;

        public readonly float SeverLimbsProbability;

        //the indices of the limbs Force is applied on 
        //(if none, force is applied only to the limb the attack is attached to)
        public readonly List<int> ApplyForceOnLimbs;
        
        public readonly float Stun;

        private float priority;

        public float GetDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? damage : damage * deltaTime;
        }

        public float GetBleedingDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? bleedingDamage : bleedingDamage * deltaTime;
        }

        public float GetStructureDamage(float deltaTime)
        {
            return (Duration == 0.0f) ? structureDamage : structureDamage * deltaTime;
        }

        public Attack(float damage, float structureDamage, float bleedingDamage, float range = 0.0f)
        {
            Range = range;
            DamageRange = range;
            this.damage = damage;
            this.structureDamage = structureDamage;
            this.bleedingDamage = bleedingDamage;
        }

        public Attack(XElement element)
        {
            try
            {
                DamageType = (DamageType)Enum.Parse(typeof(DamageType), element.GetAttributeString("damagetype", "None"), true);
            }
            catch
            {
                DamageType = DamageType.None;
            }
            
            damage          = element.GetAttributeFloat("damage", 0.0f);
            structureDamage = element.GetAttributeFloat("structuredamage", 0.0f);
            bleedingDamage  = element.GetAttributeFloat("bleedingdamage", 0.0f);
            Stun            = element.GetAttributeFloat("stun", 0.0f);

            SeverLimbsProbability = element.GetAttributeFloat("severlimbsprobability", 0.0f);

            Force = element.GetAttributeFloat("force", 0.0f);
            TargetForce = element.GetAttributeFloat("targetforce", 0.0f);
            Torque = element.GetAttributeFloat("torque", 0.0f);

            Range = element.GetAttributeFloat("range", 0.0f);
            DamageRange = element.GetAttributeFloat("damagerange", Range);
            Duration = element.GetAttributeFloat("duration", 0.0f); 

            priority = element.GetAttributeFloat("priority", 1.0f);

            onlyHumans = element.GetAttributeBool("onlyhumans", false);

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
            if (onlyHumans)
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

            if (onlyHumans)
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
