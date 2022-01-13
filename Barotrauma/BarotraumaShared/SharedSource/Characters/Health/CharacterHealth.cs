using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Extensions;
using System.Globalization;
using Barotrauma.Abilities;

namespace Barotrauma
{
    partial class CharacterHealth
    {
        class LimbHealth
        {
            public Sprite IndicatorSprite;
            public Sprite HighlightSprite;

            public Rectangle HighlightArea;

            public readonly string Name;
                        
            //public readonly List<Affliction> Afflictions = new List<Affliction>();

            public readonly Dictionary<string, float> VitalityMultipliers = new Dictionary<string, float>();
            public readonly Dictionary<string, float> VitalityTypeMultipliers = new Dictionary<string, float>();

            public LimbHealth() { }

            public LimbHealth(XElement element, CharacterHealth characterHealth)
            {
                string limbName = element.GetAttributeString("name", null) ?? "generic";
                if (limbName != "generic")
                {
                    Name = TextManager.Get("HealthLimbName." + limbName);
                }
                foreach (XElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "sprite":
                            IndicatorSprite = new Sprite(subElement);
                            HighlightArea = subElement.GetAttributeRect("highlightarea", new Rectangle(0, 0, (int)IndicatorSprite.size.X, (int)IndicatorSprite.size.Y));
                            break;
                        case "highlightsprite":
                            HighlightSprite = new Sprite(subElement);
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
        }       

        public const float InsufficientOxygenThreshold = 30.0f;
        public const float LowOxygenThreshold = 50.0f;
        protected float minVitality;

        /// <summary>
        /// Maximum vitality without talent- or job-based modifiers
        /// </summary>
        protected float UnmodifiedMaxVitality
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

        private readonly List<LimbHealth> limbHealths = new List<LimbHealth>();

        private readonly Dictionary<Affliction, LimbHealth> afflictions = new Dictionary<Affliction, LimbHealth>();
        private readonly HashSet<Affliction> irremovableAfflictions = new HashSet<Affliction>();
        private Affliction bloodlossAffliction;
        private Affliction oxygenLowAffliction;
        private Affliction pressureAffliction;
        private Affliction stunAffliction;
        public Affliction BloodlossAffliction { get => bloodlossAffliction; }

        public bool IsUnconscious
        {
            get { return (Vitality <= 0.0f || Character.IsDead) && !Character.HasAbilityFlag(AbilityFlags.AlwaysStayConscious); }
        }

        public float PressureKillDelay { get; private set; } = 5.0f;

        public float Vitality { get; private set; }

        public float HealthPercentage => MathUtils.Percentage(Vitality, MaxVitality);

        public float MaxVitality
        {
            get
            {
                float max = UnmodifiedMaxVitality;
                if (Character?.Info?.Job?.Prefab != null)
                {
                    max += Character.Info.Job.Prefab.VitalityModifier;
                }
                max *= Character.StaticHealthMultiplier;
                max *= 1f + Character.GetStatValue(StatTypes.MaximumHealthMultiplier);
                return max * Character.HealthMultiplier;
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

        public Color DefaultFaceTint = Color.TransparentBlack;

        public Color FaceTint
        {
            get;
            private set;
        }

        public Color BodyTint
        {
            get;
            private set;
        }

        public float OxygenAmount
        {
            get
            {
                if (!Character.NeedsOxygen || Unkillable || Character.GodMode) { return 100.0f; }
                return -oxygenLowAffliction.Strength + 100;
            }
            set
            {
                if (!Character.NeedsOxygen || Unkillable || Character.GodMode) { return; }
                oxygenLowAffliction.Strength = MathHelper.Clamp(-value + 100, 0.0f, 200.0f);
            }
        }

        public float BloodlossAmount
        {
            get { return bloodlossAffliction.Strength; }
            set { bloodlossAffliction.Strength = MathHelper.Clamp(value, 0, bloodlossAffliction.Prefab.MaxStrength); }
        }

        public float Stun
        {
            get { return stunAffliction.Strength; }
            set
            {
                if (Character.GodMode) { return; }
                stunAffliction.Strength = MathHelper.Clamp(value, 0.0f, stunAffliction.Prefab.MaxStrength); 
            }
        }

        public float StunTimer { get; private set; }

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

        public CharacterHealth(XElement element, Character character, XElement limbHealthElement = null)
        {
            this.Character = character;
            InitIrremovableAfflictions();

            Vitality    = UnmodifiedMaxVitality;

            minVitality = character.IsHuman ? -100.0f : 0.0f;

            limbHealths.Clear();
            limbHealthElement ??= element;
            foreach (XElement subElement in limbHealthElement.Elements())
            {
                if (!subElement.Name.ToString().Equals("limb", StringComparison.OrdinalIgnoreCase)) { continue; }
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
                afflictions.Add(affliction, null);
            }
        }

        partial void InitProjSpecific(XElement element, Character character);

        public IReadOnlyCollection<Affliction> GetAllAfflictions()
        {
            return afflictions.Keys;
        }

        public IEnumerable<Affliction> GetAllAfflictions(Func<Affliction, bool> limbHealthFilter)
        {
            return afflictions.Keys.Where(limbHealthFilter);
        }

        private float GetTotalDamage(LimbHealth limbHealth)
        {
            float totalDamage = 0.0f;
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                if (kvp.Value != limbHealth) { continue; }
                var affliction = kvp.Key;
                totalDamage += affliction.GetVitalityDecrease(this);
            }
            return totalDamage;
        }

        private LimbHealth GetMatchingLimbHealth(Limb limb) => limb == null ? null : limbHealths[limb.HealthIndex];
        private LimbHealth GetMatchingLimbHealth(Affliction affliction) => GetMatchingLimbHealth(Character.AnimController.GetLimb(affliction.Prefab.IndicatorLimb, excludeSevered: false));

        public Affliction GetAffliction(string identifier, bool allowLimbAfflictions = true)
            => GetAffliction(a => a.Prefab.Identifier == identifier, allowLimbAfflictions);

        public Affliction GetAfflictionOfType(string afflictionType, bool allowLimbAfflictions = true) 
            => GetAffliction(a => a.Prefab.AfflictionType == afflictionType, allowLimbAfflictions);

        private Affliction GetAffliction(Func<Affliction, bool> predicate, bool allowLimbAfflictions = true)
        {
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                if (!allowLimbAfflictions && kvp.Value != null) { continue; }
                if (predicate(kvp.Key)) { return kvp.Key; }
            }
            return null;
        }

        public T GetAffliction<T>(string identifier, bool allowLimbAfflictions = true) where T : Affliction
        {
            return GetAffliction(identifier, allowLimbAfflictions) as T;
        }

        public Affliction GetAffliction(string identifier, Limb limb)
        {
            if (limb.HealthIndex < 0 || limb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + Character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + limb.type + " is targeting index " + limb.HealthIndex);
                return null;
            }
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                if (limbHealths[limb.HealthIndex] == kvp.Value && kvp.Key.Prefab.Identifier == identifier) { return kvp.Key; }
            }
            return null;
        }

        public Limb GetAfflictionLimb(Affliction affliction)
        {
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                if (kvp.Key == affliction) 
                {
                    int limbHealthIndex = limbHealths.IndexOf(kvp.Value);
                    foreach (Limb limb in Character.AnimController.Limbs)
                    {
                        if (limb.HealthIndex == limbHealthIndex) { return limb; }
                    }
                    return null;
                }
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
            if (requireLimbSpecific && limbHealths.Count == 1) { return 0.0f; }

            float strength = 0.0f;
            LimbHealth limbHealth = limbHealths[limb.HealthIndex];
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                if (kvp.Value == limbHealth)
                {
                    Affliction affliction = kvp.Key;
                    if (affliction.Strength < affliction.Prefab.ActivationThreshold) { continue; }
                    if (affliction.Prefab.AfflictionType == afflictionType)
                    {
                        strength += affliction.Strength;
                    }
                }
            }
            return strength;
        }

        public float GetAfflictionStrength(string afflictionType, bool allowLimbAfflictions = true)
        {
            float strength = 0.0f;
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                if (!allowLimbAfflictions && kvp.Value != null) { continue; }
                var affliction = kvp.Key;
                if (affliction.Strength < affliction.Prefab.ActivationThreshold) { continue; }
                if (affliction.Prefab.AfflictionType == afflictionType)
                {
                    strength += affliction.Strength;
                }
            }
            return strength;
        }

        public void ApplyAffliction(Limb targetLimb, Affliction affliction, bool allowStacking = true)
        {
            if (!affliction.Prefab.IsBuff && Unkillable || Character.GodMode) { return; }
            if (affliction.Prefab.LimbSpecific)
            {
                if (targetLimb == null)
                {
                    //if a limb-specific affliction is applied to no specific limb, apply to all limbs
                    foreach (LimbHealth limbHealth in limbHealths)
                    {
                        AddLimbAffliction(limbHealth, affliction, allowStacking: allowStacking);
                    }
                }
                else
                {
                    AddLimbAffliction(targetLimb, affliction, allowStacking: allowStacking);
                }
            }
            else
            {
                AddAffliction(affliction, allowStacking: allowStacking);
            }
        }

        public float GetResistance(AfflictionPrefab afflictionPrefab)
        {
            float resistance = 0.0f;
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                resistance += affliction.GetResistance(afflictionPrefab);
            }
            return 1 - ((1 - resistance) * Character.GetAbilityResistance(afflictionPrefab));
        }

        public float GetStatValue(StatTypes statType)
        {
            float value = 0f;
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                value += affliction.GetStatValue(statType);
            }
            return value;
        }

        public bool HasFlag(AbilityFlags flagType)
        {
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                if (affliction.HasFlag(flagType)) { return true; }
            }
            return false;
        }

        private readonly List<Affliction> matchingAfflictions = new List<Affliction>();
        public void ReduceAffliction(Limb targetLimb, string afflictionIdentifier, float amount, ActionType? treatmentAction = null)
        {
            matchingAfflictions.Clear();

            if (targetLimb == null)
            {
                matchingAfflictions.AddRange(afflictions.Keys);
            }
            else
            {
                foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
                {
                    var affliction = kvp.Key;
                    if (kvp.Value == null)
                    {
                        matchingAfflictions.Add(affliction);
                    }
                    else if (limbHealths[targetLimb.HealthIndex] == kvp.Value)
                    {
                        matchingAfflictions.Add(affliction);
                    }
                }
            }

            if (!string.IsNullOrEmpty(afflictionIdentifier))
            {
                matchingAfflictions.RemoveAll(a =>
                    !a.Prefab.Identifier.Equals(afflictionIdentifier, StringComparison.OrdinalIgnoreCase) &&
                    !a.Prefab.AfflictionType.Equals(afflictionIdentifier, StringComparison.OrdinalIgnoreCase));
            }

            if (matchingAfflictions.Count == 0) { return; }

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
                    if (treatmentAction != null)
                    {
                        if (treatmentAction.Value == ActionType.OnUse)
                        {
                            matchingAffliction.AppliedAsSuccessfulTreatmentTime = Timing.TotalTime;
                        }
                        else if (treatmentAction.Value == ActionType.OnFailure)
                        {
                            matchingAffliction.AppliedAsFailedTreatmentTime = Timing.TotalTime;
                        }
                    }
                }
            }
            CalculateVitality();
        }

        public void ApplyDamage(Limb hitLimb, AttackResult attackResult, bool allowStacking = true)
        {
            if (Unkillable || Character.GodMode) { return; }
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
                    AddLimbAffliction(hitLimb, newAffliction, allowStacking);
                }
                else
                {
                    AddAffliction(newAffliction, allowStacking);
                }
            }            
        }

        private readonly static List<Affliction> afflictionsToRemove = new List<Affliction>();
        private readonly static List<KeyValuePair<Affliction, LimbHealth>> afflictionsToUpdate = new List<KeyValuePair<Affliction, LimbHealth>>();
        public void SetAllDamage(float damageAmount, float bleedingDamageAmount, float burnDamageAmount)
        {
            if (Unkillable || Character.GodMode) { return; }

            afflictionsToRemove.Clear();
            afflictionsToRemove.AddRange(afflictions.Keys.Where(a =>
                    a.Prefab.AfflictionType == AfflictionPrefab.InternalDamage.AfflictionType ||
                    a.Prefab.AfflictionType == AfflictionPrefab.Burn.AfflictionType ||
                    a.Prefab.AfflictionType == AfflictionPrefab.Bleeding.AfflictionType));
            foreach (var affliction in afflictionsToRemove)
            {
                afflictions.Remove(affliction);
            }

            foreach (LimbHealth limbHealth in limbHealths)
            {
                if (damageAmount > 0.0f) { afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damageAmount), limbHealth); }
                if (bleedingDamageAmount > 0.0f && DoesBleed) { afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamageAmount), limbHealth); }
                if (burnDamageAmount > 0.0f) { afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamageAmount), limbHealth); }
            }

            CalculateVitality();
            if (Vitality <= MinVitality) { Kill(); }
        }

        public float GetLimbDamage(Limb limb, string afflictionType = null)
        {
            float damageStrength;
            if (limb.IsSevered)
            {
                return 1;
            }
            else
            {
                // Instead of using the limbhealth count here, I think it's best to define the max vitality per limb roughly with a constant value.
                // Therefore with e.g. 80 health, the max damage per limb would be 40.
                // Having at least 40 damage on both legs would cause maximum limping.
                float max = MaxVitality / 2;
                if (string.IsNullOrEmpty(afflictionType))
                {
                    float damage = GetAfflictionStrength("damage", limb, true);
                    float bleeding = GetAfflictionStrength("bleeding", limb, true);
                    float burn = GetAfflictionStrength("burn", limb, true);
                    damageStrength = Math.Min(damage + bleeding + burn, max);
                }
                else
                {
                    damageStrength = Math.Min(GetAfflictionStrength("damage", limb, true), max);
                }
                return damageStrength / max;
            }
        }

        public void RemoveAllAfflictions()
        {
            afflictionsToRemove.Clear();
            afflictionsToRemove.AddRange(afflictions.Keys.Where(a => !irremovableAfflictions.Contains(a))); 
            foreach (var affliction in afflictionsToRemove)
            {
                afflictions.Remove(affliction);
            }
            foreach (Affliction affliction in irremovableAfflictions)
            {
                affliction.Strength = 0.0f;
            }
            CalculateVitality();
        }

        public void RemoveNegativeAfflictions()
        {
            afflictionsToRemove.Clear();
            afflictionsToRemove.AddRange(afflictions.Keys.Where(a => 
                !irremovableAfflictions.Contains(a) && 
                !a.Prefab.IsBuff && 
                a.Prefab.AfflictionType != "geneticmaterialbuff" && 
                a.Prefab.AfflictionType != "geneticmaterialdebuff"));
            foreach (var affliction in afflictionsToRemove)
            {
                afflictions.Remove(affliction);
            }
            foreach (Affliction affliction in irremovableAfflictions)
            {
                affliction.Strength = 0.0f;
            }
            CalculateVitality();
        }

        private void AddLimbAffliction(Limb limb, Affliction newAffliction, bool allowStacking = true)
        {
            if (!newAffliction.Prefab.LimbSpecific || limb == null) { return; }
            if (limb.HealthIndex < 0 || limb.HealthIndex >= limbHealths.Count)
            {
                DebugConsole.ThrowError("Limb health index out of bounds. Character\"" + Character.Name +
                    "\" only has health configured for" + limbHealths.Count + " limbs but the limb " + limb.type + " is targeting index " + limb.HealthIndex);
                return;
            }
            AddLimbAffliction(limbHealths[limb.HealthIndex], newAffliction, allowStacking);
        }

        private void AddLimbAffliction(LimbHealth limbHealth, Affliction newAffliction, bool allowStacking = true)
        {
            if (!DoesBleed && newAffliction is AfflictionBleeding) { return; }
            if (!Character.NeedsOxygen && newAffliction.Prefab == AfflictionPrefab.OxygenLow) { return; }
            if (Character.Params.Health.StunImmunity && newAffliction.Prefab.AfflictionType == "stun") { return; }
            if (newAffliction.Prefab is AfflictionPrefabHusk huskPrefab)
            {
                if (huskPrefab.TargetSpecies.None(s => s.Equals(Character.SpeciesName, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }

            Affliction existingAffliction = null;
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                if (kvp.Value == limbHealth && kvp.Key.Prefab == newAffliction.Prefab)
                {
                    existingAffliction = kvp.Key;
                    break;
                }
            }

            if (existingAffliction != null)
            {
                float newStrength = newAffliction.Strength * (100.0f / MaxVitality) * (1f - GetResistance(existingAffliction.Prefab));
                if (allowStacking)
                {
                    // Add the existing strength
                    newStrength += existingAffliction.Strength;
                }
                newStrength = Math.Min(existingAffliction.Prefab.MaxStrength, newStrength);
                if (existingAffliction == stunAffliction) { Character.SetStun(newStrength, true, true); }
                existingAffliction.Strength = newStrength;
                if (newAffliction.Source != null) { existingAffliction.Source = newAffliction.Source; }
                CalculateVitality();
                if (Vitality <= MinVitality)
                {
                    Kill();
                }
                return;
            }            

            //create a new instance of the affliction to make sure we don't use the same instance for multiple characters
            //or modify the affliction instance of an Attack or a StatusEffect
            var copyAffliction = newAffliction.Prefab.Instantiate(
                Math.Min(newAffliction.Prefab.MaxStrength, newAffliction.Strength * (100.0f / MaxVitality) * (1f - GetResistance(newAffliction.Prefab))),
                newAffliction.Source);
            afflictions.Add(copyAffliction, limbHealth);
            
            Character.HealthUpdateInterval = 0.0f;

            CalculateVitality();
            if (Vitality <= MinVitality)
            {
                Kill();
            }
#if CLIENT
            if (OpenHealthWindow != this && limbHealth != null)
            {
                selectedLimbIndex = -1;
            }
#endif
        }

        private void AddAffliction(Affliction newAffliction, bool allowStacking = true)
        {
            AddLimbAffliction(limbHealth: null, newAffliction, allowStacking);
        }

        partial void UpdateLimbAfflictionOverlays();

        public void Update(float deltaTime)
        {
            UpdateOxygen(deltaTime);

            StunTimer = Stun > 0 ? StunTimer + deltaTime : 0;

            if (Character.GodMode) { return; }

            afflictionsToRemove.Clear();
            afflictionsToUpdate.Clear();
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                if (affliction.Strength <= 0.0f)
                {
                    SteamAchievementManager.OnAfflictionRemoved(affliction, Character);
                    if (!irremovableAfflictions.Contains(affliction)) { afflictionsToRemove.Add(affliction); }
                    continue;
                }
                afflictionsToUpdate.Add(kvp);
            }
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictionsToUpdate)
            {
                var affliction = kvp.Key;
                Limb targetLimb = null;
                if (kvp.Value != null)
                {
                    int healthIndex = limbHealths.IndexOf(kvp.Value);
                    targetLimb =
                        Character.AnimController.Limbs.LastOrDefault(l => !l.IsSevered && !l.Hidden && l.HealthIndex == healthIndex) ??
                        Character.AnimController.MainLimb;
                }
                affliction.Update(this, targetLimb, deltaTime);
                affliction.DamagePerSecondTimer += deltaTime;
                if (affliction is AfflictionBleeding bleeding)
                {
                    UpdateBleedingProjSpecific(bleeding, targetLimb, deltaTime);
                }
                Character.StackSpeedMultiplier(affliction.GetSpeedMultiplier());
            }

            foreach (var affliction in afflictionsToRemove)
            {
                afflictions.Remove(affliction);
            }

            Character.StackSpeedMultiplier(1f + Character.GetStatValue(StatTypes.MovementSpeed));

            if (Character.InWater)
            {
                Character.StackSpeedMultiplier(1f + Character.GetStatValue(StatTypes.SwimmingSpeed));
            }
            else
            {
                Character.StackSpeedMultiplier(1f + Character.GetStatValue(StatTypes.WalkingSpeed));
            }

            UpdateLimbAfflictionOverlays();
            UpdateSkinTint();
            CalculateVitality();

            if (Vitality <= MinVitality)
            {
                Kill();
            }
        }

        private void UpdateSkinTint()
        {
            FaceTint = DefaultFaceTint;
            BodyTint = Color.TransparentBlack;

            if (!(Character?.Params?.Health.ApplyAfflictionColors ?? false)) { return; }

            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                Color faceTint = affliction.GetFaceTint();
                if (faceTint.A > FaceTint.A) { FaceTint = faceTint; }
                Color bodyTint = affliction.GetBodyTint();
                if (bodyTint.A > BodyTint.A) { BodyTint = bodyTint; }
            }
        }

        private void UpdateOxygen(float deltaTime)
        {
            if (!Character.NeedsOxygen) { return; }

            float prevOxygen = OxygenAmount;
            if (IsUnconscious)
            {
                //the character dies of oxygen deprivation in 100 seconds after losing consciousness
                OxygenAmount = MathHelper.Clamp(OxygenAmount - 1.0f * deltaTime, -100.0f, 100.0f);                
            }
            else
            {
                float decreaseSpeed = -5.0f;
                float increaseSpeed = 10.0f;
                float oxygenlowResistance = GetResistance(oxygenLowAffliction.Prefab);
                decreaseSpeed *= (1f - oxygenlowResistance);
                increaseSpeed *= (1f + oxygenlowResistance);
                OxygenAmount = MathHelper.Clamp(OxygenAmount + deltaTime * (Character.OxygenAvailable < InsufficientOxygenThreshold ? decreaseSpeed : increaseSpeed), -100.0f, 100.0f);
            }

            UpdateOxygenProjSpecific(prevOxygen, deltaTime);
        }
        
        partial void UpdateOxygenProjSpecific(float prevOxygen, float deltaTime);

        partial void UpdateBleedingProjSpecific(AfflictionBleeding affliction, Limb targetLimb, float deltaTime);

        public void SetVitality(float newVitality)
        {
            UnmodifiedMaxVitality = newVitality;
            CalculateVitality();
        }

        public void CalculateVitality()
        {
            Vitality = MaxVitality;
            if (Unkillable || Character.GodMode) { return; }

            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                var limbHealth = kvp.Value;
                float vitalityDecrease = affliction.GetVitalityDecrease(this);
                if (limbHealth != null)
                {
                    if (limbHealth.VitalityMultipliers.ContainsKey(affliction.Prefab.Identifier))
                    {
                        vitalityDecrease *= limbHealth.VitalityMultipliers[affliction.Prefab.Identifier];
                    }
                    if (limbHealth.VitalityTypeMultipliers.ContainsKey(affliction.Prefab.AfflictionType))
                    {
                        vitalityDecrease *= limbHealth.VitalityTypeMultipliers[affliction.Prefab.AfflictionType];
                    }
                }
                Vitality -= vitalityDecrease;
                affliction.CalculateDamagePerSecond(vitalityDecrease);
            }

#if CLIENT
            if (IsUnconscious)
            {
                HintManager.OnCharacterUnconscious(Character);
            }
#endif
        }

        private void Kill()
        {
            if (Unkillable || Character.GodMode) { return; }
            
            var (type, affliction) = GetCauseOfDeath();
            UpdateSkinTint();
            Character.Kill(type, affliction);
#if CLIENT
            DisplayVitalityDelay = 0.0f;
            DisplayedVitality = Vitality;
#endif
        }

        // We need to use another list of the afflictions when we call the status effects triggered by afflictions,
        // because those status effects may add or remove other afflictions while iterating the collection.
        private readonly List<(Affliction affliction, Limb limb)> afflictionsCopy = new List<(Affliction affliction, Limb limb)>();
        public void ApplyAfflictionStatusEffects(ActionType type)
        {
            afflictionsCopy.Clear();
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                var limbHealth = kvp.Value;
                Limb targetLimb = null;
                if (limbHealth != null)
                {
                    int healthIndex = limbHealths.IndexOf(limbHealth);
                    targetLimb =
                        Character.AnimController.Limbs.LastOrDefault(l => !l.IsSevered && !l.Hidden && l.HealthIndex == healthIndex) ??
                        Character.AnimController.MainLimb;
                }
                afflictionsCopy.Add((affliction, GetAfflictionLimb(affliction)));
            }

            foreach ((Affliction affliction, Limb limb) in afflictionsCopy)
            {
                affliction.ApplyStatusEffects(type, 1.0f, this, targetLimb: limb);
            }
        }

        public (CauseOfDeathType type, Affliction affliction) GetCauseOfDeath()
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

            return (causeOfDeath, strongestAffliction);
        }

        private readonly List<Affliction> allAfflictions = new List<Affliction>();
        private List<Affliction> GetAllAfflictions(bool mergeSameAfflictions)
        {
            allAfflictions.Clear();
            if (!mergeSameAfflictions)
            {
                allAfflictions.AddRange(afflictions.Keys);
            }
            else
            {
                foreach (Affliction affliction in afflictions.Keys)
                {
                    var existingAffliction = allAfflictions.Find(a => a.Prefab == affliction.Prefab);
                    if (existingAffliction == null)
                    {
                        var newAffliction = affliction.Prefab.Instantiate(affliction.Strength);
                        if (affliction.Source != null) { newAffliction.Source = affliction.Source; }
                        newAffliction.DamagePerSecond = affliction.DamagePerSecond;
                        newAffliction.DamagePerSecondTimer = affliction.DamagePerSecondTimer;
                        allAfflictions.Add(newAffliction);
                    }
                    else
                    {
                        existingAffliction.DamagePerSecond += affliction.DamagePerSecond;
                        existingAffliction.Strength += affliction.Strength;
                    }
                }
            }
            return allAfflictions;
        }

        /// <summary>
        /// Get the identifiers of the items that can be used to treat the character. Takes into account all the afflictions the character has,
        /// and negative treatment suitabilities (e.g. a medicine that causes oxygen loss may not be suitable if the character is already suffocating)
        /// </summary>
        /// <param name="treatmentSuitability">A dictionary where the key is the identifier of the item and the value the suitability</param>
        /// <param name="normalize">If true, the suitability values are normalized between 0 and 1. If not, they're arbitrary values defined in the medical item XML, where negative values are unsuitable, and positive ones suitable.</param>   
        /// <param name="predictFutureDuration">If above 0, the method will take into account how much currently active status effects while affect the afflictions in the next x seconds.</param>   
        public void GetSuitableTreatments(Dictionary<string, float> treatmentSuitability, bool normalize, Limb limb = null, bool ignoreHiddenAfflictions = false, float predictFutureDuration = 0.0f)
        {
            //key = item identifier
            //float = suitability
            treatmentSuitability.Clear();
            float minSuitability = -10, maxSuitability = 10;
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                var limbHealth = kvp.Value;
                if (limb != null && affliction.Prefab.IndicatorLimb != limb.type)
                {
                    if (limbHealth == null) { continue; }
                    int healthIndex = limbHealths.IndexOf(limbHealth);
                    if (limb.HealthIndex != healthIndex) { continue; }
                }

                float strength = affliction.Strength;
                if (predictFutureDuration > 0.0f)
                {
                    strength = GetPredictedStrength(affliction, predictFutureDuration, limb);
                }

                if (strength <= affliction.Prefab.TreatmentThreshold) { continue; }
                if (ignoreHiddenAfflictions && strength < affliction.Prefab.ShowIconThreshold) { continue; }

                foreach (KeyValuePair<string, float> treatment in affliction.Prefab.TreatmentSuitability)
                {
                    if (!treatmentSuitability.ContainsKey(treatment.Key))
                    {
                        treatmentSuitability[treatment.Key] = treatment.Value * strength;
                    }
                    else
                    {
                        treatmentSuitability[treatment.Key] += treatment.Value * strength;
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
                }
            }
        }

        public IEnumerable<string> GetActiveAfflictionTags() => GetActiveAfflictionTags(afflictions.Keys);

        private readonly HashSet<string> afflictionTags = new HashSet<string>();
        public IEnumerable<string> GetActiveAfflictionTags(IEnumerable<Affliction> afflictions)
        {
            afflictionTags.Clear();
            foreach (Affliction affliction in afflictions)
            {
                var currentEffect = affliction.GetActiveEffect();
                if (currentEffect != null && !string.IsNullOrEmpty(currentEffect.Tag))
                {
                    afflictionTags.Add(currentEffect.Tag);
                }
            }
            return afflictionTags;
        }

        public float GetPredictedStrength(Affliction affliction, float predictFutureDuration, Limb limb = null)
        {
            float strength = affliction.Strength;
            foreach (var statusEffect in StatusEffect.DurationList)
            {
                if (!statusEffect.Targets.Any(t => t == Character || (limb != null && Character.AnimController.Limbs.Contains(t)))) { continue; }
                float statusEffectDuration = Math.Min(statusEffect.Timer, predictFutureDuration);
                foreach (var statusEffectAffliction in statusEffect.Parent.Afflictions)
                {
                    if (statusEffectAffliction.Prefab == affliction.Prefab)
                    {
                        strength += statusEffectAffliction.Strength * statusEffectDuration;
                    }
                }
                foreach (var statusEffectAffliction in statusEffect.Parent.ReduceAffliction)
                {
                    if (statusEffectAffliction.affliction.Equals(affliction.Identifier, StringComparison.OrdinalIgnoreCase) ||
                        statusEffectAffliction.affliction.Equals(affliction.Prefab.AfflictionType, StringComparison.OrdinalIgnoreCase))
                    {
                        strength -= statusEffectAffliction.amount * statusEffectDuration;
                    }
                }
            }
            return MathHelper.Clamp(strength, 0.0f, affliction.Prefab.MaxStrength);
        }

        private readonly List<Affliction> activeAfflictions = new List<Affliction>();
        private readonly List<(LimbHealth limbHealth, Affliction affliction)> limbAfflictions = new List<(LimbHealth limbHealth, Affliction affliction)>();
        public void ServerWrite(IWriteMessage msg)
        {
            activeAfflictions.Clear();
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                var limbHealth = kvp.Value;
                if (limbHealth != null) { continue; }
                if (affliction.Strength > 0.0f && affliction.Strength >= affliction.Prefab.ActivationThreshold)
                {
                    activeAfflictions.Add(affliction);
                }
            }
            msg.Write((byte)activeAfflictions.Count);
            foreach (Affliction affliction in activeAfflictions)
            {
                msg.Write(affliction.Prefab.UIntIdentifier);
                msg.WriteRangedSingle(
                    MathHelper.Clamp(affliction.Strength, 0.0f, affliction.Prefab.MaxStrength), 
                    0.0f, affliction.Prefab.MaxStrength, 8);
                msg.Write((byte)affliction.Prefab.PeriodicEffects.Count());
                foreach (AfflictionPrefab.PeriodicEffect periodicEffect in affliction.Prefab.PeriodicEffects)
                {
                    msg.WriteRangedSingle(affliction.PeriodicEffectTimers[periodicEffect], periodicEffect.MinInterval, periodicEffect.MaxInterval, 8);
                }
            }

            limbAfflictions.Clear();
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var limbAffliction = kvp.Key;
                var limbHealth = kvp.Value;
                if (limbHealth == null) { continue; }
                if (limbAffliction.Strength <= 0.0f || limbAffliction.Strength < limbAffliction.Prefab.ActivationThreshold) { continue; }
                limbAfflictions.Add((limbHealth, limbAffliction));                
            }

            msg.Write((byte)limbAfflictions.Count);
            foreach (var (limbHealth, affliction) in limbAfflictions)
            {
                msg.WriteRangedInteger(limbHealths.IndexOf(limbHealth), 0, limbHealths.Count - 1);
                msg.Write(affliction.Prefab.UIntIdentifier);
                msg.WriteRangedSingle(
                    MathHelper.Clamp(affliction.Strength, 0.0f, affliction.Prefab.MaxStrength), 
                    0.0f, affliction.Prefab.MaxStrength, 8);
                msg.Write((byte)affliction.Prefab.PeriodicEffects.Count());
                foreach (AfflictionPrefab.PeriodicEffect periodicEffect in affliction.Prefab.PeriodicEffects)
                {
                    msg.WriteRangedSingle(affliction.PeriodicEffectTimers[periodicEffect], periodicEffect.MinInterval, periodicEffect.MaxInterval, 8);
                }
            }
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();

        /// <summary>
        /// Automatically filters out buffs.
        /// </summary>
        public static IEnumerable<Affliction> SortAfflictionsBySeverity(IEnumerable<Affliction> afflictions, bool excludeBuffs = true) =>
            afflictions.Where(a => !excludeBuffs || !a.Prefab.IsBuff).OrderByDescending(a => a.DamagePerSecond).ThenByDescending(a => a.Strength / a.Prefab.MaxStrength);

        public void Save(XElement healthElement)
        {
            foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions)
            {
                var affliction = kvp.Key;
                var limbHealth = kvp.Value;
                if (affliction.Strength <= 0.0f || limbHealth != null) { continue; }
                healthElement.Add(new XElement("Affliction",
                    new XAttribute("identifier", affliction.Identifier),
                    new XAttribute("strength", affliction.Strength.ToString("G", CultureInfo.InvariantCulture))));
            }

            for (int i = 0; i < limbHealths.Count; i++)
            {
                var limbHealthElement = new XElement("LimbHealth", new XAttribute("i", i));
                healthElement.Add(limbHealthElement);
                foreach (KeyValuePair<Affliction, LimbHealth> kvp in afflictions.Where(a => a.Value == limbHealths[i]))
                {
                    var affliction = kvp.Key;
                    var limbHealth = kvp.Value;
                    if (affliction.Strength <= 0.0f) { continue; }
                    limbHealthElement.Add(new XElement("Affliction",
                        new XAttribute("identifier", affliction.Identifier),
                        new XAttribute("strength", affliction.Strength.ToString("G", CultureInfo.InvariantCulture))));
                }
            }
        }

        public void Load(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "affliction":
                        LoadAffliction(subElement);
                        break;
                    case "limbhealth":
                        int limbHealthIndex = subElement.GetAttributeInt("i", -1);
                        if (limbHealthIndex < 0 || limbHealthIndex >= limbHealths.Count)
                        {
                            DebugConsole.ThrowError($"Error while loading character health: limb index \"{limbHealthIndex}\" out of range.");
                            continue;
                        }
                        foreach (XElement afflictionElement in subElement.Elements())
                        {
                            LoadAffliction(afflictionElement, limbHealths[limbHealthIndex]);
                        }
                        break;
                }
            }

            void LoadAffliction(XElement afflictionElement, LimbHealth limbHealth = null)
            {
                string id = afflictionElement.GetAttributeString("identifier", "");
                var afflictionPrefab = AfflictionPrefab.Prefabs.Find(a => a.Identifier == id);
                if (afflictionPrefab == null)
                {
                    DebugConsole.ThrowError($"Error while loading character health: affliction \"{id}\" not found.");
                    return;
                }
                float strength = afflictionElement.GetAttributeFloat("strength", 0.0f);
                var irremovableAffliction = irremovableAfflictions.FirstOrDefault(a => a.Prefab == afflictionPrefab);
                if (irremovableAffliction != null)
                {
                    irremovableAffliction.Strength = strength;
                }
                else
                {
                    afflictions.Add(afflictionPrefab.Instantiate(strength), limbHealth);
                }
            }
        }
    }
}
