using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;
using System.Collections.Generic;
#if CLIENT
using Barotrauma.Particles;
#endif

namespace Barotrauma
{
    enum CauseOfDeath
    {
        Damage, Bloodloss, Pressure, Suffocation, Drowning, Burn, Husk, Disconnected
    }

    public enum DamageType { None, Blunt, Slash, Burn }

    struct AttackResult
    {
        public readonly float Damage;
        public readonly float Bleeding;

        public readonly bool HitArmor;

        public AttackResult(float damage, float bleeding, bool hitArmor=false)
        {
            this.Damage = damage;
            this.Bleeding = bleeding;

            this.HitArmor = hitArmor;
        }
    }

    class Attack
    {
        public readonly float Range;
        public readonly float Duration;

        public readonly DamageType DamageType;

        private readonly float structureDamage;
        private readonly float damage;
        private readonly float bleedingDamage;

        private readonly List<StatusEffect> statusEffects;

        public readonly float Force;

        public readonly float Torque;

        public readonly float TargetForce;

#if CLIENT
        private Sound sound;

        private ParticleEmitterPrefab particleEmitterPrefab;
#endif

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

        public Attack(XElement element)
        {
            try
            {
                DamageType = (DamageType)Enum.Parse(typeof(DamageType), ToolBox.GetAttributeString(element, "damagetype", "None"), true);
            }
            catch
            {
                DamageType = DamageType.None;
            }


            damage = ToolBox.GetAttributeFloat(element, "damage", 0.0f);
            structureDamage = ToolBox.GetAttributeFloat(element, "structuredamage", 0.0f);
            bleedingDamage = ToolBox.GetAttributeFloat(element, "bleedingdamage", 0.0f);

            Force = ToolBox.GetAttributeFloat(element,"force", 0.0f);
            TargetForce = ToolBox.GetAttributeFloat(element, "targetforce", 0.0f);

            Torque = ToolBox.GetAttributeFloat(element, "torque", 0.0f);

            Stun = ToolBox.GetAttributeFloat(element, "stun", 0.0f);

#if CLIENT
            string soundPath = ToolBox.GetAttributeString(element, "sound", "");
            if (!string.IsNullOrWhiteSpace(soundPath))
            {
                sound = Sound.Load(soundPath);
            }
#endif
                      
            Range = ToolBox.GetAttributeFloat(element, "range", 0.0f);

            Duration = ToolBox.GetAttributeFloat(element, "duration", 0.0f); 

            priority = ToolBox.GetAttributeFloat(element, "priority", 1.0f);

            statusEffects = new List<StatusEffect>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
#if CLIENT
                    case "particleemitter":
                        particleEmitterPrefab = new ParticleEmitterPrefab(subElement);
                        break;
#endif
                    case "statuseffect":
                        statusEffects.Add(StatusEffect.Load(subElement));
                        break;
                }

            }
        }


        public AttackResult DoDamage(IDamageable attacker, IDamageable target, Vector2 worldPosition, float deltaTime, bool playSound = true)
        {
            if (particleEmitterPrefab != null)
            {
                particleEmitterPrefab.Emit(worldPosition);
            }

            if (sound != null)
            {
                sound.Play(1.0f, 500.0f, worldPosition);
            }
            
            var attackResult = target.AddDamage(attacker, worldPosition, this, deltaTime, playSound);

            var effectType = attackResult.Damage > 0.0f ? ActionType.OnUse : ActionType.OnFailure;

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.Targets.HasFlag(StatusEffect.TargetType.This) && attacker is Character)
                {
                    effect.Apply(effectType, deltaTime, (Character)attacker, (Character)attacker);
                }
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Character) && target is Character)
                {
                    effect.Apply(effectType, deltaTime, (Character)target, (Character)target);
                }
            }

            return attackResult;
        }
    }
}
