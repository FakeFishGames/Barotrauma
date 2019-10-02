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

namespace Barotrauma
{
    public enum LimbType
    {
        None, LeftHand, RightHand, LeftArm, RightArm, LeftForearm, RightForearm,
        LeftLeg, RightLeg, LeftFoot, RightFoot, Head, Torso, Tail, Legs, RightThigh, LeftThigh, Waist
    };
    
    partial class LimbJoint : RevoluteJoint
    {
        public bool IsSevered;
        public bool CanBeSevered => Params.CanBeSevered;
        public readonly JointParams Params;
        public readonly Ragdoll ragdoll;
        public readonly Limb LimbA, LimbB;

        public LimbJoint(Limb limbA, Limb limbB, JointParams jointParams, Ragdoll ragdoll) : this(limbA, limbB, Vector2.One, Vector2.One)
        {
            Params = jointParams;
            this.ragdoll = ragdoll;
            LoadParams();
        }

        public LimbJoint(Limb limbA, Limb limbB, Vector2 anchor1, Vector2 anchor2)
            : base(limbA.body.FarseerBody, limbB.body.FarseerBody, anchor1, anchor2)
        {
            CollideConnected = false;
            MotorEnabled = true;
            MaxMotorTorque = 0.25f;
            LimbA = limbA;
            LimbB = limbB;
        }

        public void LoadParams()
        {
            MaxMotorTorque = Params.Stiffness;
            LimitEnabled = Params.LimitEnabled;
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
                LocalAnchorA = ConvertUnits.ToSimUnits(new Vector2(-Params.Limb1Anchor.X, Params.Limb1Anchor.Y) * Params.Ragdoll.JointScale);
                LocalAnchorB = ConvertUnits.ToSimUnits(new Vector2(-Params.Limb2Anchor.X, Params.Limb2Anchor.Y) * Params.Ragdoll.JointScale);
                UpperLimit = MathHelper.ToRadians(-Params.LowerLimit);
                LowerLimit = MathHelper.ToRadians(-Params.UpperLimit);
            }
            else
            {
                LocalAnchorA = ConvertUnits.ToSimUnits(Params.Limb1Anchor * Params.Ragdoll.JointScale);
                LocalAnchorB = ConvertUnits.ToSimUnits(Params.Limb2Anchor * Params.Ragdoll.JointScale);
                UpperLimit = MathHelper.ToRadians(Params.UpperLimit);
                LowerLimit = MathHelper.ToRadians(Params.LowerLimit);
            }
        }
    }
    
    partial class Limb : ISerializableEntity, ISpatialEntity
    {
        //how long it takes for severed limbs to fade out
        private const float SeveredFadeOutTime = 10.0f;

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

        private readonly FixedMouseJoint pullJoint;

        public readonly LimbType type;

        public readonly bool ignoreCollisions;
        
        private bool isSevered;
        private float severedFadeOutTimer;
                
        public Vector2? MouthPos;
        
        public readonly Attack attack;
        private List<DamageModifier> damageModifiers;

        private Direction dir;

        public int HealthIndex => Params.HealthIndex;
        public float Scale => Params.Ragdoll.LimbScale;
        public float AttackPriority => Params.AttackPriority;
        public bool DoesFlip => Params.Flip;
        public float SteerForce => Params.SteerForce;
        
        public Vector2 DebugTargetPos;
        public Vector2 DebugRefPos;

        public bool IsSevered
        {
            get { return isSevered; }
            set
            {
                isSevered = value;
                if (!isSevered) severedFadeOutTimer = 0.0f;
#if CLIENT
                if (isSevered) damageOverlayStrength = 100.0f;
#endif
            }
        }

        public Submarine Submarine => character.Submarine;

        public Vector2 WorldPosition
        {
            get { return character.Submarine == null ? Position : Position + character.Submarine.Position; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(body.SimPosition); }
        }

        public Vector2 SimPosition
        {
            get { return body.SimPosition; }
        }

        public float Rotation
        {
            get { return body.Rotation; }
        }

        //where an animcontroller is trying to pull the limb, only used for debug visualization
        public Vector2 AnimTargetPos { get; private set; }

        public float Mass
        {
            get { return body.Mass; }
        }

        public bool Disabled { get; set; }
 
        public Vector2 LinearVelocity
        {
            get { return body.LinearVelocity; }
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
                    string errorMsg = "Attempted to set the anchor A of a limb's pull joint to an invalid value (" + value + ")\n" + Environment.StackTrace;
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
                        + Environment.StackTrace;
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
                    string errorMsg = "Attempted to set the anchor B of a limb's pull joint to an invalid value (" + value + ")\n" + Environment.StackTrace;
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
                        + Environment.StackTrace;
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

        public string Name => Params.Name;

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

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
                ignoreCollisions = true;
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

            GameMain.World.AddJoint(pullJoint);

            var element = limbParams.Element;
            if (element.Attribute("mouthpos") != null)
            {
                MouthPos = ConvertUnits.ToSimUnits(element.GetAttributeVector2("mouthpos", Vector2.Zero));
            }

            body.BodyType = BodyType.Dynamic;

            damageModifiers = new List<DamageModifier>();

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
                        break;
                    case "damagemodifier":
                        damageModifiers.Add(new DamageModifier(subElement, character.Name));
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

        public AttackResult AddDamage(Vector2 simPosition, IEnumerable<Affliction> afflictions, bool playSound)
        {
            List<DamageModifier> appliedDamageModifiers = new List<DamageModifier>();
            //create a copy of the original affliction list to prevent modifying the afflictions of an Attack/StatusEffect etc
            var afflictionsCopy = afflictions.Where(a => Rand.Range(0.0f, 1.0f) <= a.Probability).ToList();
            for (int i = 0; i < afflictionsCopy.Count; i++)
            {
                foreach (DamageModifier damageModifier in damageModifiers)
                {
                    if (!damageModifier.MatchesAffliction(afflictionsCopy[i])) continue;
                    if (SectorHit(damageModifier.ArmorSectorInRadians, simPosition))
                    {
                        afflictionsCopy[i] = afflictionsCopy[i].CreateMultiplied(damageModifier.DamageMultiplier);
                        appliedDamageModifiers.Add(damageModifier);
                    }
                }

                foreach (WearableSprite wearable in wearingItems)
                {
                    foreach (DamageModifier damageModifier in wearable.WearableComponent.DamageModifiers)
                    {
                        if (!damageModifier.MatchesAffliction(afflictionsCopy[i])) continue;
                        if (SectorHit(damageModifier.ArmorSectorInRadians, simPosition))
                        {
                            afflictionsCopy[i] = afflictionsCopy[i].CreateMultiplied(damageModifier.DamageMultiplier);
                            appliedDamageModifiers.Add(damageModifier);
                        }
                    }
                }
            }

            AddDamageProjSpecific(simPosition, afflictionsCopy, playSound, appliedDamageModifiers);

            return new AttackResult(afflictionsCopy, this, appliedDamageModifiers);
        }

        partial void AddDamageProjSpecific(Vector2 simPosition, List<Affliction> afflictions, bool playSound, List<DamageModifier> appliedDamageModifiers);

        public bool SectorHit(Vector2 armorSector, Vector2 simPosition)
        {
            if (armorSector == Vector2.Zero) { return false; }
            //sector 360 degrees or more -> always hits
            if (Math.Abs(armorSector.Y - armorSector.X) >= MathHelper.TwoPi) { return true; }
            float rotation = body.TransformedRotation;
            float offset = (MathHelper.PiOver2 - GetArmorSectorRotationOffset(armorSector)) * Dir;
            float hitAngle = VectorExtensions.Angle(VectorExtensions.Forward(rotation + offset), SimPosition - simPosition);
            float sectorSize = GetArmorSectorSize(armorSector);
            return hitAngle < sectorSize / 2;
        }

        protected float GetArmorSectorRotationOffset(Vector2 armorSector)
        {
            float midAngle = MathUtils.GetMidAngle(armorSector.X, armorSector.Y);
            float spritesheetOrientation = Params.GetSpriteOrientation();
            return midAngle + spritesheetOrientation;
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

            if (attack != null)
            {
                attack.UpdateCoolDown(deltaTime);
            }
        }

        partial void UpdateProjSpecific(float deltaTime);

        /// <summary>
        /// Returns true if the attack successfully hit something. If the distance is not given, it will be calculated.
        /// </summary>
        public bool UpdateAttack(float deltaTime, Vector2 attackSimPos, IDamageable damageTarget, out AttackResult attackResult, float distance = -1, Limb targetLimb = null)
        {
            attackResult = default(AttackResult);
            float dist = distance > -1 ? distance : ConvertUnits.ToDisplayUnits(Vector2.Distance(SimPosition, attackSimPos));
            bool wasRunning = attack.IsRunning;
            attack.UpdateAttackTimer(deltaTime);

            bool wasHit = false;
            Body structureBody = null;
            if (damageTarget != null)
            {
                switch (attack.HitDetectionType)
                {
                    case HitDetection.Distance:
                        if (dist < attack.DamageRange)
                        {
                            // TODO: cache
                            List<Body> ignoredBodies = character.AnimController.Limbs.Select(l => l.body.FarseerBody).ToList();
                            ignoredBodies.Add(character.AnimController.Collider.FarseerBody);

                            structureBody = Submarine.PickBody(
                                SimPosition, attackSimPos,
                                ignoredBodies, Physics.CollisionWall);
                            
                            if (damageTarget is Item)
                            {
                                // If the attack is aimed to an item and hits an item, it's successful.
                                // Ignore blocking on items, because it causes cases where a Mudraptor cannot hit the hatch, for example.
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
                        // TODO: ensure that this works
                        var targetBodies = new List<Body>();
                        if (damageTarget is Character targetCharacter)
                        {
                            foreach (Limb limb in targetCharacter.AnimController.Limbs)
                            {
                                if (!limb.IsSevered && limb.body?.FarseerBody != null) targetBodies.Add(limb.body.FarseerBody);
                            }
                        }
                        else if (damageTarget is Structure targetStructure)
                        {
                            if (character.Submarine == null && targetStructure.Submarine != null)
                            {
                                targetBodies.Add(targetStructure.Submarine.PhysicsBody.FarseerBody);
                            }
                            else
                            {
                                targetBodies.AddRange(targetStructure.Bodies);
                            }
                        }
                        else if (damageTarget is Item)
                        {
                            Item targetItem = damageTarget as Item;
                            if (targetItem.body?.FarseerBody != null) targetBodies.Add(targetItem.body.FarseerBody);
                        }

                        if (targetBodies != null)
                        {
                            ContactEdge contactEdge = body.FarseerBody.ContactList;
                            while (contactEdge != null)
                            {
                                if (contactEdge.Contact != null &&
                                    contactEdge.Contact.IsTouching &&
                                    targetBodies.Any(b => b == contactEdge.Contact.FixtureA?.Body || b == contactEdge.Contact.FixtureB?.Body))
                                {
                                    structureBody = targetBodies.LastOrDefault();
                                    wasHit = true;
                                    break;
                                }
                                contactEdge = contactEdge.Next;
                            }
                        }
                        break;
                }
            }

            if (wasHit)
            {
                wasHit = damageTarget != null;
            }

            if (wasHit)
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
                    attackResult = attack.DoDamageToLimb(character, targetLimb, WorldPosition, 1.0f, playSound);
                }
                else
                {
                    attackResult = attack.DoDamage(character, damageTarget, WorldPosition, 1.0f, playSound);
                }
                if (structureBody != null && attack.StickChance > Rand.Range(0.0f, 1.0f, Rand.RandSync.Server))
                {
                    // TODO: use the hit pos?
                    var localFront = body.GetLocalFront(Params.GetSpriteOrientation());
                    var from = body.FarseerBody.GetWorldPoint(localFront);
                    var to = from;
                    var drawPos = body.DrawPosition;
                    StickTo(structureBody, from, to);
                }
                attack.ResetAttackTimer();
                attack.SetCoolDown();
            }

            Vector2 diff = attackSimPos - SimPosition;
            bool applyForces = (!attack.ApplyForcesOnlyOnce || !wasRunning) && diff.LengthSquared() > 0.00001f;
            if (applyForces)
            {
                body.ApplyTorque(Mass * character.AnimController.Dir * attack.Torque);
                if (attack.ForceOnLimbIndices != null && attack.ForceOnLimbIndices.Count > 0)
                {
                    foreach (int limbIndex in attack.ForceOnLimbIndices)
                    {
                        if (limbIndex < 0 || limbIndex >= character.AnimController.Limbs.Length) continue;

                        Limb limb = character.AnimController.Limbs[limbIndex];
                        Vector2 forcePos = limb.pullJoint == null ? limb.body.SimPosition : limb.pullJoint.WorldAnchorA;
                        limb.body.ApplyLinearImpulse(limb.Mass * attack.Force * Vector2.Normalize(attackSimPos - SimPosition), forcePos,
                            maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    }
                }
                else
                {
                    Vector2 forcePos = pullJoint == null ? body.SimPosition : pullJoint.WorldAnchorA;
                    body.ApplyLinearImpulse(
                        Mass * attack.Force * Vector2.Normalize(attackSimPos - SimPosition), 
                        forcePos, 
                        maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                }
            }
            return wasHit;
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
                GameMain.World.AddJoint(colliderJoint);
            }

            attachJoint = new WeldJoint(body.FarseerBody, target, from, to, true)
            {
                FrequencyHz = 1,
                DampingRatio = 0.5f,
                KinematicBodyB = true,
                CollideConnected = false
            };
            GameMain.World.AddJoint(attachJoint);
        }

        public void Release()
        {
            if (!IsStuck) { return; }
            GameMain.World.RemoveJoint(attachJoint);
            attachJoint = null;
            if (colliderJoint != null)
            {
                GameMain.World.RemoveJoint(colliderJoint);
                colliderJoint = null;
            }
        }

        public void Remove()
        {
            body?.Remove();
            body = null;
            Release();
            RemoveProjSpecific();
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
