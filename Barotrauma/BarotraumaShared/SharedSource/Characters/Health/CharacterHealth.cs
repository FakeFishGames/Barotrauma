using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        class LimbHealth
        {
            public Sprite IndicatorSprite;

            public Rectangle HighlightArea;

            public readonly string Name;
                        
            public readonly List<Affliction> Afflictions = new List<Affliction>();

            public readonly Dictionary<string, float> VitalityMultipliers = new Dictionary<string, float>();
            public readonly Dictionary<string, float> VitalityTypeMultipliers = new Dictionary<string, float>();

            private readonly CharacterHealth characterHealth;

            public float TotalDamage
            {
                get { return Afflictions.Sum(a => a.GetVitalityDecrease(characterHealth)); }
            }

            public LimbHealth() { }

            public LimbHealth(XElement element, CharacterHealth characterHealth)
            {
                Name = TextManager.Get("HealthLimbName." + element.GetAttributeString("name", ""));
                this.characterHealth = characterHealth;
                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "sprite":
                            IndicatorSprite = new Sprite(subElement);
                            HighlightArea = subElement.GetAttributeRect("highlightarea", new Rectangle(0, 0, (int)IndicatorSprite.size.X, (int)IndicatorSprite.size.Y));
                            break;
                        case "vitalitymultiplier":
                            if (subElement.Attribute("name") != null)
                            {
                                DebugConsole.ThrowError("Error in character health config (" + characterHealth.Character.Name + ") - define vitality multipliers using affliction identifiers or types instead of names.");
                                continue;
                            }

                            string afflictionIdentifier = subElement.GetAttributeString("identifier", "");
                            string afflictionType = subElement.GetAttributeString("type", "");
                            float multiplier = subElement.GetAttributeFloat("multiplier", 1.0f);
                            if (!string.IsNullOrEmpty(afflictionIdentifier))
                            {
                                VitalityMultipliers.Add(afflictionIdentifier.ToLowerInvariant(), multiplier);
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

        public const float InsufficientOxygenThreshold = 30.0f;
        public const float LowOxygenThreshold = 50.0f;
        protected float minVitality;

        protected float maxVitality
        {
            get => Character.Params.Health.Vitality;
            set => Character.Params.Health.Vitality = value;
        }

        public bool Unkillable;

        public bool DoesBleed
        {
            get => Character.Params.Health.DoesBleed;
            private set => Character.Params.Health.DoesBleed = value;
        }

        public bool UseHealthWindow
        {
            get => Character.Params.Health.UseHealthWindow;
            set => Character.Params.Health.UseHealthWindow = value;
        }

        public float CrushDepth
        {
            get => Character.Params.Health.CrushDepth;
            private set => Character.Params.Health.CrushDepth = value;
        }

        private List<LimbHealth> limbHealths = new List<LimbHealth>();
        //non-limb-specific afflictions
        private List<Affliction> afflictions = new List<Affliction>();

        private HashSet<Affliction> irremovableAfflictions = new HashSet<Affliction>();
        private Affliction bloodlossAffliction;
        private Affliction oxygenLowAffliction;
        private Affliction pressureAffliction;
        private Affliction stunAffliction;

        public bool IsUnconscious
        {
            get { return Vitality <= 0.0f; }
        }

        public float PressureKillDelay { get; private set; } = 5.0f;

        public float Vitality { get; private set; }

        public float HealthPercentage => MathUtils.Percentage(Vitality, MaxVitality);

        public float MaxVitality
        {
            get
            {
                if (Character?.Info?.Job?.Prefab != null)
                {
                    return maxVitality + Character.Info.Job.Prefab.VitalityModifier;
                }
                return maxVitality;
            }
        }

        public float MinVitality
        {
            get
            {
                if (Character?.Info?.Job?.Prefab != null)
                {
                    return -MaxVitality;
                }
                return minVitality;
            }
        }

        public float OxygenAmount
        {
            get
            {
                if (!Character.NeedsAir || Unkillable) return 100.0f;
                return -oxygenLowAffliction.Strength + 100;
            }
            set
            {
                if (!Character.NeedsAir || Unkillable) return;
                oxygenLowAffliction.Strength = MathHelper.Clamp(-value + 100, 0.0f, 200.0f);
            }
        }

        public float BloodlossAmount
        {
            get { return bloodlossAffliction.Strength; }
            set { bloodlossAffliction.Strength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public float StunTimer
        {
            get { return stunAffliction.Strength; }
            set { stunAffliction.Strength = MathHelper.Clamp(value, 0.0f, stunAffliction.Prefab.MaxStrength); }
        }

        public Affliction PressureAffliction
        {
            get { return pressureAffliction; }
        }

        public Character Character { get; private set; }

        public CharacterHealth(Character character)
        {
            this.Character = character;
            Vitality = 100.0f;

            DoesBleed = true;
            UseHealthWindow = false;

            InitIrremovableAfflictions();

            limbHealths.Add(new LimbHealth());

            InitProjSpecific(null, character);
        }

        public CharacterHealth(XElement element, Character character)
        {
            this.Character = character;
            InitIrremovableAfflictions();

            Vitality    = maxVitality;

            minVitality = character.IsHuman ? -100.0f : 0.0f;

            limbHealths.Clear();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "limb") continue;
                limbHealths.Add(new LimbHealth(subElement, this));
            }
            if (limbHealths.Count == 0)
            {
                limbHealths.Add(new LimbHealth());
            }

            InitProjSpecific(element, character);
        }

        private void InitIrremovableAfflictions()
        {
            irremovableAfflictions.Add(bloodlossAffliction = new Affliction(AfflictionPrefab.Bloodloss, 0.0f));
            irremovableAfflictions.Add(stunAffliction = new Affliction(AfflictionPrefab.Stun, 0.0f));
            irremovableAfflictions.Add(pressureAffliction = new Affliction(AfflictionPrefab.Pressure, 0.0f));
            irremovableAfflictions.Add(oxygenLowAffliction = new Affliction(AfflictionPrefab.OxygenLow, 0.0f));
            foreach (Affliction affliction in irremovableAfflictions)
            {
                afflictions.Add(affliction);
            }
        }

        partial void InitProjSpecific(XElement element, Character character);

        public IEnumerable<Affliction> GetAllAfflictions(Func<Affliction, bool> limbHealthFilter = null)
        {
            return limbHealthFilter == null
                ? afflictions.Union(limbHealths.SelectMany(lh => lh.Afflictions))
                : afflictions.Where(limbHealthFilter).Union(limbHealths.SelectMany(lh => lh.Afflictions.Where(limbHealthFilter)));
        }

        private LimbHealth GetMatchingLimbHealth(Limb limb) => limbHealths[limb.HealthIndex];
        private LimbHealth GetMatchingLimbHealth(Affliction affliction) => GetMatchingLimbHealth(Character.AnimController.GetLimb(affliction.Prefab.IndicatorLimb));

        /// <summary>
        /// Returns the limb afflictions and non-limbspecific afflictions that are set to be displayed on this limb.
        /// </summary>
        private IEnumerable<Affliction> GetMatchingAfflictions(LimbHealth limb, Func<Affliction, bool> predicate)
            => limb.Afflictions.Where(predicate).Union(afflictions.Where(a => predicate(a) && GetMatchingLimbHealth(a) == limb));

        public IEnumerable<Affliction> GetAfflictionsByType(string afflictionType, bool allowLimbAfflictions = true)
        {
            if (allowLimbAfflictions)
            {
                return GetAllAfflictions(a => a.Prefab.AfflictionType == afflictionType);
            }
            else
            {
                return afflictions.Where(a => a.Prefab.AfflictionType == afflictionType);
            }
        }

        public Affliction GetAffliction(string identifier, bool allowLimbAfflictions = true)
        {
            foreach (Affliction affliction in afflictions)
            {
                if (affliction.Prefab.Identifier == identifier) return affliction;
            }
            if (!allowLimbAfflictions) return null;

            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    if (affliction.Prefab.Identifier == identifier) return affliction;
                }
            }

            return null;
        }

        public T GetAffliction<T>(string identifier, bool allowLimbAfflictions = true) where T : Affliction
        {
            return GetAffliction(identifier, allowLimbAfflictions) as T;
        }

        public IEnumerable<Affliction> GetAfflictionsByType(string afflictionType, Limb limb)
        {
            if (limb.HealthIndex < 0 || limb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + Character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + limb.type + " is targeting index " + limb.HealthIndex);
                return null;
            }
            return limbHealths[limb.HealthIndex].Afflictions.Where(a => a.Prefab.AfflictionType == afflictionType);
        }

        public Affliction GetAffliction(string identifier, Limb limb)
        {
            if (limb.HealthIndex < 0 || limb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + Character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + limb.type + " is targeting index " + limb.HealthIndex);
                return null;
            }
            foreach (Affliction affliction in limbHealths[limb.HealthIndex].Afflictions)
            {
                if (affliction.Prefab.Identifier == identifier) return affliction;
            }
            return null;
        }

        public Limb GetAfflictionLimb(Affliction affliction)
        {
            for (int i = 0; i < limbHealths.Count; i++)
            {
                if (!limbHealths[i].Afflictions.Contains(affliction)) continue;
                return Character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == i);
            }
            return null;
        }

        /// <summary>
        /// Get the total strength of the afflictions of a specific type attached to a specific limb
        /// </summary>
        /// <param name="afflictionType">Type of the affliction</param>
        /// <param name="limb">The limb the affliction is attached to</param>
        /// <param name="requireLimbSpecific">Does the affliction have to be attached to only the specific limb. 
        /// Most monsters for example don't have separate healths for different limbs, essentially meaning that every affliction is applied to every limb.</param>
        public float GetAfflictionStrength(string afflictionType, Limb limb, bool requireLimbSpecific)
        {
            if (requireLimbSpecific && limbHealths.Count == 1) return 0.0f;

            float strength = 0.0f;
            foreach (Affliction affliction in limbHealths[limb.HealthIndex].Afflictions)
            {
                if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                if (affliction.Prefab.AfflictionType == afflictionType) strength += affliction.Strength;
            }
            return strength;
        }

        public float GetAfflictionStrength(string afflictionType, bool allowLimbAfflictions = true)
        {
            float strength = 0.0f;
            foreach (Affliction affliction in afflictions)
            {
                if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                if (affliction.Prefab.AfflictionType == afflictionType) strength += affliction.Strength;
            }
            if (!allowLimbAfflictions) return strength;

            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    if (affliction.Strength < affliction.Prefab.ActivationThreshold) continue;
                    if (affliction.Prefab.AfflictionType == afflictionType) strength += affliction.Strength;
                }
            }

            return strength;
        }

        public void ApplyAffliction(Limb targetLimb, Affliction affliction)
        {
            if (Unkillable) { return; }
            if (affliction.Prefab.LimbSpecific)
            {
                if (targetLimb == null)
                {
                    //if a limb-specific affliction is applied to no specific limb, apply to all limbs
                    foreach (LimbHealth limbHealth in limbHealths)
                    {
                        AddLimbAffliction(limbHealth, affliction);
                    }
                }
                else
                {
                    AddLimbAffliction(targetLimb, affliction);
                }
            }
            else
            {
                AddAffliction(affliction);
            }
        }

        public float GetResistance(string resistanceId)
        {
            float resistance = 0.0f;
            for (int i = 0; i < afflictions.Count; i++)
            {
                if (!afflictions[i].Prefab.IsBuff) continue;
                float temp = afflictions[i].GetResistance(resistanceId);
                if (temp > resistance) resistance = temp;
            }

            return resistance;
        }

        public void ReduceAffliction(Limb targetLimb, string affliction, float amount)
        {
            affliction = affliction.ToLowerInvariant();

            List<Affliction> matchingAfflictions = new List<Affliction>(afflictions);

            if (targetLimb != null)
            {
                matchingAfflictions.AddRange(limbHealths[targetLimb.HealthIndex].Afflictions);
            }
            else
            {
                foreach (LimbHealth limbHealth in limbHealths)
                {
                    matchingAfflictions.AddRange(limbHealth.Afflictions);
                }
            }
            matchingAfflictions.RemoveAll(a => 
                a.Prefab.Identifier.ToLowerInvariant() != affliction && 
                a.Prefab.AfflictionType.ToLowerInvariant() != affliction);

            if (matchingAfflictions.Count == 0) return;

            float reduceAmount = amount / matchingAfflictions.Count;
            for (int i = matchingAfflictions.Count - 1; i >= 0; i--)
            {
                var matchingAffliction = matchingAfflictions[i];
                if (matchingAffliction.Strength < reduceAmount)
                {
                    float surplus = reduceAmount - matchingAffliction.Strength;
                    amount -= matchingAffliction.Strength;
                    matchingAffliction.Strength = 0.0f;
                    matchingAfflictions.RemoveAt(i);
                    if (i == 0) i = matchingAfflictions.Count;
                    if (i > 0) reduceAmount += surplus / i;
                    SteamAchievementManager.OnAfflictionRemoved(matchingAffliction, Character);
                }
                else
                {
                    matchingAffliction.Strength -= reduceAmount;
                    amount -= reduceAmount;
                }
            }
            CalculateVitality();
        }

        public void ApplyDamage(Limb hitLimb, AttackResult attackResult)
        {
            if (Unkillable) { return; }
            if (hitLimb.HealthIndex < 0 || hitLimb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + Character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + hitLimb.type + " is targeting index " + hitLimb.HealthIndex);
                return;
            }

            foreach (Affliction newAffliction in attackResult.Afflictions)
            {
                if (newAffliction.Prefab.LimbSpecific)
                {
                    AddLimbAffliction(hitLimb, newAffliction);
                }
                else
                {
                    AddAffliction(newAffliction);
                }
            }            
        }
        
        public void SetAllDamage(float damageAmount, float bleedingDamageAmount, float burnDamageAmount)
        {
            if (Unkillable) { return; }
            foreach (LimbHealth limbHealth in limbHealths)
            {
                limbHealth.Afflictions.RemoveAll(a => 
                    a.Prefab.AfflictionType == AfflictionPrefab.InternalDamage.AfflictionType ||
                    a.Prefab.AfflictionType == AfflictionPrefab.Burn.AfflictionType ||
                    a.Prefab.AfflictionType == AfflictionPrefab.Bleeding.AfflictionType);

                if (damageAmount > 0.0f) limbHealth.Afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damageAmount));
                if (bleedingDamageAmount > 0.0f && DoesBleed) limbHealth.Afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamageAmount));
                if (burnDamageAmount > 0.0f) limbHealth.Afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamageAmount));
            }

            CalculateVitality();
            if (Vitality <= MinVitality) { Kill(); }
        }

        public void RemoveAllAfflictions()
        {
            foreach (LimbHealth limbHealth in limbHealths)
            {
                limbHealth.Afflictions.Clear();
            }

            afflictions.RemoveAll(a => !irremovableAfflictions.Contains(a));
            foreach (Affliction affliction in irremovableAfflictions)
            {
                affliction.Strength = 0.0f;
            }
            CalculateVitality();
        }

        private void AddLimbAffliction(Limb limb, Affliction newAffliction)
        {
            if (!newAffliction.Prefab.LimbSpecific || limb == null) return;
            if (limb.HealthIndex < 0 || limb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + Character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + limb.type + " is targeting index " + limb.HealthIndex);
                return;
            }
            AddLimbAffliction(limbHealths[limb.HealthIndex], newAffliction);
        }

        private void AddLimbAffliction(LimbHealth limbHealth, Affliction newAffliction)
        {
            if (!DoesBleed && newAffliction is AfflictionBleeding) return;
            if (!Character.NeedsAir && newAffliction.Prefab == AfflictionPrefab.OxygenLow) return;

            foreach (Affliction affliction in limbHealth.Afflictions)
            {
                if (newAffliction.Prefab == affliction.Prefab)
                {
                    affliction.Strength = Math.Min(affliction.Prefab.MaxStrength, affliction.Strength + (newAffliction.Strength * (100.0f / MaxVitality) * (1f - GetResistance(affliction.Prefab.Identifier))));
                    affliction.Source = newAffliction.Source;
                    CalculateVitality();
                    if (Vitality <= MinVitality) Kill();
                    return;
                }
            }

            //create a new instance of the affliction to make sure we don't use the same instance for multiple characters
            //or modify the affliction instance of an Attack or a StatusEffect
            var copyAffliction = newAffliction.Prefab.Instantiate(
                Math.Min(newAffliction.Prefab.MaxStrength, newAffliction.Strength * (100.0f / MaxVitality) * (1f - GetResistance(newAffliction.Prefab.Identifier))),
                newAffliction.Source);
            limbHealth.Afflictions.Add(copyAffliction);
            
            Character.HealthUpdateInterval = 0.0f;

            CalculateVitality();
            if (Vitality <= MinVitality) Kill();
#if CLIENT
            selectedLimbIndex = -1;
#endif
        }

        private void AddAffliction(Affliction newAffliction)
        {
            if (!DoesBleed && newAffliction is AfflictionBleeding) return;
            if (!Character.NeedsAir && newAffliction.Prefab == AfflictionPrefab.OxygenLow) return;
            if (newAffliction.Prefab.AfflictionType == "huskinfection")
            {
                var huskPrefab = newAffliction.Prefab as AfflictionPrefabHusk;
                if (huskPrefab.TargetSpecies.None(s => s.Equals(Character.SpeciesName, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }
            foreach (Affliction affliction in afflictions)
            {
                if (newAffliction.Prefab == affliction.Prefab)
                {
                    float newStrength = Math.Min(affliction.Prefab.MaxStrength, affliction.Strength + (newAffliction.Strength * (100.0f / MaxVitality) * (1f - GetResistance(affliction.Prefab.Identifier))));
                    if (affliction == stunAffliction) { Character.SetStun(newStrength, true, true); }
                    affliction.Strength = newStrength;
                    affliction.Source = newAffliction.Source;
                    CalculateVitality();
                    if (Vitality <= MinVitality) Kill();
                    return;
                }
            }

            //create a new instance of the affliction to make sure we don't use the same instance for multiple characters
            //or modify the affliction instance of an Attack or a StatusEffect
            afflictions.Add(newAffliction.Prefab.Instantiate(
                Math.Min(newAffliction.Prefab.MaxStrength, newAffliction.Strength * (100.0f / MaxVitality) * (1f - GetResistance(newAffliction.Prefab.Identifier))),
                source: newAffliction.Source));

            Character.HealthUpdateInterval = 0.0f;

            CalculateVitality();
            if (Vitality <= MinVitality) Kill();
        }


        partial void UpdateProjSpecific(float deltaTime);

        partial void UpdateLimbAfflictionOverlays();

        public void Update(float deltaTime)
        {
            UpdateOxygen(deltaTime);

            for (int i = 0; i < limbHealths.Count; i++)
            {
                for (int j = limbHealths[i].Afflictions.Count - 1; j >= 0; j--)
                {
                    if (limbHealths[i].Afflictions[j].Strength <= 0.0f)
                    {
                        SteamAchievementManager.OnAfflictionRemoved(limbHealths[i].Afflictions[j], Character);
                        limbHealths[i].Afflictions.RemoveAt(j);
                    }
                }
                for (int j = limbHealths[i].Afflictions.Count - 1; j >= 0; j--)
                {
                    var affliction = limbHealths[i].Afflictions[j];
                    Limb targetLimb = Character.AnimController.Limbs.FirstOrDefault(l => l.HealthIndex == i);
                    affliction.Update(this, targetLimb, deltaTime);
                    affliction.DamagePerSecondTimer += deltaTime;
                    if (affliction is AfflictionBleeding)
                    {
                        UpdateBleedingProjSpecific((AfflictionBleeding)affliction, targetLimb, deltaTime);
                    }
                    Character.StackSpeedMultiplier(affliction.GetSpeedMultiplier());
                }
            }
            
            for (int i = afflictions.Count - 1; i >= 0; i--)
            {
                var affliction = afflictions[i];
                if (irremovableAfflictions.Contains(affliction)) continue;
                if (affliction.Strength <= 0.0f)
                {
                    SteamAchievementManager.OnAfflictionRemoved(affliction, Character);
                    afflictions.RemoveAt(i);
                }
            }
            for (int i = 0; i < afflictions.Count; i++)
            {
                var affliction = afflictions[i];
                affliction.Update(this, null, deltaTime);
                affliction.DamagePerSecondTimer += deltaTime;
                Character.StackSpeedMultiplier(affliction.GetSpeedMultiplier());
            }
            
            UpdateLimbAfflictionOverlays();

            CalculateVitality();
            if (Vitality <= MinVitality) Kill();
        }

        private void UpdateOxygen(float deltaTime)
        {
            if (!Character.NeedsAir) return;

            float prevOxygen = OxygenAmount;
            if (IsUnconscious)
            {
                //the character dies of oxygen deprivation in 100 seconds after losing consciousness
                OxygenAmount = MathHelper.Clamp(OxygenAmount - 1.0f * deltaTime, -100.0f, 100.0f);                
            }
            else
            {
                OxygenAmount = MathHelper.Clamp(OxygenAmount + deltaTime * (Character.OxygenAvailable < InsufficientOxygenThreshold ? -5.0f : 10.0f), -100.0f, 100.0f);
            }

            UpdateOxygenProjSpecific(prevOxygen);
        }
        
        partial void UpdateOxygenProjSpecific(float prevOxygen);

        partial void UpdateBleedingProjSpecific(AfflictionBleeding affliction, Limb targetLimb, float deltaTime);

        public void SetVitality(float newVitality)
        {
            maxVitality = newVitality;
            CalculateVitality();
        }

        public void CalculateVitality()
        {
            Vitality = MaxVitality;
            if (Unkillable) { return; }

            float damageResistanceMultiplier = 1f - GetResistance("damage");

            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction affliction in limbHealth.Afflictions)
                {
                    float vitalityDecrease = affliction.GetVitalityDecrease(this);
                    if (limbHealth.VitalityMultipliers.ContainsKey(affliction.Prefab.Identifier.ToLowerInvariant()))
                    {
                        vitalityDecrease *= limbHealth.VitalityMultipliers[affliction.Prefab.Identifier.ToLowerInvariant()];
                    }
                    if (limbHealth.VitalityTypeMultipliers.ContainsKey(affliction.Prefab.AfflictionType.ToLowerInvariant()))
                    {
                        vitalityDecrease *= limbHealth.VitalityTypeMultipliers[affliction.Prefab.AfflictionType.ToLowerInvariant()];
                    }
                    vitalityDecrease *= damageResistanceMultiplier;
                    Vitality -= vitalityDecrease;
                    affliction.CalculateDamagePerSecond(vitalityDecrease);
                }
            }

            foreach (Affliction affliction in afflictions)
            {
                float vitalityDecrease = affliction.GetVitalityDecrease(this);
                vitalityDecrease *= damageResistanceMultiplier;
                Vitality -= vitalityDecrease;
                affliction.CalculateDamagePerSecond(vitalityDecrease);
            }
        }

        private void Kill()
        {
            if (Unkillable) { return; }
            
            var causeOfDeath = GetCauseOfDeath();
            Character.Kill(causeOfDeath.First, causeOfDeath.Second);
#if CLIENT
            DisplayVitalityDelay = 0.0f;
            DisplayedVitality = Vitality;
#endif
        }

        public Pair<CauseOfDeathType, Affliction> GetCauseOfDeath()
        {
            List<Affliction> currentAfflictions = GetAllAfflictions(true);

            Affliction strongestAffliction = null;
            float largestStrength = 0.0f;
            foreach (Affliction affliction in currentAfflictions)
            {
                if (strongestAffliction == null || affliction.GetVitalityDecrease(this) > largestStrength)
                {
                    strongestAffliction = affliction;
                    largestStrength = affliction.GetVitalityDecrease(this);
                }
            }

            CauseOfDeathType causeOfDeath = strongestAffliction == null ? CauseOfDeathType.Unknown : CauseOfDeathType.Affliction;
            if (strongestAffliction == oxygenLowAffliction)
            {
                causeOfDeath = Character.AnimController.InWater ? CauseOfDeathType.Drowning : CauseOfDeathType.Suffocation;
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
                        var newAffliction = affliction.Prefab.Instantiate(affliction.Strength);
                        if (affliction.Source != null) { newAffliction.Source = affliction.Source; }
                        newAffliction.DamagePerSecond = affliction.DamagePerSecond;
                        newAffliction.DamagePerSecondTimer = affliction.DamagePerSecondTimer;
                        mergedAfflictions.Add(newAffliction);
                    }
                    else
                    {
                        existingAffliction.DamagePerSecond += affliction.DamagePerSecond;
                        existingAffliction.Strength += affliction.Strength;
                    }
                }

                return mergedAfflictions;
            }

            return allAfflictions;
        }

        /// <summary>
        /// Get the identifiers of the items that can be used to treat the character. Takes into account all the afflictions the character has,
        /// and negative treatment suitabilities (e.g. a medicine that causes oxygen loss may not be suitable if the character is already suffocating)
        /// </summary>
        /// <param name="treatmentSuitability">A dictionary where the key is the identifier of the item and the value the suitability</param>
        /// <param name="normalize">If true, the suitability values are normalized between 0 and 1. If not, they're arbitrary values defined in the medical item XML, where negative values are unsuitable, and positive ones suitable.</param>
        /// <param name="randomization">Amount of randomization to apply to the values (0 = the values are accurate, 1 = the values are completely random)</param>
        
        public void GetSuitableTreatments(Dictionary<string, float> treatmentSuitability, bool normalize, float randomization = 0.0f)
        {
            //key = item identifier
            //float = suitability
            treatmentSuitability.Clear();
            float minSuitability = -10, maxSuitability = 10;
            foreach (Affliction affliction in GetAllAfflictions())
            {
                foreach (KeyValuePair<string, float> treatment in affliction.Prefab.TreatmentSuitability)
                {
                    if (!treatmentSuitability.ContainsKey(treatment.Key))
                    {
                        treatmentSuitability[treatment.Key] = treatment.Value * affliction.Strength;
                    }
                    else
                    {
                        treatmentSuitability[treatment.Key] += treatment.Value * affliction.Strength;
                    }
                    minSuitability = Math.Min(treatmentSuitability[treatment.Key], minSuitability);
                    maxSuitability = Math.Max(treatmentSuitability[treatment.Key], maxSuitability);
                }
            }
            //normalize the suitabilities to a range of 0 to 1
            if (normalize)
            {
                foreach (string treatment in treatmentSuitability.Keys.ToList())
                {
                    treatmentSuitability[treatment] = (treatmentSuitability[treatment] - minSuitability) / (maxSuitability - minSuitability);
                    treatmentSuitability[treatment] = MathHelper.Lerp(treatmentSuitability[treatment], Rand.Range(0.0f, 1.0f), randomization);
                }
            }
            else
            {
                foreach (string treatment in treatmentSuitability.Keys.ToList())
                {
                    treatmentSuitability[treatment] += Rand.Range(-100.0f, 100.0f) * randomization;
                }
            }
        }

        public void ServerWrite(IWriteMessage msg)
        {
            List<Affliction> activeAfflictions = afflictions.FindAll(a => a.Strength > 0.0f && a.Strength >= a.Prefab.ActivationThreshold);

            msg.Write((byte)activeAfflictions.Count);
            foreach (Affliction affliction in activeAfflictions)
            {
                msg.Write(affliction.Prefab.Identifier);
                msg.WriteRangedSingle(
                    MathHelper.Clamp(affliction.Strength, 0.0f, affliction.Prefab.MaxStrength), 
                    0.0f, affliction.Prefab.MaxStrength, 8);
            }

            List<Pair<LimbHealth, Affliction>> limbAfflictions = new List<Pair<LimbHealth, Affliction>>();
            foreach (LimbHealth limbHealth in limbHealths)
            {
                foreach (Affliction limbAffliction in limbHealth.Afflictions)
                {
                    if (limbAffliction.Strength <= 0.0f || limbAffliction.Strength < limbAffliction.Prefab.ActivationThreshold) continue;
                    limbAfflictions.Add(new Pair<LimbHealth, Affliction>(limbHealth, limbAffliction));
                }
            }

            msg.Write((byte)limbAfflictions.Count);
            foreach (var limbAffliction in limbAfflictions)
            {
                msg.WriteRangedInteger(limbHealths.IndexOf(limbAffliction.First), 0, limbHealths.Count - 1);
                msg.Write(limbAffliction.Second.Prefab.Identifier);
                msg.WriteRangedSingle(
                    MathHelper.Clamp(limbAffliction.Second.Strength, 0.0f, limbAffliction.Second.Prefab.MaxStrength), 
                    0.0f, limbAffliction.Second.Prefab.MaxStrength, 8);
            }
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();
    }
}
