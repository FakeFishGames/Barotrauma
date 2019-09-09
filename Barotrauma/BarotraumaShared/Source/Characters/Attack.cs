using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{    
    public enum HitDetection
    {
        Distance,
        Contact
    }

    public enum AttackContext
    {
        NotDefined,
        Water,
        Ground
    }

    public enum AttackTarget
    {
        Any,
        Character,
        Structure   // Including hulls etc. Evaluated as anything but a character.
    }

    public enum AIBehaviorAfterAttack
    {
        FallBack,
        FallBackUntilCanAttack,
        PursueIfCanAttack,
        Pursue
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
                Afflictions.Add(affliction.Prefab.Instantiate(affliction.Strength, affliction.Source));
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

    partial class Attack : ISerializableEntity
    {
        [Serialize(AttackContext.NotDefined, true, description: "Is the attack used only in a specific condition?"), Editable]
        public AttackContext Context { get; private set; }

        [Serialize(AttackTarget.Any, true, description: "Does the attack target only specific targets?"), Editable]
        public AttackTarget TargetType { get; private set; }

        [Serialize(HitDetection.Distance, true, description: "Collision detection is more accurate, but it only affects targets that are in contact with the limb."), Editable]
        public HitDetection HitDetectionType { get; private set; }

        [Serialize(AIBehaviorAfterAttack.FallBack, true, description: "The preferred AI behavior after the attack."), Editable]
        public AIBehaviorAfterAttack AfterAttack { get; set; }

        [Serialize(false, true, description: "Should the AI try to reverse when aiming with this attack?"), Editable]
        public bool Reverse { get; private set; }

        [Serialize(0.0f, true, description: "The min distance from the attack limb to the target before the AI tries to attack."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 2000.0f)]
        public float Range { get; set; }

        [Serialize(0.0f, true, description: "The min distance from the attack limb to the target to do damage. In distance-based hit detection, the hit will be registered as soon as the target is within the damage range, unless the attack duration has expired."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 2000.0f)]
        public float DamageRange { get; set; }

        [Serialize(0.25f, true, description: "An approximation of the attack duration. Effectively defines the time window in which the hit can be registered. If set to too low value, it's possible that the attack won't hit the target in time."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, DecimalCount = 2)]
        public float Duration { get; private set; }

        [Serialize(5f, true, description: "How long the AI waits between the attacks."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2)]
        public float CoolDown { get; set; } = 5;

        [Serialize(0f, true, description: "Used as the attack cooldown between different kind of attacks. Does not have effect, if set to 0."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2)]
        public float SecondaryCoolDown { get; set; } = 0;

        [Serialize(0f, true, description: "A random factor applied to all cooldowns. Example: 0.1 -> adds a random value between -10% and 10% of the cooldown. Min 0 (default), Max 1 (could disable or double the cooldown in extreme cases)."), Editable(MinValueFloat = 0, MaxValueFloat = 1, DecimalCount = 2)]
        public float CoolDownRandomFactor { get; private set; } = 0;

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10000.0f)]
        public float StructureDamage { get; set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float ItemDamage { get; set; }

        /// <summary>
        /// Legacy support. Use Afflictions.
        /// </summary>
        [Serialize(0.0f, false)]
        public float Stun { get; private set; }

        [Serialize(false, true), Editable]
        public bool OnlyHumans { get; private set; }

        [Serialize("", true), Editable]
        public string ApplyForceOnLimbs
        {
            get
            {
                return string.Join(", ", ForceOnLimbIndices);
            }
            set
            {
                ForceOnLimbIndices.Clear();
                if (string.IsNullOrEmpty(value)) { return; }
                foreach (string limbIndexStr in value.Split(','))
                {
                    if (int.TryParse(limbIndexStr.Trim(), out int limbIndex))
                    {
                        ForceOnLimbIndices.Add(limbIndex);
                    }
                }
            }
        }

        [Serialize(0.0f, true, description: "Applied to the attacking limb (or limbs defined using ApplyForceOnLimbs). The direction of the force is towards the target that's being attacked."), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float Force { get; private set; }

        [Serialize(0.0f, true, description: "Applied to the attacking limb."), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float Torque { get; private set; }

        [Serialize(false, true), Editable]
        public bool ApplyForcesOnlyOnce { get; private set; }

        [Serialize(0.0f, true, description: "Applied to the target the attack hits. The direction of the impulse is from this limb towards the target (use negative values to pull the target closer)."), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float TargetImpulse { get; private set; }

        [Serialize("0.0, 0.0", true, description: "Applied to the target, in world space coordinates(i.e. 0, -1 pushes the target downwards)."), Editable]
        public Vector2 TargetImpulseWorld { get; private set; }

        [Serialize(0.0f, true, description: "Applied to the target the attack hits. The direction of the force is from this limb towards the target (use negative values to pull the target closer)."), Editable(-1000.0f, 1000.0f)]
        public float TargetForce { get; private set; }

        [Serialize("0.0, 0.0", true, description: "Applied to the target, in world space coordinates(i.e. 0, -1 pushes the target downwards)."), Editable]
        public Vector2 TargetForceWorld { get; private set; }

        [Serialize(0.0f, true, description: "How likely the attack causes target limbs to be severed when the target is dead."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float SeverLimbsProbability { get; set; }

        // TODO: disabled because not synced
        //[Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        //public float StickChance { get; set; }
        public float StickChance => 0f;

        [Serialize(0.0f, true, description: ""), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float Priority { get; private set; }

        public IEnumerable<StatusEffect> StatusEffects
        {
            get { return statusEffects; }
        }

        public string Name => "Attack";

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        } = new Dictionary<string, SerializableProperty>();

        //the indices of the limbs Force is applied on 
        //(if none, force is applied only to the limb the attack is attached to)
        public readonly List<int> ForceOnLimbIndices = new List<int>();

        public readonly Dictionary<Affliction, XElement> Afflictions = new Dictionary<Affliction, XElement>();

        /// <summary>
        /// Only affects ai decision making. All the conditionals has to be met in order to select the attack. TODO: allow to define conditionals using any (implemented in StatusEffect -> move from there to PropertyConditional?)
        /// </summary>
        public List<PropertyConditional> Conditionals { get; private set; } = new List<PropertyConditional>();

        private readonly List<StatusEffect> statusEffects;

        public void SetUser(Character user)
        {
            if (statusEffects == null) { return; }
            foreach (StatusEffect statusEffect in statusEffects)
            {
                statusEffect.SetUser(user);
            }
        }
        
        public List<Affliction> GetMultipliedAfflictions(float multiplier)
        {
            List<Affliction> multipliedAfflictions = new List<Affliction>();
            foreach (Affliction affliction in Afflictions.Keys)
            {
                multipliedAfflictions.Add(affliction.Prefab.Instantiate(affliction.Strength * multiplier, affliction.Source));
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
            foreach (Affliction affliction in Afflictions.Keys)
            {
                totalDamage += affliction.GetVitalityDecrease(null);
            }
            return totalDamage;
        }

        public Attack(float damage, float bleedingDamage, float burnDamage, float structureDamage, float range = 0.0f)
        {
            if (damage > 0.0f) Afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damage), null);
            if (bleedingDamage > 0.0f) Afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamage), null);
            if (burnDamage > 0.0f) Afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamage), null);

            Range = range;
            DamageRange = range;
            StructureDamage = structureDamage;
        }

        public Attack(XElement element, string parentDebugName)
        {
            Deserialize(element);

            if (element.Attribute("damage") != null ||
                element.Attribute("bluntdamage") != null ||
                element.Attribute("burndamage") != null ||
                element.Attribute("bleedingdamage") != null)
            {
                DebugConsole.ThrowError("Error in Attack (" + parentDebugName + ") - Define damage as afflictions instead of using the damage attribute (e.g. <Affliction identifier=\"internaldamage\" strength=\"10\" />).");
            }

            InitProjSpecific(element);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                        if (statusEffects == null)
                        {
                            statusEffects = new List<StatusEffect>();
                        }
                        statusEffects.Add(StatusEffect.Load(subElement, parentDebugName));
                        break;
                    case "affliction":
                        AfflictionPrefab afflictionPrefab;
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in Attack (" + parentDebugName + ") - define afflictions using identifiers instead of names.");
                            string afflictionName = subElement.GetAttributeString("name", "").ToLowerInvariant();
                            afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Name.ToLowerInvariant() == afflictionName);
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in Attack (" + parentDebugName + ") - Affliction prefab \"" + afflictionName + "\" not found.");
                                continue;
                            }
                        }
                        else
                        {
                            string afflictionIdentifier = subElement.GetAttributeString("identifier", "").ToLowerInvariant();
                            afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Identifier.ToLowerInvariant() == afflictionIdentifier);
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in Attack (" + parentDebugName + ") - Affliction prefab \"" + afflictionIdentifier + "\" not found.");
                                continue;
                            }
                        }

                        //float afflictionStrength = subElement.GetAttributeFloat(1.0f, "amount", "strength");
                        //var affliction = afflictionPrefab.Instantiate(afflictionStrength);
                        //Afflictions.Add(affliction, subElement);

                        break;
                    case "conditional":
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            Conditionals.Add(new PropertyConditional(attribute));
                        }
                        break;
                }
            }
        }
        partial void InitProjSpecific(XElement element = null);

        public void ReloadAfflictions(XElement element)
        {
            Afflictions.Clear();
            foreach (var subElement in element.GetChildElements("affliction"))
            {
                AfflictionPrefab afflictionPrefab;
                Affliction affliction;
                string afflictionIdentifier = subElement.GetAttributeString("identifier", "").ToLowerInvariant();
                afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Identifier.ToLowerInvariant() == afflictionIdentifier);
                if (afflictionPrefab != null)
                {
                    float afflictionStrength = subElement.GetAttributeFloat(1.0f, "amount", "strength");
                    affliction = afflictionPrefab.Instantiate(afflictionStrength);
                }
                else
                {
                    affliction = new Affliction(null, 0);
                }
                affliction.Deserialize(subElement);
                // add the affliction anyway, so that it can be shown in the editor.
                Afflictions.Add(affliction, subElement);
            }
        }

        public void Serialize(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element, true);
            foreach (var affliction in Afflictions)
            {
                if (affliction.Value != null)
                {
                    affliction.Key.Serialize(affliction.Value);
                }
            }
        }

        public void Deserialize(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            ReloadAfflictions(element);
        }
        
        public AttackResult DoDamage(Character attacker, IDamageable target, Vector2 worldPosition, float deltaTime, bool playSound = true)
        {
            Character targetCharacter = target as Character;
            if (OnlyHumans)
            {
                if (targetCharacter != null && !targetCharacter.IsHuman)
                {
                    return new AttackResult();
                }
            }

            SetUser(attacker);

            DamageParticles(deltaTime, worldPosition);
            
            var attackResult = target.AddDamage(attacker, worldPosition, this, deltaTime, playSound);
            var effectType = attackResult.Damage > 0.0f ? ActionType.OnUse : ActionType.OnFailure;
            if (targetCharacter != null && targetCharacter.IsDead)
            {
                effectType = ActionType.OnEating;
            }
            if (statusEffects == null) return attackResult;

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.HasTargetType(StatusEffect.TargetType.This))
                {
                    effect.Apply(effectType, deltaTime, attacker, attacker);
                }
                if (target is Character)
                {
                    if (effect.HasTargetType(StatusEffect.TargetType.Character))
                    {
                        effect.Apply(effectType, deltaTime, (Character)target, (Character)target);
                    }
                    if (effect.HasTargetType(StatusEffect.TargetType.Limb))
                    {
                        effect.Apply(effectType, deltaTime, (Character)target, attackResult.HitLimb);
                    }                    
                    if (effect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                    {
                        effect.Apply(effectType, deltaTime, (Character)target, ((Character)target).AnimController.Limbs.Cast<ISerializableEntity>().ToList());
                    }
                }
                if (target is Entity entity)
                {
                    if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                        effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                    {
                        var targets = new List<ISerializableEntity>();
                        effect.GetNearbyTargets(worldPosition, targets);
                        effect.Apply(ActionType.OnActive, deltaTime, entity, targets);
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
                if (targetLimb.character != null && !targetLimb.character.IsHuman)
                {
                    return new AttackResult();
                }
            }

            SetUser(attacker);

            DamageParticles(deltaTime, worldPosition);

            var attackResult = targetLimb.character.ApplyAttack(attacker, worldPosition, this, deltaTime, playSound, targetLimb);
            var effectType = attackResult.Damage > 0.0f ? ActionType.OnUse : ActionType.OnFailure;
            if (statusEffects == null) return attackResult;            

            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.HasTargetType(StatusEffect.TargetType.This))
                {
                    effect.Apply(effectType, deltaTime, attacker, attacker);
                }
                if (effect.HasTargetType(StatusEffect.TargetType.Character))
                {
                    effect.Apply(effectType, deltaTime, targetLimb.character, targetLimb.character);
                }
                if (effect.HasTargetType(StatusEffect.TargetType.Limb))
                {
                    effect.Apply(effectType, deltaTime, targetLimb.character, targetLimb);
                }
                if (effect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                {
                    effect.Apply(effectType, deltaTime, targetLimb.character, targetLimb.character.AnimController.Limbs.Cast<ISerializableEntity>().ToList());
                }

            }

            return attackResult;
        }

        public float AttackTimer { get; private set; }
        public float CoolDownTimer { get; set; }
        public float SecondaryCoolDownTimer { get; set; }
        public bool IsRunning { get; private set; }

        public void UpdateCoolDown(float deltaTime)
        {
            CoolDownTimer -= deltaTime;
            SecondaryCoolDownTimer -= deltaTime;
            if (CoolDownTimer < 0) { CoolDownTimer = 0; }
            if (SecondaryCoolDownTimer < 0) { SecondaryCoolDownTimer = 0; }
        }

        public void UpdateAttackTimer(float deltaTime)
        {
            IsRunning = true;
            AttackTimer += deltaTime;
            if (AttackTimer >= Duration)
            {
                ResetAttackTimer();
                SetCoolDown();
            }
        }

        public void ResetAttackTimer()
        {
            AttackTimer = 0;
            IsRunning = false;
        }

        public void SetCoolDown()
        {
            float randomFraction = CoolDown * CoolDownRandomFactor;
            CoolDownTimer = CoolDown + MathHelper.Lerp(-randomFraction, randomFraction, Rand.Value(Rand.RandSync.Server));
            randomFraction = SecondaryCoolDown * CoolDownRandomFactor;
            SecondaryCoolDownTimer = SecondaryCoolDown + MathHelper.Lerp(-randomFraction, randomFraction, Rand.Value(Rand.RandSync.Server));
        }

        public void ResetCoolDown()
        {
            CoolDownTimer = 0;
            SecondaryCoolDownTimer = 0;
        }

        partial void DamageParticles(float deltaTime, Vector2 worldPosition);

        public bool IsValidContext(AttackContext context) => Context == context || Context == AttackContext.NotDefined;

        public bool IsValidTarget(AttackTarget targetType) => TargetType == AttackTarget.Any || TargetType == targetType;

        public bool IsValidTarget(Entity target)
        {
            switch (TargetType)
            {
                case AttackTarget.Character:
                    return target is Character;
                case AttackTarget.Structure:
                    return !(target is Character);
                case AttackTarget.Any:
                default:
                    return true;
            }
        }
    }
}
