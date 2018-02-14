using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        class LimbHealth
        {
            public Sprite IndicatorSprite;

            //all values withing the range of 0-100
            public float BluntDamageAmount;
            public float BurnDamageAmount;
            //units per minute
            public float BleedingAmount;

            //how much blunt damage to this limb decreases vitality
            public float BluntDamageVitalityMultiplier = 1.0f;
            public float BurnDamageVitalityMultiplier = 1.0f;
            public float BleedingSpeedMultiplier = 1.0f;

            public LimbHealth() { }

            public LimbHealth(XElement element)
            {
                BluntDamageVitalityMultiplier = element.GetAttributeFloat("bluntdamagevitalitymultiplier", 1.0f);
                BurnDamageVitalityMultiplier = element.GetAttributeFloat("burndamagevitalitymultiplier", 1.0f);
                BleedingSpeedMultiplier = element.GetAttributeFloat("bleedingspeedmultiplier", 1.0f);

                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;
                    IndicatorSprite = new Sprite(subElement);
                }
            }
        }

        private readonly Character character;

        private float vitality, lastSentVitality;
        protected float minVitality, maxVitality;

        //bleeding settings
        public bool DoesBleed { get; private set; }
        public bool UseBloodParticles { get; private set; }
        public float BleedingDecreaseSpeed { get; private set; }

        private List<LimbHealth> limbHealths = new List<LimbHealth>();

        //0-100
        private float bloodlossAmount;

        //0-100
        private float mentalHealth;

        //0-100
        private float oxygenAmount;

        public bool IsUnconscious
        {
            get { return vitality <= 0.0f; }
        }

        public float Vitality
        {
            get { return vitality; }
        }

        public float MaxVitality
        {
            get { return maxVitality; }
        }

        public float OxygenAmount
        {
            get { return oxygenAmount; }
        }
        
        public CharacterHealth(Character character)
        {
            this.character = character;
            vitality = 100.0f;
            maxVitality = 100.0f;
            oxygenAmount = 100.0f;
            mentalHealth = 100.0f;
            limbHealths.Add(new LimbHealth());
        }

        public CharacterHealth(XElement element, Character character)
            : this(character) 
        {
            maxVitality = element.GetAttributeFloat("vitality", 100.0f);
            vitality    = maxVitality;

            DoesBleed               = element.GetAttributeBool("doesbleed", true);
            UseBloodParticles       = element.GetAttributeBool("usebloodparticles", true);
            BleedingDecreaseSpeed   = element.GetAttributeFloat("bleedingdecreasespeed", 0.05f);
            
            minVitality = (character.ConfigPath == Character.HumanConfigFile) ? -100.0f : 0.0f;

            limbHealths.Clear();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "limb") continue;
                limbHealths.Add(new LimbHealth(subElement));
            }
            if (limbHealths.Count == 0)
            {
                limbHealths.Add(new LimbHealth());
            }

            InitProjSpecific(element, character);
        }

        partial void InitProjSpecific(XElement element, Character character);

        public void ApplyDamage(Limb hitLimb, AttackResult attackResult)
        {
            if (hitLimb.HealthIndex < 0 || hitLimb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + hitLimb.type + " is targeting index " + hitLimb.HealthIndex);
                return;
            }

            limbHealths[hitLimb.HealthIndex].BluntDamageAmount  += attackResult.BluntDamage;
            limbHealths[hitLimb.HealthIndex].BleedingAmount     += attackResult.BleedingDamage;
            limbHealths[hitLimb.HealthIndex].BurnDamageAmount   += attackResult.BurnDamage;
        }

        public void ApplyDamage(Limb hitLimb, DamageType damageType, float bluntDamage, float bleedingDamage, float burnDamage, float stun)
        {
            if (hitLimb.HealthIndex < 0 || hitLimb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + hitLimb.type + " is targeting index " + hitLimb.HealthIndex);
                return;
            }
            
            limbHealths[hitLimb.HealthIndex].BluntDamageAmount  += bluntDamage;
            limbHealths[hitLimb.HealthIndex].BleedingAmount     += bleedingDamage;
            limbHealths[hitLimb.HealthIndex].BurnDamageAmount   += burnDamage;
        }

        public void Update(float deltaTime)
        {
            UpdateBleeding(deltaTime);
            UpdateOxygen(deltaTime);

            CalculateVitality();

            if (IsUnconscious) UpdateUnconscious(deltaTime);
        }

        private void UpdateBleeding(float deltaTime)
        {
            if (!DoesBleed) return;

            foreach (LimbHealth limbHealth in limbHealths)
            {
                bloodlossAmount += limbHealth.BleedingAmount * (1.0f / 60.0f) * limbHealth.BleedingSpeedMultiplier * deltaTime;
                limbHealth.BleedingAmount = Math.Max(limbHealth.BleedingAmount - BleedingDecreaseSpeed * deltaTime, 0.0f);
            }         
        }
        
        private void UpdateOxygen(float deltaTime)
        {
            float prevOxygen = oxygenAmount;
            oxygenAmount = MathHelper.Clamp(oxygenAmount + deltaTime * (character.OxygenAvailable < 30.0f ? -5.0f : 10.0f), 0.0f, 100.0f);

            UpdateOxygenProjSpecific(prevOxygen);
        }

        private void UpdateUnconscious(float deltaTime)
        {
            /*Oxygen -= deltaTime * 0.5f; //We're critical - our heart stopped!

            if (health <= 0.0f) //Critical health - use current state for crit time
            {
                AddDamage(bleeding > 0.5f ? CauseOfDeath.Bloodloss : CauseOfDeath.Damage, Math.Max(bleeding, 1.0f) * deltaTime, null);
            }
            else //Keep on bleedin'
            {
                Health -= bleeding * deltaTime;
                Bleeding -= BleedingDecreaseSpeed * deltaTime;
            }*/
        }
        
        partial void UpdateOxygenProjSpecific(float prevOxygen);

        private void CalculateVitality()
        {
            vitality = maxVitality;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                vitality -= limbHealth.BluntDamageAmount * limbHealth.BluntDamageVitalityMultiplier;
                vitality -= limbHealth.BurnDamageAmount * limbHealth.BurnDamageVitalityMultiplier;
            }

            vitality -= bloodlossAmount;
            vitality -= (100.0f - oxygenAmount);
            vitality -= (100.0f - mentalHealth);
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();
    }
}
