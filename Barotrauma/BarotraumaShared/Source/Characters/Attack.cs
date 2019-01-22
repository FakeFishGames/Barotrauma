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
    
    partial class Attack : ISerializableEntity
    {
        public readonly XElement SourceElement;

        [Serialize(AttackContext.NotDefined, true), Editable]
        public AttackContext Context { get; private set; }

        [Serialize(AttackTarget.Any, true), Editable]
        public AttackTarget TargetType { get; private set; }

        [Serialize(HitDetection.Distance, true), Editable]
        public HitDetection HitDetectionType { get; private set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 2000.0f)]
        public float Range { get; private set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 2000.0f)]
        public float DamageRange { get; set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10.0f, DecimalCount = 2)]
        public float Duration { get; private set; }

        [Serialize(5f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2, ToolTip = "How long the AI waits between the attacks.")]
        public float CoolDown { get; private set; } = 5;

        [Serialize(0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f, DecimalCount = 2, ToolTip = "Used as the attack cooldown between different kind of attacks. Does not have effect, if set to 0.")]
        public float SecondaryCoolDown { get; private set; } = 0;

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 10000.0f)]
        public float StructureDamage { get; private set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float ItemDamage { get; private set; }

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

        //force applied to the attacking limb (or limbs defined using ApplyForceOnLimbs)
        //the direction of the force is towards the target that's being attacked
        [Serialize(0.0f, true), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float Force { get; private set; }

        //torque applied to the attacking limb
        [Serialize(0.0f, true), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float Torque { get; private set; }

        [Serialize(false, true), Editable]
        public bool ApplyForcesOnlyOnce { get; private set; }

        //impulse applied to the target the attack hits
        //the direction of the impulse is from this limb towards the target (use negative values to pull the target closer)
        [Serialize(0.0f, true), Editable(MinValueFloat = -1000.0f, MaxValueFloat = 1000.0f)]
        public float TargetImpulse { get; private set; }

        //impulse applied to the target, in world space coordinates (i.e. 0,-1 pushes the target downwards)
        [Serialize("0.0, 0.0", true), Editable]
        public Vector2 TargetImpulseWorld { get; private set; }

        //force applied to the target the attack hits 
        //the direction of the force is from this limb towards the target (use negative values to pull the target closer)
        [Serialize(0.0f, true), Editable(-1000.0f, 1000.0f)]
        public float TargetForce { get; private set; }

        //force applied to the target, in world space coordinates (i.e. 0,-1 pushes the target downwards)
        [Serialize("0.0, 0.0", true), Editable]
        public Vector2 TargetForceWorld { get; private set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float SeverLimbsProbability { get; set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
        public float StickChance { get; set; }

        [Serialize(0.0f, true), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1.0f)]
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

        public readonly List<Affliction> Afflictions = new List<Affliction>();

        /// <summary>
        /// Only affects ai decision making.
        /// </summary>
        public List<PropertyConditional> Conditionals { get; private set; } = new List<PropertyConditional>();

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

        public Attack(XElement element, string parentDebugName)
        {
            SourceElement = element;
            Deserialize();

            if (element.Attribute("damage") != null ||
                element.Attribute("bluntdamage") != null ||
                element.Attribute("burndamage") != null ||
                element.Attribute("bleedingdamage") != null)
            {
                DebugConsole.ThrowError("Error in Attack (" + parentDebugName + ") - Define damage as afflictions instead of using the damage attribute (e.g. <Affliction identifier=\"internaldamage\" strength=\"10\" />).");
            }

            DamageRange = element.GetAttributeFloat("damagerange", 0f);

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
                            }
                        }
                        else
                        {
                            string afflictionIdentifier = subElement.GetAttributeString("identifier", "").ToLowerInvariant();
                            afflictionPrefab = AfflictionPrefab.List.Find(ap => ap.Identifier.ToLowerInvariant() == afflictionIdentifier);
                            if (afflictionPrefab == null)
                            {
                                DebugConsole.ThrowError("Error in Attack (" + parentDebugName + ") - Affliction prefab \"" + afflictionIdentifier + "\" not found.");
                            }
                        }

                        float afflictionStrength = subElement.GetAttributeFloat(1.0f, "amount", "strength");
                        Afflictions.Add(afflictionPrefab.Instantiate(afflictionStrength));
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
        partial void InitProjSpecific(XElement element);

        public void Serialize()
        {
            if (SourceElement == null) { return; }
            SerializableProperty.SerializeProperties(this, SourceElement, true);
        }

        public void Deserialize()
        {
            if (SourceElement == null) { return; }
            SerializableProperties = SerializableProperty.DeserializeProperties(this, SourceElement);
        }
        
        public AttackResult DoDamage(Character attacker, IDamageable target, Vector2 worldPosition, float deltaTime, bool playSound = true)
        {
            Character targetCharacter = target as Character;
            if (OnlyHumans)
            {
                if (targetCharacter != null && targetCharacter.ConfigPath != Character.HumanConfigFile) return new AttackResult();
            }

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
