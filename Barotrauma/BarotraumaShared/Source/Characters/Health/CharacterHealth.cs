using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        class LimbHealth
        {
            public Sprite IndicatorSprite;

            //all values withing the range of 0-100
            /*public float DamageAmount;
            public float BurnDamageAmount;
            //units per minute
            public float BleedingAmount;
            */
            public readonly List<Affliction> Afflictions = new List<Affliction>();

            //how much damage to this limb decreases vitality
            public float DamageVitalityMultiplier = 1.0f;
            public float BurnDamageVitalityMultiplier = 1.0f;
            public float BleedingSpeedMultiplier = 1.0f;

            public float TotalDamage
            {
                get { return Afflictions.Sum(a => a.GetVitalityDecrease()); }
            }

            public LimbHealth() { }

            public LimbHealth(XElement element)
            {
                DamageVitalityMultiplier = element.GetAttributeFloat("damagevitalitymultiplier", 1.0f);
                BurnDamageVitalityMultiplier = element.GetAttributeFloat("burndamagevitalitymultiplier", 1.0f);
                BleedingSpeedMultiplier = element.GetAttributeFloat("bleedingspeedmultiplier", 1.0f);

                foreach (XElement subElement in element.Elements())
                {
                    if (subElement.Name.ToString().ToLowerInvariant() != "sprite") continue;
                    IndicatorSprite = new Sprite(subElement);
                }
            }

            public List<Affliction> GetActiveAfflictions(AfflictionPrefab prefab)
            {
                return Afflictions.FindAll(a => a.Prefab == prefab);
            }
            public List<Affliction> GetActiveAfflictions(string afflictionType)
            {
                return Afflictions.FindAll(a => a.Prefab.AfflictionType == afflictionType);
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

        public float MinVitality
        {
            get { return minVitality; }
        }

        public float OxygenAmount
        {
            get { return oxygenAmount; }
            set { oxygenAmount = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public float BloodlossAmount
        {
            get { return bloodlossAmount; }
            set { bloodlossAmount = MathHelper.Clamp(value, 0.0f, 100.0f); }
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
            BleedingDecreaseSpeed   = element.GetAttributeFloat("bleedingdecreasespeed", 0.5f);
            
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

            foreach (Affliction newAffliction in attackResult.Afflictions)
            {
                AddLimbAffliction(hitLimb, newAffliction);
            }
        }

        public void ApplyDamage(Limb hitLimb, DamageType damageType, float damage, float bleedingDamage, float burnDamage, float stun)
        {
            if (hitLimb.HealthIndex < 0 || hitLimb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + hitLimb.type + " is targeting index " + hitLimb.HealthIndex);
                return;
            }
            
            if (damage != 0.0f) AddLimbAffliction(hitLimb, AfflictionPrefab.InternalDamage.Instantiate(damage));            
            if (bleedingDamage != 0.0f) AddLimbAffliction(hitLimb, AfflictionPrefab.Bleeding.Instantiate(bleedingDamage));            
            if (burnDamage != 0.0f) AddLimbAffliction(hitLimb, AfflictionPrefab.Burn.Instantiate(burnDamage));            
        }

        public void SetAllDamage(float damageAmount, float bleedingDamageAmount, float burnDamageAmount)
        {
            foreach (LimbHealth limbHealth in limbHealths)
            {
                limbHealth.Afflictions.RemoveAll(a => 
                    a.Prefab.AfflictionType == AfflictionPrefab.InternalDamage.AfflictionType ||
                    a.Prefab.AfflictionType == AfflictionPrefab.Burn.AfflictionType ||
                    a.Prefab.AfflictionType == AfflictionPrefab.Bleeding.AfflictionType);

                if (damageAmount > 0.0f) limbHealth.Afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damageAmount));
                if (bleedingDamageAmount > 0.0f) limbHealth.Afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamageAmount));
                if (burnDamageAmount > 0.0f) limbHealth.Afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamageAmount));
            }
        }

        private void AddLimbAffliction(Limb limb, Affliction newAffliction)
        {
            foreach (Affliction affliction in limbHealths[limb.HealthIndex].Afflictions)
            {
                if (newAffliction.Prefab.AfflictionType == affliction.Prefab.AfflictionType)
                {
                    affliction.Merge(newAffliction);
                    return;
                }
            }

            limbHealths[limb.HealthIndex].Afflictions.Add(newAffliction);
        }
        
        /*public float GetDamage(Limb limb)
        {
            return limbHealths[limb.HealthIndex].GetActiveAfflictions(AfflictionPrefab.InternalDamage.AfflictionType).Sum(a => a.GetVitalityDecrease());
        }

        public void SetDamage(Limb limb, float damage)
        {
            limbHealths[limb.HealthIndex].DamageAmount = damage;
        }

        public float GetBleedingAmount(Limb limb)
        {
            return limbHealths[limb.HealthIndex].BleedingAmount;
        }

        public void SetBleedingAmount(Limb limb, float bleedingAmount)
        {
            limbHealths[limb.HealthIndex].BleedingAmount = bleedingAmount;
        }

        public float GetBurnDamage(Limb limb)
        {
            return limbHealths[limb.HealthIndex].BurnDamageAmount;
        }

        public void SetBurnDamage(Limb limb, float damage)
        {
            limbHealths[limb.HealthIndex].BurnDamageAmount = damage;
        }*/

        public void Update(float deltaTime)
        {
            //UpdateBleeding(deltaTime);
            UpdateOxygen(deltaTime);

            foreach (LimbHealth limbHealth in limbHealths)
            {
                limbHealth.Afflictions.RemoveAll(a => a.Strength <= 0.0f);
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                limb.BurnOverlayStrength = limbHealths[limb.HealthIndex].Afflictions.Sum(a=> a.Strength / a.Prefab.MaxStrength * a.Prefab.BurnOverlayAlpha);
                limb.DamageOverlayStrength = limb.IsSevered ? 
                    100.0f : 
                    limbHealths[limb.HealthIndex].Afflictions.Sum(a => a.Strength / a.Prefab.MaxStrength * a.Prefab.DamageOverlayAlpha);
            }

            CalculateVitality();

            if (IsUnconscious) UpdateUnconscious(deltaTime);
        }

        /*private void UpdateBleeding(float deltaTime)
        {
            if (!DoesBleed) return;

            foreach (LimbHealth limbHealth in limbHealths)
            {
                bloodlossAmount += limbHealth.BleedingAmount * (1.0f / 60.0f) * limbHealth.BleedingSpeedMultiplier * deltaTime;
                limbHealth.BleedingAmount = Math.Max(limbHealth.BleedingAmount - BleedingDecreaseSpeed * deltaTime, 0.0f);
            }         
        }*/
        
        private void UpdateOxygen(float deltaTime)
        {
            float prevOxygen = oxygenAmount;
            if (IsUnconscious)
            {
                if (character.OxygenAvailable < 30.0f)
                {
                    //the character dies of oxygen deprivation in 100 seconds after losing consciousness
                    oxygenAmount = MathHelper.Clamp(oxygenAmount - 1.0f * deltaTime, -100.0f, 100.0f);
                }
            }
            else
            {
                oxygenAmount = MathHelper.Clamp(oxygenAmount + deltaTime * (character.OxygenAvailable < 30.0f ? -5.0f : 10.0f), 0.0f, 100.0f);
            }

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
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    vitality -= affliction.GetVitalityDecrease();
                }
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
