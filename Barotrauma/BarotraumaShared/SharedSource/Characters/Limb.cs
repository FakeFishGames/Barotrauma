using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using LimbParams = Barotrauma.RagdollParams.LimbParams;
using JointParams = Barotrauma.RagdollParams.JointParams;
using Barotrauma.Abilities;

namespace Barotrauma
{
    public enum LimbType
    {
        None, LeftHand, RightHand, LeftArm, RightArm, LeftForearm, RightForearm,
        LeftLeg, RightLeg, LeftFoot, RightFoot, Head, Torso, Tail, Legs, RightThigh, LeftThigh, Waist, Jaw
    };

    partial class LimbJoint
    {
        public bool IsSevered;
        public bool CanBeSevered => Params.CanBeSevered;
        public readonly JointParams Params;
        public readonly Ragdoll ragdoll;
        public readonly Limb LimbA, LimbB;

        public float Scale => Params.Scale * ragdoll.RagdollParams.JointScale;

        public readonly RevoluteJoint revoluteJoint;
        public readonly WeldJoint weldJoint;
        public Joint Joint => revoluteJoint ?? weldJoint as Joint;

        public bool Enabled
        {
            get => Joint.Enabled;
            set => Joint.Enabled = value;
        }

        public Body BodyA => Joint.BodyA;

        public Body BodyB => Joint.BodyB;

        public Vector2 WorldAnchorA
        {
            get => Joint.WorldAnchorA;
            set => Joint.WorldAnchorA = value;
        }

        public Vector2 WorldAnchorB
        {
            get => Joint.WorldAnchorB;
            set => Joint.WorldAnchorB = value;
        }

        public Vector2 LocalAnchorA
        {
            get => revoluteJoint != null ? revoluteJoint.LocalAnchorA : weldJoint.LocalAnchorA;
            set
            {
                if (weldJoint != null)
                {
                    weldJoint.LocalAnchorA = value;
                }
                else
                {
                    revoluteJoint.LocalAnchorA = value;
                }
            }
        }

        public Vector2 LocalAnchorB
        {
            get => revoluteJoint != null ? revoluteJoint.LocalAnchorB : weldJoint.LocalAnchorB;
            set
            {
                if (weldJoint != null)
                {
                    weldJoint.LocalAnchorB = value;
                }
                else
                {
                    revoluteJoint.LocalAnchorB = value;
                }
            }
        }

        public bool LimitEnabled
        {
            get => revoluteJoint != null ? revoluteJoint.LimitEnabled : false;
            set
            {
                if (revoluteJoint != null)
                {
                    revoluteJoint.LimitEnabled = value;
                }
            }
        }

        public float LowerLimit
        {
            get => revoluteJoint != null ? revoluteJoint.LowerLimit : 0;
            set
            {
                if (revoluteJoint != null)
                {
                    revoluteJoint.LowerLimit = value;
                }
            }
        }

        public float UpperLimit
        {
            get => revoluteJoint != null ? revoluteJoint.UpperLimit : 0;
            set
            {
                if (revoluteJoint != null)
                {
                    revoluteJoint.UpperLimit = value;
                }
            }
        }

        public float JointAngle => revoluteJoint != null ? revoluteJoint.JointAngle : weldJoint.ReferenceAngle;

        public LimbJoint(Limb limbA, Limb limbB, JointParams jointParams, Ragdoll ragdoll) : this(limbA, limbB, Vector2.One, Vector2.One, jointParams.WeldJoint)
        {
            Params = jointParams;
            this.ragdoll = ragdoll;
            LoadParams();
        }

        public LimbJoint(Limb limbA, Limb limbB, Vector2 anchor1, Vector2 anchor2, bool weld = false)
        {
            if (weld)
            {
                weldJoint = new WeldJoint(limbA.body.FarseerBody, limbB.body.FarseerBody, anchor1, anchor2);
            }
            else
            {
                revoluteJoint = new RevoluteJoint(limbA.body.FarseerBody, limbB.body.FarseerBody, anchor1, anchor2)
                {
                    MotorEnabled = true,
                    MaxMotorTorque = 0.25f
                };
            }
            Joint.CollideConnected = false;
            LimbA = limbA;
            LimbB = limbB;
        }

        public void LoadParams()
        {
            if (revoluteJoint != null)
            {
                revoluteJoint.MaxMotorTorque = Params.Stiffness;
                revoluteJoint.LimitEnabled = Params.LimitEnabled;
            }
            if (float.IsNaN(Params.LowerLimit))
            {
                Params.LowerLimit = 0;
            }
            if (float.IsNaN(Params.UpperLimit))
            {
                Params.UpperLimit = 0;
            }
            if (ragdoll.IsFlipped)
            {
                if (weldJoint != null)
                {
                    weldJoint.LocalAnchorA = ConvertUnits.ToSimUnits(new Vector2(-Params.Limb1Anchor.X, Params.Limb1Anchor.Y) * Scale);
                    weldJoint.LocalAnchorB = ConvertUnits.ToSimUnits(new Vector2(-Params.Limb2Anchor.X, Params.Limb2Anchor.Y) * Scale);
                }
                else
                {
                    revoluteJoint.LocalAnchorA = ConvertUnits.ToSimUnits(new Vector2(-Params.Limb1Anchor.X, Params.Limb1Anchor.Y) * Scale);
                    revoluteJoint.LocalAnchorB = ConvertUnits.ToSimUnits(new Vector2(-Params.Limb2Anchor.X, Params.Limb2Anchor.Y) * Scale);
                    revoluteJoint.UpperLimit = MathHelper.ToRadians(-Params.LowerLimit);
                    revoluteJoint.LowerLimit = MathHelper.ToRadians(-Params.UpperLimit);
                }
            }
            else
            {
                if (weldJoint != null)
                {
                    weldJoint.LocalAnchorA = ConvertUnits.ToSimUnits(Params.Limb1Anchor * Scale);
                    weldJoint.LocalAnchorB = ConvertUnits.ToSimUnits(Params.Limb2Anchor * Scale);
                }
                else
                {
                    revoluteJoint.LocalAnchorA = ConvertUnits.ToSimUnits(Params.Limb1Anchor * Scale);
                    revoluteJoint.LocalAnchorB = ConvertUnits.ToSimUnits(Params.Limb2Anchor * Scale);
                    revoluteJoint.UpperLimit = MathHelper.ToRadians(Params.UpperLimit);
                    revoluteJoint.LowerLimit = MathHelper.ToRadians(Params.LowerLimit);
                }
            }
        }
    }
    
    partial class Limb : ISerializableEntity, ISpatialEntity
    {
        //how long it takes for severed limbs to fade out
        public float SeveredFadeOutTime { get; private set; } = 10;

        public readonly Character character;
        /// <summary>
        /// Note that during the limb initialization, character.AnimController returns null, whereas this field is already assigned.
        /// </summary>
        public readonly Ragdoll ragdoll;
        public readonly LimbParams Params;

        //the physics body of the limb
        public PhysicsBody body;
                        
        public Vector2 StepOffset => ConvertUnits.ToSimUnits(Params.StepOffset) * ragdoll.RagdollParams.JointScale;

        public bool inWater;

        private FixedMouseJoint pullJoint;

        public readonly LimbType type;

        private bool ignoreCollisions;
        public bool IgnoreCollisions
        {
            get { return ignoreCollisions; }
            set
            {
                ignoreCollisions = value;
                if (body != null)
                {
                    if (ignoreCollisions)
                    {
                        body.CollisionCategories = Category.None;
                        body.CollidesWith = Category.None;
                    }
                    else
                    {
                        //limbs don't collide with each other
                        body.CollisionCategories = Physics.CollisionCharacter;
                        body.CollidesWith = Physics.CollisionAll & ~Physics.CollisionCharacter & ~Physics.CollisionItem & ~Physics.CollisionItemBlocking;
                    }
                }
            }
        }
        
        private bool isSevered;
        private float severedFadeOutTimer;

        private Vector2? mouthPos;
        public Vector2 MouthPos
        {
            get
            {
                if (!mouthPos.HasValue)
                {
                    mouthPos = Params.MouthPos;
                }
                return mouthPos.Value;
            }
            set
            {
                mouthPos = value;
            }
        }
        
        public readonly Attack attack;
        public List<DamageModifier> DamageModifiers { get; private set; } = new List<DamageModifier>();

        private Direction dir;

        public int HealthIndex => Params.HealthIndex;
        public float Scale => Params.Scale * Params.Ragdoll.LimbScale;
        public float AttackPriority => Params.AttackPriority;
        public bool DoesFlip
        {
            get
            {
                if (character.AnimController.CurrentAnimationParams is GroundedMovementParams)
                {
                    switch (type)
                    {
                        case LimbType.LeftFoot:
                        case LimbType.LeftLeg:
                        case LimbType.LeftThigh:
                        case LimbType.RightFoot:
                        case LimbType.RightLeg:
                        case LimbType.RightThigh:
                            // Legs always has to flip
                            return true;
                    }
                }
                return Params.Flip;
            }
        }

        public float SteerForce => Params.SteerForce;
        
        public Vector2 DebugTargetPos;
        public Vector2 DebugRefPos;

        public bool IsSevered
        {
            get { return isSevered; }
            set
            {
                if (isSevered == value) { return; }
                if (value == true)
                {
                    // If any of the connected limbs have a longer fade out time, use that
                    var connectedLimbs = GetConnectedLimbs();
                    SeveredFadeOutTime = Math.Max(Params.SeveredFadeOutTime, connectedLimbs.Any() ? connectedLimbs.Max(l => l.SeveredFadeOutTime) : 0);
                }
                isSevered = value;
                if (isSevered)
                {
                    ragdoll.SubtractMass(this);
                    if (type == LimbType.Head)
                    {
                        character.Kill(CauseOfDeathType.Unknown, null);
                    }
                }
                else
                {
                    severedFadeOutTimer = 0.0f;
                }
#if CLIENT
                if (isSevered)
                {
                    damageOverlayStrength = 100.0f;
                }
#endif
            }
        }

        public Submarine Submarine => character?.Submarine;

        public bool Hidden
        {
            get => Params.Hide;
            set => Params.Hide = value;
        }

        public Vector2 WorldPosition
        {
            get { return character?.Submarine == null ? Position : Position + character.Submarine.Position; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(body.SimPosition); }
        }

        public Vector2 SimPosition
        {
            get 
            {
                if (Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    GameAnalyticsManager.AddErrorEventOnce("Limb.LinearVelocity:SimPosition", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
                    return Vector2.Zero;
                }
                return body.SimPosition; 
            }
        }

        public float Rotation
        {
            get
            {
                if (Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    GameAnalyticsManager.AddErrorEventOnce("Limb.LinearVelocity:SimPosition", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
                    return 0.0f;
                }
                return body.Rotation; 
            }
        }

        //where an animcontroller is trying to pull the limb, only used for debug visualization
        public Vector2 AnimTargetPos { get; private set; }

        public float Mass
        {
            get 
            { 
                if (Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    GameAnalyticsManager.AddErrorEventOnce("Limb.Mass:AccessRemoved", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
                    return 1.0f;
                }
                return body.Mass; 
            }
        }

        public bool Disabled { get; set; }
 
        public Vector2 LinearVelocity
        {
            get 
            {
                if (Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    GameAnalyticsManager.AddErrorEventOnce("Limb.LinearVelocity:AccessRemoved", GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed limb.\n" + Environment.StackTrace.CleanupStackTrace());
                    return Vector2.Zero;
                }
                return body.LinearVelocity; 
            }
        }

        public float Dir
        {
            get { return ((dir == Direction.Left) ? -1.0f : 1.0f); }
            set { dir = (value == -1.0f) ? Direction.Left : Direction.Right; }
        }

        public int RefJointIndex => Params.RefJoint;

        private List<WearableSprite> wearingItems;
        public List<WearableSprite> WearingItems
        {
            get { return wearingItems; }
        }

        public List<WearableSprite> OtherWearables { get; private set; } = new List<WearableSprite>();

        public bool PullJointEnabled
        {
            get { return pullJoint.Enabled; }
            set { pullJoint.Enabled = value; }
        }

        public float PullJointMaxForce
        {
            get { return pullJoint.MaxForce; }
            set { pullJoint.MaxForce = value; }
        }

        public Vector2 PullJointWorldAnchorA
        {
            get { return pullJoint.WorldAnchorA; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    string errorMsg = "Attempted to set the anchor A of a limb's pull joint to an invalid value (" + value + ")\n" + Environment.StackTrace.CleanupStackTrace();
                    GameAnalyticsManager.AddErrorEventOnce("Limb.SetPullJointAnchorA:InvalidValue", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                    DebugConsole.ThrowError(errorMsg);
#endif
                    return;
                }

                if (Vector2.DistanceSquared(SimPosition, value) > 50.0f * 50.0f)
                {
                    Vector2 diff = value - SimPosition;
                    string errorMsg = "Attempted to move the anchor A of a limb's pull joint extremely far from the limb (diff: " + diff +
                        ", limb enabled: " + body.Enabled +
                        ", simple physics enabled: " + character.AnimController.SimplePhysicsEnabled + ")\n"
                        + Environment.StackTrace.CleanupStackTrace();
                    GameAnalyticsManager.AddErrorEventOnce("Limb.SetPullJointAnchorA:ExcessiveValue", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                    DebugConsole.ThrowError(errorMsg);
#endif
                    return;
                }
                
                pullJoint.WorldAnchorA = value;
            }
        }
        
        public Vector2 PullJointWorldAnchorB
        {
            get { return pullJoint.WorldAnchorB; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    string errorMsg = "Attempted to set the anchor B of a limb's pull joint to an invalid value (" + value + ")\n" + Environment.StackTrace.CleanupStackTrace();
                    GameAnalyticsManager.AddErrorEventOnce("Limb.SetPullJointAnchorB:InvalidValue", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                    DebugConsole.ThrowError(errorMsg);
#endif
                    return;
                }
                
                if (Vector2.DistanceSquared(pullJoint.WorldAnchorA, value) > 50.0f * 50.0f)
                {
                    Vector2 diff = value - pullJoint.WorldAnchorA;
                    string errorMsg = "Attempted to move the anchor B of a limb's pull joint extremely far from the limb (diff: " + diff +
                        ", limb enabled: " + body.Enabled +
                        ", simple physics enabled: " + character.AnimController.SimplePhysicsEnabled + ")\n"
                        + Environment.StackTrace.CleanupStackTrace();
                    GameAnalyticsManager.AddErrorEventOnce("Limb.SetPullJointAnchorB:ExcessiveValue", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                    DebugConsole.ThrowError(errorMsg);
#endif
                    return;
                }

                pullJoint.WorldAnchorB = value;                
            }
        }

        public Vector2 PullJointLocalAnchorA
        {
            get { return pullJoint.LocalAnchorA; }
        }

        public bool Removed
        {
            get;
            private set;
        }

        public string Name => Params.Name;

        // Exposed for status effects
        public bool IsDead => character.IsDead;

        public bool CanBeSeveredAlive
        {
            get
            {
                if (character.IsHumanoid) { return false; }
                if (this == character.AnimController.MainLimb) { return false; }
                if (character.AnimController.CanWalk)
                {
                    switch (type)
                    {
                        case LimbType.LeftFoot:
                        case LimbType.RightFoot:
                        case LimbType.LeftLeg:
                        case LimbType.RightLeg:
                        case LimbType.LeftThigh:
                        case LimbType.RightThigh:
                        case LimbType.Legs:
                        case LimbType.Waist:
                            return false;
                    }
                }
                return true;
            }
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        private readonly List<StatusEffect> statusEffects = new List<StatusEffect>();

        public Limb(Ragdoll ragdoll, Character character, LimbParams limbParams)
        {
            this.ragdoll = ragdoll;
            this.character = character;
            this.Params = limbParams;
            wearingItems = new List<WearableSprite>();            
            dir = Direction.Right;
            body = new PhysicsBody(limbParams);
            type = limbParams.Type;
            if (limbParams.IgnoreCollisions)
            {
                body.CollisionCategories = Category.None;
                body.CollidesWith = Category.None;
                IgnoreCollisions = true;
            }
            else
            {
                //limbs don't collide with each other
                body.CollisionCategories = Physics.CollisionCharacter;
                body.CollidesWith = Physics.CollisionAll & ~Physics.CollisionCharacter & ~Physics.CollisionItem & ~Physics.CollisionItemBlocking;
            }
            body.UserData = this;
            pullJoint = new FixedMouseJoint(body.FarseerBody, ConvertUnits.ToSimUnits(limbParams.PullPos * Scale))
            {
                Enabled = false,
                //MaxForce = ((type == LimbType.LeftHand || type == LimbType.RightHand) ? 400.0f : 150.0f) * body.Mass
                // 150 or even 400 is too low if the joint is used for moving the character position from the mainlimb towards the collider position
                MaxForce = 1000 * Mass
            };

            GameMain.World.Add(pullJoint);

            var element = limbParams.Element;

            body.BodyType = BodyType.Dynamic;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "attack":
                        attack = new Attack(subElement, (character == null ? "null" : character.Name) + ", limb " + type);
                        if (attack.DamageRange <= 0)
                        {
                            switch (body.BodyShape)
                            {
                                case PhysicsBody.Shape.Circle:
                                    attack.DamageRange = body.radius;
                                    break;
                                case PhysicsBody.Shape.Capsule:
                                    attack.DamageRange = body.height / 2 + body.radius;
                                    break;
                                case PhysicsBody.Shape.Rectangle:
                                    attack.DamageRange = new Vector2(body.width / 2.0f, body.height / 2.0f).Length();
                                    break;
                            }
                            attack.DamageRange = ConvertUnits.ToDisplayUnits(attack.DamageRange);
                        }
                        if (character.VariantOf != null && character.Params.VariantFile != null)
                        {
                            var attackElement = character.Params.VariantFile.Root.GetChildElement("attack");
                            if (attackElement != null)
                            {
                                attack.DamageMultiplier = attackElement.GetAttributeFloat("damagemultiplier", 1f);
                            }
                        }
                        break;
                    case "damagemodifier":
                        DamageModifiers.Add(new DamageModifier(subElement, character.Name));
                        break;
                    case "statuseffect":
                        statusEffects.Add(StatusEffect.Load(subElement, Name));
                        break;
                }
            }

            SerializableProperties = SerializableProperty.GetProperties(this);

            InitProjSpecific(element);
        }
        partial void InitProjSpecific(XElement element);

        public void MoveToPos(Vector2 pos, float force, bool pullFromCenter = false)
        {
            Vector2 pullPos = body.SimPosition;
            if (!pullFromCenter)
            {
                pullPos = pullJoint.WorldAnchorA;
            }

            AnimTargetPos = pos;

            body.MoveToPos(pos, force, pullPos);
        }

        public void MirrorPullJoint()
        {
            pullJoint.LocalAnchorA = new Vector2(-pullJoint.LocalAnchorA.X, pullJoint.LocalAnchorA.Y);
        }
        
        public AttackResult AddDamage(Vector2 simPosition, float damage, float bleedingDamage, float burnDamage, bool playSound)
        {
            List<Affliction> afflictions = new List<Affliction>();
            if (damage > 0.0f) afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damage));
            if (bleedingDamage > 0.0f) afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamage));
            if (burnDamage > 0.0f) afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamage));

            return AddDamage(simPosition, afflictions, playSound);
        }

        private readonly List<DamageModifier> appliedDamageModifiers = new List<DamageModifier>();
        private readonly List<DamageModifier> tempModifiers = new List<DamageModifier>();
        private readonly List<Affliction> afflictionsCopy = new List<Affliction>();
        public AttackResult AddDamage(Vector2 simPosition, IEnumerable<Affliction> afflictions, bool playSound, float damageMultiplier = 1, float penetration = 0f, Character attacker = null)
        {
            appliedDamageModifiers.Clear();
            afflictionsCopy.Clear();
            foreach (var affliction in afflictions)
            {
                tempModifiers.Clear();
                var newAffliction = affliction;
                float random = Rand.Value(Rand.RandSync.Unsynced);
                if (random > affliction.Probability) { continue; }
                bool applyAffliction = true;
                foreach (DamageModifier damageModifier in DamageModifiers)
                {
                    if (!damageModifier.MatchesAffliction(affliction)) { continue; }
                    if (random > affliction.Probability * damageModifier.ProbabilityMultiplier)
                    {
                        applyAffliction = false;
                        continue;
                    }
                    if (SectorHit(damageModifier.ArmorSectorInRadians, simPosition))
                    {
                        tempModifiers.Add(damageModifier);
                    }
                }
                foreach (WearableSprite wearable in wearingItems)
                {
                    foreach (DamageModifier damageModifier in wearable.WearableComponent.DamageModifiers)
                    {
                        if (!damageModifier.MatchesAffliction(affliction)) { continue; }
                        if (random > affliction.Probability * damageModifier.ProbabilityMultiplier)
                        {
                            applyAffliction = false;
                            continue;
                        }
                        if (SectorHit(damageModifier.ArmorSectorInRadians, simPosition))
                        {
                            tempModifiers.Add(damageModifier);
                        }
                    }
                }
                float finalDamageModifier = damageMultiplier;
                foreach (DamageModifier damageModifier in tempModifiers)
                {
                    float damageModifierValue = damageModifier.DamageMultiplier;
                    if (damageModifier.DeflectProjectiles && damageModifierValue < 1f)
                    {
                        damageModifierValue = MathHelper.Lerp(damageModifierValue, 1f, penetration);
                    }
                    finalDamageModifier *= damageModifierValue;
                }
                if (!MathUtils.NearlyEqual(finalDamageModifier, 1.0f))
                {
                    newAffliction = affliction.CreateMultiplied(finalDamageModifier);
                }
                else
                {
                    newAffliction.SetStrength(affliction.NonClampedStrength);
                }
                if (attacker != null)
                {
                    var abilityAffliction = new AbilityAffliction(newAffliction);
                    attacker.CheckTalents(AbilityEffectType.OnAddDamageAffliction, abilityAffliction);
                }
                if (applyAffliction)
                {
                    afflictionsCopy.Add(newAffliction);
                }
                appliedDamageModifiers.AddRange(tempModifiers);
            }
            var result = new AttackResult(afflictionsCopy, this, appliedDamageModifiers);
            AddDamageProjSpecific(playSound, result);

            float bleedingDamage = 0;
            if (character.CharacterHealth.DoesBleed)
            {
                foreach (var affliction in result.Afflictions)
                {
                    if (affliction is AfflictionBleeding)
                    {
                        bleedingDamage += affliction.GetVitalityDecrease(character.CharacterHealth);
                    }
                }
                if (bleedingDamage > 0)
                {
                    float bloodDecalSize = MathHelper.Clamp(bleedingDamage / 5, 0.1f, 1.0f);
                    if (character.CurrentHull != null && !string.IsNullOrEmpty(character.BloodDecalName))
                    {
                        character.CurrentHull.AddDecal(character.BloodDecalName, WorldPosition, MathHelper.Clamp(bloodDecalSize, 0.5f, 1.0f), isNetworkEvent: false);
                    }
                }
            }

            return result;
        }

        partial void AddDamageProjSpecific(bool playSound, AttackResult result);

        public bool SectorHit(Vector2 armorSector, Vector2 simPosition)
        {
            if (armorSector == Vector2.Zero) { return false; }
            //sector 360 degrees or more -> always hits
            if (Math.Abs(armorSector.Y - armorSector.X) >= MathHelper.TwoPi) { return true; }
            float rotation = body.TransformedRotation;
            float offset = (MathHelper.PiOver2 - MathUtils.GetMidAngle(armorSector.X, armorSector.Y)) * Dir;
            float hitAngle = VectorExtensions.Angle(VectorExtensions.Forward(rotation + offset), SimPosition - simPosition);
            float sectorSize = GetArmorSectorSize(armorSector);
            return hitAngle < sectorSize / 2;
        }

        protected float GetArmorSectorSize(Vector2 armorSector)
        {
            return Math.Abs(armorSector.X - armorSector.Y);
        }

        public void Update(float deltaTime)
        {
            UpdateProjSpecific(deltaTime);
            
            if (inWater)
            {
                body.ApplyWaterForces();
            }

            if (isSevered)
            {                
                severedFadeOutTimer += deltaTime;
                if (severedFadeOutTimer >= SeveredFadeOutTime)
                {
                    body.Enabled = false;
                }
                else if (character.CurrentHull == null && Hull.FindHull(WorldPosition) != null)
                {
                    severedFadeOutTimer = SeveredFadeOutTime;
                }
            }
            else if (!IsDead)
            {
                if (Params.BlinkFrequency > 0)
                {
                    if (blinkTimer > -TotalBlinkDurationOut)
                    {
                        blinkTimer -= deltaTime;
                    }
                    else
                    {
                        blinkTimer = Params.BlinkFrequency;
                    }
                }
                if (reEnableTimer > 0)
                {
                    reEnableTimer -= deltaTime;
                }
                else if (reEnableTimer > -1)
                {
                    ReEnable();
                }
            }

            attack?.UpdateCoolDown(deltaTime);
        }

        private float reEnableTimer = -1;
        public void HideAndDisable(float duration = 0)
        {
            Hidden = true;
            Disabled = true;
            IgnoreCollisions = true;
            if (duration > 0)
            {
                reEnableTimer = duration;
            }
        }

        private void ReEnable()
        {
            Hidden = false;
            Disabled = false;
            IgnoreCollisions = false;
            reEnableTimer = -1;
        }

        partial void UpdateProjSpecific(float deltaTime);

        private readonly List<Body> contactBodies = new List<Body>();
        /// <summary>
        /// Returns true if the attack successfully hit something. If the distance is not given, it will be calculated.
        /// </summary>
        public bool UpdateAttack(float deltaTime, Vector2 attackSimPos, IDamageable damageTarget, out AttackResult attackResult, float distance = -1, Limb targetLimb = null)
        {
            attackResult = default;
            Vector2 simPos = ragdoll.SimplePhysicsEnabled ? character.SimPosition : SimPosition;
            float dist = distance > -1 ? distance : ConvertUnits.ToDisplayUnits(Vector2.Distance(simPos, attackSimPos));
            bool wasRunning = attack.IsRunning;
            attack.UpdateAttackTimer(deltaTime, character);
            if (attack.Blink)
            {
                if (attack.ForceOnLimbIndices != null && attack.ForceOnLimbIndices.Any())
                {
                    foreach (int limbIndex in attack.ForceOnLimbIndices)
                    {
                        if (limbIndex < 0 || limbIndex >= character.AnimController.Limbs.Length) { continue; }
                        Limb limb = character.AnimController.Limbs[limbIndex];
                        if (limb.IsSevered) { continue; }
                        limb.Blink();
                    }
                }
                else
                {
                    Blink();
                }
            }

            bool wasHit = false;
            Body structureBody = null;
            if (damageTarget != null)
            {
                switch (attack.HitDetectionType)
                {
                    case HitDetection.Distance:
                        if (dist < attack.DamageRange)
                        {
                            structureBody = Submarine.PickBody(simPos, attackSimPos, collisionCategory: Physics.CollisionWall | Physics.CollisionLevel, allowInsideFixture: true, customPredicate:                             
                            (Fixture f) =>
                            {
                                return f?.Body?.UserData as string != "ruinroom";
                            });
                            if (damageTarget is Item i && i.GetComponent<Items.Components.Door>() != null)
                            {
                                // If the attack is aimed to an item and hits an item, it's successful.
                                // Ignore blocking checks on doors, because it causes cases where a Mudraptor cannot hit the hatch, for example.
                                wasHit = true;
                            }
                            else if (damageTarget is Structure wall && structureBody != null && 
                                (structureBody.UserData is Structure || (structureBody.UserData is Submarine sub && sub == wall.Submarine)))
                            {
                                // If the attack is aimed to a structure (wall) and hits a structure or the sub, it's successful
                                wasHit = true;
                            }
                            else
                            {
                                // If there is nothing between, the hit is successful
                                wasHit = structureBody == null;
                            }
                        }
                        break;
                    case HitDetection.Contact:
                        contactBodies.Clear();
                        if (damageTarget is Character targetCharacter)
                        {
                            foreach (Limb limb in targetCharacter.AnimController.Limbs)
                            {
                                if (!limb.IsSevered && limb.body?.FarseerBody != null) contactBodies.Add(limb.body.FarseerBody);
                            }
                        }
                        else if (damageTarget is Structure targetStructure)
                        {
                            if (character.Submarine == null && targetStructure.Submarine != null)
                            {
                                contactBodies.Add(targetStructure.Submarine.PhysicsBody.FarseerBody);
                            }
                            else
                            {
                                contactBodies.AddRange(targetStructure.Bodies);
                            }
                        }
                        else if (damageTarget is Item)
                        {
                            Item targetItem = damageTarget as Item;
                            if (targetItem.body?.FarseerBody != null) contactBodies.Add(targetItem.body.FarseerBody);
                        }
                        ContactEdge contactEdge = body.FarseerBody.ContactList;
                        while (contactEdge != null)
                        {
                            if (contactEdge.Contact != null &&
                                contactEdge.Contact.IsTouching &&
                                contactBodies.Any(b => b == contactEdge.Contact.FixtureA?.Body || b == contactEdge.Contact.FixtureB?.Body))
                            {
                                structureBody = contactBodies.LastOrDefault();
                                wasHit = true;
                                break;
                            }
                            contactEdge = contactEdge.Next;
                        }
                        break;
                }
            }

            if (wasHit)
            {
                wasHit = damageTarget != null;
            }

            if (wasHit || attack.HitDetectionType == HitDetection.None)
            {
                if (character == Character.Controlled || GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                {
                    ExecuteAttack(damageTarget, targetLimb, out attackResult);
                }
#if SERVER
                GameMain.NetworkMember.CreateEntityEvent(character, new object[] 
                { 
                    NetEntityEvent.Type.ExecuteAttack, 
                    this, 
                    (damageTarget as Entity)?.ID ?? Entity.NullEntityID, 
                    damageTarget is Character && targetLimb != null ? Array.IndexOf(((Character)damageTarget).AnimController.Limbs, targetLimb) : 0,
                    attackSimPos.X,
                    attackSimPos.Y
                });   
#endif
            }

            Vector2 diff = attackSimPos - SimPosition;
            bool applyForces = !attack.ApplyForcesOnlyOnce || !wasRunning;

            if (applyForces)
            {
                if (attack.ForceOnLimbIndices != null && attack.ForceOnLimbIndices.Count > 0)
                {
                    foreach (int limbIndex in attack.ForceOnLimbIndices)
                    {
                        if (limbIndex < 0 || limbIndex >= character.AnimController.Limbs.Length) { continue; }
                        Limb limb = character.AnimController.Limbs[limbIndex];
                        if (limb.IsSevered) { continue; }
                        diff = attackSimPos - limb.SimPosition;
                        if (diff == Vector2.Zero) { continue; }
                        limb.body.ApplyTorque(limb.Mass * character.AnimController.Dir * attack.Torque * limb.Params.AttackForceMultiplier);
                        Vector2 forcePos = limb.pullJoint == null ? limb.body.SimPosition : limb.pullJoint.WorldAnchorA;
                        limb.body.ApplyLinearImpulse(limb.Mass * attack.Force * limb.Params.AttackForceMultiplier * Vector2.Normalize(diff), forcePos, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    }
                }
                else if (diff != Vector2.Zero)
                {
                    body.ApplyTorque(Mass * character.AnimController.Dir * attack.Torque * Params.AttackForceMultiplier);
                    Vector2 forcePos = pullJoint == null ? body.SimPosition : pullJoint.WorldAnchorA;
                    body.ApplyLinearImpulse(Mass * attack.Force * Params.AttackForceMultiplier * Vector2.Normalize(diff), forcePos, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                }
            }
            Vector2 forceWorld = attack.CalculateAttackPhase(attack.RootTransitionEasing);
            forceWorld.X *= character.AnimController.Dir;
            character.AnimController.MainLimb.body.ApplyLinearImpulse(character.Mass * forceWorld, character.SimPosition, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            if (!attack.IsRunning)
            {
                // Set the main collider where the body lands after the attack
                character.AnimController.Collider.SetTransform(character.AnimController.MainLimb.body.SimPosition, rotation: character.AnimController.Collider.Rotation);
            }
            return wasHit;
        }

        public void ExecuteAttack(IDamageable damageTarget, Limb targetLimb, out AttackResult attackResult)
        {
            bool playSound = false;
#if CLIENT
            playSound = LastAttackSoundTime < Timing.TotalTime - SoundInterval;
            if (playSound)
            {
                LastAttackSoundTime = SoundInterval;
            }
#endif
            if (damageTarget is Character targetCharacter && targetLimb != null)
            {
                attackResult = attack.DoDamageToLimb(character, targetLimb, WorldPosition, 1.0f, playSound, body);
            }
            else
            {
                if (damageTarget is Item targetItem && !targetItem.Prefab.DamagedByMonsters)
                {
                    attackResult = new AttackResult();
                }
                else
                {
                    attackResult = attack.DoDamage(character, damageTarget, WorldPosition, 1.0f, playSound, body);
                }
            }
            /*if (structureBody != null && attack.StickChance > Rand.Range(0.0f, 1.0f, Rand.RandSync.Server))
            {
                // TODO: use the hit pos?
                var localFront = body.GetLocalFront(Params.GetSpriteOrientation());
                var from = body.FarseerBody.GetWorldPoint(localFront);
                var to = from;
                var drawPos = body.DrawPosition;
                StickTo(structureBody, from, to);
            }*/
            attack.ResetAttackTimer();
            attack.SetCoolDown(applyRandom: !character.IsPlayer);
        }

        private WeldJoint attachJoint;
        private WeldJoint colliderJoint;
        public bool IsStuck => attachJoint != null;

        /// <summary>
        /// Attach the limb to a target with WeldJoints.
        /// Uses sim units.
        /// </summary>
        private void StickTo(Body target, Vector2 from, Vector2 to)
        {
            if (attachJoint != null)
            {
                // Already attached to the target body, no need to do anything
                if (attachJoint.BodyB == target) { return; }
                Release();
            }

            if (!ragdoll.IsStuck)
            {
                PhysicsBody mainLimbBody = ragdoll.MainLimb.body;
                Body colliderBody = ragdoll.Collider.FarseerBody;
                Vector2 mainLimbLocalFront = mainLimbBody.GetLocalFront(ragdoll.MainLimb.Params.GetSpriteOrientation());
                if (Dir < 0)
                {
                    mainLimbLocalFront.X = -mainLimbLocalFront.X;
                }
                Vector2 mainLimbFront = mainLimbBody.FarseerBody.GetWorldPoint(mainLimbLocalFront);
                colliderBody.SetTransform(mainLimbBody.SimPosition, mainLimbBody.Rotation);
                // Attach the collider to the main body so that they don't go out of sync (TODO: why is the collider still rotated 90d off?)
                colliderJoint = new WeldJoint(colliderBody, mainLimbBody.FarseerBody, mainLimbFront, mainLimbFront, true)
                {
                    KinematicBodyB = true,
                    CollideConnected = false
                };
                GameMain.World.Add(colliderJoint);
            }

            attachJoint = new WeldJoint(body.FarseerBody, target, from, to, true)
            {
                FrequencyHz = 1,
                DampingRatio = 0.5f,
                KinematicBodyB = true,
                CollideConnected = false
            };
            GameMain.World.Add(attachJoint);
        }

        public void Release()
        {
            if (!IsStuck) { return; }
            GameMain.World.Remove(attachJoint);
            attachJoint = null;
            if (colliderJoint != null)
            {
                GameMain.World.Remove(colliderJoint);
                colliderJoint = null;
            }
        }

        private readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();
        public void ApplyStatusEffects(ActionType actionType, float deltaTime)
        {
            foreach (StatusEffect statusEffect in statusEffects)
            {
                if (statusEffect.type != actionType) { continue; }
                if (statusEffect.type == ActionType.OnDamaged)
                {
                    if (!statusEffect.HasRequiredAfflictions(character.LastDamage)) { continue; }
                    if (statusEffect.OnlyPlayerTriggered)
                    {
                        if (character.LastAttacker == null || !character.LastAttacker.IsPlayer)
                        {
                            continue;
                        }
                    }
                }
                if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                    statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                {
                    targets.Clear();
                    targets.AddRange(statusEffect.GetNearbyTargets(WorldPosition, targets));
                    statusEffect.Apply(actionType, deltaTime, character, targets);
                }
                else
                {
                    if (statusEffect.HasTargetType(StatusEffect.TargetType.Character))
                    {
                        statusEffect.Apply(actionType, deltaTime, character, character, WorldPosition);
                    }
                    else if (statusEffect.targetLimbs != null)
                    {
                        foreach (var limbType in statusEffect.targetLimbs)
                        {
                            if (statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                            {
                                // Target all matching limbs
                                foreach (var limb in ragdoll.Limbs)
                                {
                                    if (limb.IsSevered) { continue; }
                                    if (limb.type == limbType)
                                    {
                                        statusEffect.Apply(actionType, deltaTime, character, limb);
                                    }
                                }
                            }
                            else if (statusEffect.HasTargetType(StatusEffect.TargetType.Limb))
                            {
                                // Target just the first matching limb
                                Limb limb = ragdoll.GetLimb(limbType);
                                statusEffect.Apply(actionType, deltaTime, character, limb);
                            }
                            else if (statusEffect.HasTargetType(StatusEffect.TargetType.LastLimb))
                            {
                                // Target just the last matching limb
                                Limb limb = ragdoll.Limbs.LastOrDefault(l => l.type == limbType && !l.IsSevered && !l.Hidden);
                                statusEffect.Apply(actionType, deltaTime, character, limb);
                            }
                        }
                    }
                    else
                    {
                        statusEffect.Apply(actionType, deltaTime, character, this, WorldPosition);
                    }
                }
            }
        }

        private float blinkTimer;
        private float blinkPhase;

        private float TotalBlinkDurationOut => Params.BlinkDurationOut + Params.BlinkHoldTime;

        public void Blink()
        {
            blinkTimer = -TotalBlinkDurationOut;
        }

        public void UpdateBlink(float deltaTime, float referenceRotation)
        {
            if (blinkTimer > -TotalBlinkDurationOut)
            {
                blinkPhase -= deltaTime;
                if (blinkPhase > 0)
                {
                    // in
                    float t = ToolBox.GetEasing(Params.BlinkTransitionIn, MathUtils.InverseLerp(1, 0, blinkPhase / Params.BlinkDurationIn));
                    body.SmoothRotate(referenceRotation + MathHelper.ToRadians(Params.BlinkRotationIn) * Dir, Mass * Params.BlinkForce * t, wrapAngle: true);
                }
                else
                {
                    if (Math.Abs(blinkPhase) < Params.BlinkHoldTime)
                    {
                        // hold
                        body.SmoothRotate(referenceRotation + MathHelper.ToRadians(Params.BlinkRotationIn) * Dir, Mass * Params.BlinkForce, wrapAngle: true);
                    }
                    else
                    {
                        // out
                        float t = ToolBox.GetEasing(Params.BlinkTransitionOut, MathUtils.InverseLerp(0, 1, -blinkPhase / TotalBlinkDurationOut));
                        body.SmoothRotate(referenceRotation + MathHelper.ToRadians(Params.BlinkRotationOut) * Dir, Mass * Params.BlinkForce * t, wrapAngle: true);
                    }
                }
            }
            else
            {
                // out
                blinkPhase = Params.BlinkDurationIn;
                body.SmoothRotate(referenceRotation + MathHelper.ToRadians(Params.BlinkRotationOut) * Dir, Mass * Params.BlinkForce, wrapAngle: true);
            }
        }

        public IEnumerable<LimbJoint> GetConnectedJoints() => ragdoll.LimbJoints.Where(j => !j.IsSevered && (j.LimbA == this || j.LimbB == this));

        public IEnumerable<Limb> GetConnectedLimbs()
        {
            var connectedJoints = GetConnectedJoints();
            var connectedLimbs = new HashSet<Limb>();
            foreach (Limb limb in ragdoll.Limbs)
            {
                var otherJoints = limb.GetConnectedJoints();
                foreach (LimbJoint connectedJoint in connectedJoints)
                {
                    if (otherJoints.Contains(connectedJoint))
                    {
                        connectedLimbs.Add(limb);
                    }
                }
            }
            return connectedLimbs;
        }

        public void Remove()
        {
            body?.Remove();
            body = null;
            if (pullJoint != null)
            {
                if (GameMain.World.JointList.Contains(pullJoint))
                {
                    GameMain.World.Remove(pullJoint);
                }
                pullJoint = null;
            }
            Release();
            RemoveProjSpecific();
            Removed = true;
        }

        partial void RemoveProjSpecific();

        public void LoadParams()
        {
            pullJoint.LocalAnchorA = ConvertUnits.ToSimUnits(Params.PullPos * Scale);
            LoadParamsProjSpecific();
        }

        partial void LoadParamsProjSpecific();
    }
}
