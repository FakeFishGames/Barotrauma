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

            public Rectangle HighlightArea;
                        
            public readonly List<Affliction> Afflictions = new List<Affliction>();

            public readonly Dictionary<string, float> VitalityNameMultipliers = new Dictionary<string, float>();
            public readonly Dictionary<string, float> VitalityTypeMultipliers = new Dictionary<string, float>();

            public float TotalDamage
            {
                get { return Afflictions.Sum(a => a.GetVitalityDecrease()); }
            }

            public LimbHealth() { }

            public LimbHealth(XElement element)
            {
                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "sprite":
                            IndicatorSprite = new Sprite(subElement);
                            HighlightArea = subElement.GetAttributeRect("highlightarea", new Rectangle(0, 0, (int)IndicatorSprite.size.X, (int)IndicatorSprite.size.Y));
                            break;
                        case "vitalitymultiplier":
                            string afflictionName = subElement.GetAttributeString("name", "");
                            string afflictionType = subElement.GetAttributeString("type", "");
                            float multiplier = subElement.GetAttributeFloat("multiplier", 1.0f);
                            if (!string.IsNullOrEmpty(afflictionName))
                            {
                                VitalityNameMultipliers.Add(afflictionName.ToLowerInvariant(), multiplier);
                            }
                            else
                            {
                                VitalityTypeMultipliers.Add(afflictionType.ToLowerInvariant(), multiplier);
                            }
                            break;
                    }
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

        private List<LimbHealth> limbHealths = new List<LimbHealth>();
        //non-limb-specific afflictions
        private List<Affliction> afflictions = new List<Affliction>();

        private Affliction bloodlossAffliction;
        private Affliction oxygenLowAffliction;
        
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
            get { return -oxygenLowAffliction.Strength + 100; }
            set { oxygenLowAffliction.Strength = MathHelper.Clamp(-value + 100, 0.0f, 200.0f); }
        }

        public float BloodlossAmount
        {
            get { return bloodlossAffliction.Strength; }
            set { bloodlossAffliction.Strength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public Character Character
        {
            get { return character; }
        }
        
        public CharacterHealth(Character character)
        {
            this.character = character;
            vitality = 100.0f;
            maxVitality = 100.0f;

            afflictions.Add(bloodlossAffliction = new Affliction(AfflictionPrefab.Bloodloss, 0.0f));
            afflictions.Add(oxygenLowAffliction = new Affliction(AfflictionPrefab.OxygenLow, 0.0f));

            limbHealths.Add(new LimbHealth());
        }

        public CharacterHealth(XElement element, Character character)
            : this(character) 
        {
            maxVitality = element.GetAttributeFloat("vitality", 100.0f);
            vitality    = maxVitality;

            DoesBleed               = element.GetAttributeBool("doesbleed", true);
            UseBloodParticles       = element.GetAttributeBool("usebloodparticles", true);
            
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

        public void ApplyAffliction(Limb targetLimb, Affliction affliction)
        {
            if (affliction.Prefab.LimbSpecific)
            {
                if (targetLimb == null) return;
                AddLimbAffliction(targetLimb, affliction);
            }
            else
            {
                AddAffliction(affliction);
            }            
        }

        public void ReduceAffliction(Limb targetLimb, string affliction, float amount)
        {
            affliction = affliction.ToLowerInvariant();

            List<Affliction> matchingAfflictions = (targetLimb == null ? afflictions : limbHealths[targetLimb.HealthIndex].Afflictions)
                .FindAll(a => a.Prefab.Name.ToLowerInvariant() == affliction || a.Prefab.AfflictionType.ToLowerInvariant() == affliction);

            if (matchingAfflictions.Count == 0) return;

            do
            {
                float reduceAmount = amount / matchingAfflictions.Count;
                for (int i = matchingAfflictions.Count - 1; i >= 0; i--)
                {
                    var matchingAffliction = matchingAfflictions[i];
                    if (matchingAffliction.Strength < reduceAmount)
                    {
                        amount -= matchingAffliction.Strength;
                        matchingAffliction.Strength = 0.0f;
                        matchingAfflictions.RemoveAt(i);
                    }
                    else
                    {
                        matchingAffliction.Strength -= reduceAmount;
                        amount -= reduceAmount;
                    }
                }
            } while (matchingAfflictions.Count > 0 && amount > 0.0f);
            
        }

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

        public void ApplyDamage(Limb hitLimb, float damage, float bleedingDamage, float burnDamage, float stun)
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
            if (!newAffliction.Prefab.LimbSpecific) return;

            foreach (Affliction affliction in limbHealths[limb.HealthIndex].Afflictions)
            {
                if (newAffliction.Prefab == affliction.Prefab)
                {
                    affliction.Merge(newAffliction);
                    return;
                }
            }

            //create a new instance of the affliction to make sure we don't use the same instance for multiple characters
            //or modify the affliction instance of an Attack or a StatusEffect
            limbHealths[limb.HealthIndex].Afflictions.Add(newAffliction.Prefab.Instantiate(newAffliction.Strength));
        }

        private void AddAffliction(Affliction newAffliction)
        {
            foreach (Affliction affliction in afflictions)
            {
                if (newAffliction.Prefab == affliction.Prefab)
                {
                    affliction.Merge(newAffliction);
                    return;
                }
            }

            //create a new instance of the affliction to make sure we don't use the same instance for multiple characters
            //or modify the affliction instance of an Attack or a StatusEffect
            afflictions.Add(newAffliction.Prefab.Instantiate(newAffliction.Strength));
        }

        
        public void Update(float deltaTime)
        {
            UpdateOxygen(deltaTime);

            int i = 0;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                limbHealth.Afflictions.RemoveAll(a => a.Strength <= 0.0f);
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    affliction.Update(this, character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == i), deltaTime);
                }
                i++;
            }

            afflictions.RemoveAll(a => a.Strength <= 0.0f && a != bloodlossAffliction && a != oxygenLowAffliction);
            foreach (Affliction affliction in afflictions)
            {
                affliction.Update(this, null, deltaTime);
            }

            foreach (Limb limb in character.AnimController.Limbs)
            {
                limb.BurnOverlayStrength = limbHealths[limb.HealthIndex].Afflictions.Sum(a => a.Strength / a.Prefab.MaxStrength * a.Prefab.BurnOverlayAlpha);
                limb.DamageOverlayStrength = limb.IsSevered ?
                    100.0f :
                    limbHealths[limb.HealthIndex].Afflictions.Sum(a => a.Strength / a.Prefab.MaxStrength * a.Prefab.DamageOverlayAlpha);
            }

            CalculateVitality();
            
            if (vitality <= minVitality) character.Kill(GetCauseOfDeath());            
        }

        private void UpdateOxygen(float deltaTime)
        {
            float prevOxygen = OxygenAmount;
            if (IsUnconscious)
            {
                //the character dies of oxygen deprivation in 100 seconds after losing consciousness
                OxygenAmount = MathHelper.Clamp(OxygenAmount - 1.0f * deltaTime, -100.0f, 100.0f);                
            }
            else
            {
                OxygenAmount = MathHelper.Clamp(OxygenAmount + deltaTime * (character.OxygenAvailable < 30.0f ? -5.0f : 10.0f), 0.0f, 100.0f);
            }

            UpdateOxygenProjSpecific(prevOxygen);
        }
        
        partial void UpdateOxygenProjSpecific(float prevOxygen);

        private void CalculateVitality()
        {
            vitality = maxVitality;
            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    float vitalityDecrease = affliction.GetVitalityDecrease();
                    if (limbHealth.VitalityNameMultipliers.ContainsKey(affliction.Prefab.Name.ToLowerInvariant()))
                    {
                        vitalityDecrease *= limbHealth.VitalityNameMultipliers[affliction.Prefab.Name.ToLowerInvariant()];
                    }
                    if (limbHealth.VitalityTypeMultipliers.ContainsKey(affliction.Prefab.AfflictionType.ToLowerInvariant()))
                    {
                        vitalityDecrease *= limbHealth.VitalityTypeMultipliers[affliction.Prefab.AfflictionType.ToLowerInvariant()];
                    }
                    vitality -= vitalityDecrease;
                }
            }

            foreach (Affliction affliction in afflictions)
            {
                vitality -= affliction.GetVitalityDecrease();
            }            
        }

        public Pair<CauseOfDeathType, Affliction> GetCauseOfDeath()
        {
            List<Affliction> currentAfflictions = GetAllAfflictions(true);

            Affliction strongestAffliction = null;
            float largestStrength = 0.0f;
            foreach (Affliction affliction in currentAfflictions)
            {
                if (strongestAffliction == null || affliction.GetVitalityDecrease() > largestStrength)
                {
                    strongestAffliction = affliction;
                    largestStrength = affliction.GetVitalityDecrease();
                }
            }

            CauseOfDeathType causeOfDeath = strongestAffliction == null ? CauseOfDeathType.Unknown : CauseOfDeathType.Affliction;
            if (strongestAffliction == oxygenLowAffliction)
            {
                causeOfDeath = character.AnimController.InWater ? CauseOfDeathType.Drowning : CauseOfDeathType.Suffocation;
            }

            return new Pair<CauseOfDeathType, Affliction>(causeOfDeath, strongestAffliction);
        }

        private List<Affliction> GetAllAfflictions(bool mergeSameAfflictions)
        {
            List<Affliction> allAfflictions = new List<Affliction>(afflictions);
            foreach (LimbHealth limbHealth in limbHealths)
            {
                allAfflictions.AddRange(limbHealth.Afflictions);
            }

            if (mergeSameAfflictions)
            {
                List<Affliction> mergedAfflictions = new List<Affliction>();
                foreach (Affliction affliction in allAfflictions)
                {
                    var existingAffliction = mergedAfflictions.Find(a => a.Prefab == affliction.Prefab);
                    if (existingAffliction == null)
                    {
                        mergedAfflictions.Add(affliction.Prefab.Instantiate(affliction.Strength));
                    }
                    else
                    {
                        existingAffliction.Merge(affliction);
                    }
                }

                return mergedAfflictions;
            }

            return allAfflictions;
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();
    }
}
