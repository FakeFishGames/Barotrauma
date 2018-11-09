//using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum LimbType
    {
        None, LeftHand, RightHand, LeftArm, RightArm,
        LeftLeg, RightLeg, LeftFoot, RightFoot, Head, Torso, Tail, Legs, RightThigh, LeftThigh, Waist
    };
    
    class LimbJoint : RevoluteJoint
    {
        public bool IsSevered;
        public bool CanBeSevered;

        public readonly Limb LimbA, LimbB;

        public LimbJoint(Limb limbA, Limb limbB, Vector2 anchor1, Vector2 anchor2)
            : base(limbA.body.FarseerBody, limbB.body.FarseerBody, anchor1, anchor2)
        {
            CollideConnected = false;
            MotorEnabled = true;
            MaxMotorTorque = 0.25f;

            LimbA = limbA;
            LimbB = limbB;
        }
    }
    
    partial class Limb
    {
        private const float LimbDensity = 15;
        private const float LimbAngularDamping = 7;

        //how long it takes for severed limbs to fade out
        private const float SeveredFadeOutTime = 10.0f;

        public readonly Character character;
        
        //the physics body of the limb
        public PhysicsBody body;

        protected readonly Vector2 stepOffset;
        
        public Sprite sprite, damagedSprite;

        public bool inWater;

        private readonly FixedMouseJoint pullJoint;

        public readonly LimbType type;

        public readonly bool ignoreCollisions;
        
        private bool isSevered;
        private float severedFadeOutTimer;
                
        public Vector2? MouthPos;
        
        //a timer for delaying when a hitsound/attacksound can be played again
        public float SoundTimer;
        public const float SoundInterval = 0.4f;

        public readonly Attack attack;
        private List<DamageModifier> damageModifiers;

        private Direction dir;
        
        public float AttackTimer;

        public bool IsSevered
        {
            get { return isSevered; }
            set
            {
                isSevered = value;
                if (!isSevered) severedFadeOutTimer = 0.0f;
#if CLIENT
                if (isSevered) damage = 100.0f;
#endif
            }
        }

        public bool DoesFlip { get; private set; }

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

        public float Scale { get; private set; }

        //where an animcontroller is trying to pull the limb, only used for debug visualization
        public Vector2 AnimTargetPos { get; private set; }

        public float SteerForce { get; private set; }

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
            set { dir = (value==-1.0f) ? Direction.Left : Direction.Right; }
        }

        public int RefJointIndex { get; private set; }

        public Vector2 StepOffset
        {
            get { return stepOffset; }
        }

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
        }
        
        public Vector2 PullJointWorldAnchorB
        {
            get { return pullJoint.WorldAnchorB; }
            set
            {                
                if (!MathUtils.IsValid(value))
                {
                    string errorMsg = "Attempted to set the anchor of a limb's pull joint to an invalid value (" + value + ")\n" + Environment.StackTrace;
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Limb.SetPullJointAnchor:InvalidValue", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return;
                }


                if (Vector2.DistanceSquared(pullJoint.WorldAnchorA, value) > 50.0f * 50.0f)
                {
                    Vector2 diff = value - pullJoint.WorldAnchorA;
                    string errorMsg = "Attempted to move the anchor of a limb's pull joint extremely far from the limb (diff: " + diff +
                        ", limb enabled: " + body.Enabled +
                        ", simple physics enabled: " + character.AnimController.SimplePhysicsEnabled + ")\n"
                        + Environment.StackTrace;
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Limb.SetPullJointAnchor:ExcessiveValue", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return;
                }

                pullJoint.WorldAnchorB = value;                
            }
        }
        public List<WearableSprite> WearingItems { get; private set; }

        public Limb (Character character, XElement element, float scale = 1.0f)
        {
            this.character = character;

            WearingItems = new List<WearableSprite>();
            
            dir = Direction.Right;
            DoesFlip = element.GetAttributeBool("flip", false);

            Scale = scale;

            body = new PhysicsBody(element, scale);

            if (element.GetAttributeBool("ignorecollisions", false))
            {
                body.CollisionCategories = Category.None;
                body.CollidesWith = Category.None;

                ignoreCollisions = true;
            }
            else
            {
                //limbs don't collide with each other
                body.CollisionCategories = Physics.CollisionCharacter;
                body.CollidesWith = Physics.CollisionAll & ~Physics.CollisionCharacter & ~Physics.CollisionItem;
            }
            
            body.UserData = this;

            RefJointIndex = -1;

            Vector2 pullJointPos = Vector2.Zero;

            if (element.Attribute("type") != null)
            {
                try
                {
                    type = (LimbType)Enum.Parse(typeof(LimbType), element.Attribute("type").Value, true);
                }
                catch
                {
                    type = LimbType.None;
                    DebugConsole.ThrowError("Error in "+element+"! \""+element.Attribute("type").Value+"\" is not a valid limb type");
                }


                pullJointPos = element.GetAttributeVector2("pullpos", Vector2.Zero) * scale;
                pullJointPos = ConvertUnits.ToSimUnits(pullJointPos);

                stepOffset = element.GetAttributeVector2("stepoffset", Vector2.Zero) * scale;
                stepOffset = ConvertUnits.ToSimUnits(stepOffset);

                RefJointIndex = element.GetAttributeInt("refjoint", -1);

            }
            else
            {
                type = LimbType.None;
            }

            pullJoint = new FixedMouseJoint(body.FarseerBody, pullJointPos)
            {
                Enabled = false,
                MaxForce = ((type == LimbType.LeftHand || type == LimbType.RightHand) ? 400.0f : 150.0f) * body.Mass
            };

            GameMain.World.AddJoint(pullJoint);

            SteerForce = element.GetAttributeFloat("steerforce", 0.0f);
            
            if (element.Attribute("mouthpos") != null)
            {
                MouthPos = ConvertUnits.ToSimUnits(element.GetAttributeVector2("mouthpos", Vector2.Zero));
            }

            body.BodyType = BodyType.Dynamic;
            body.FarseerBody.AngularDamping = LimbAngularDamping;

            damageModifiers = new List<DamageModifier>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        string spritePath = subElement.Attribute("texture").Value;

                        string spritePathWithTags = spritePath;

                        if (character.Info != null)
                        {
                            spritePath = spritePath.Replace("[GENDER]", (character.Info.Gender == Gender.Female) ? "f" : "");
                            spritePath = spritePath.Replace("[HEADID]", character.Info.HeadSpriteId.ToString());

                            if (character.Info.HeadSprite != null && character.Info.SpriteTags.Any())
                            {
                                string tags = "";
                                character.Info.SpriteTags.ForEach(tag => tags += "[" + tag + "]");

                                spritePathWithTags = Path.Combine(
                                    Path.GetDirectoryName(spritePath),
                                    Path.GetFileNameWithoutExtension(spritePath) + tags + Path.GetExtension(spritePath));
                            }
                        }

                        if (File.Exists(spritePathWithTags))
                        {
                            sprite = new Sprite(subElement, "", spritePathWithTags);
                        }
                        else
                        {

                            sprite = new Sprite(subElement, "", spritePath);
                        }

                        break;
                    case "damagedsprite":
                        string damagedSpritePath = subElement.Attribute("texture").Value;

                        if (character.Info != null)
                        {
                            damagedSpritePath = damagedSpritePath.Replace("[GENDER]", (character.Info.Gender == Gender.Female) ? "f" : "");
                            damagedSpritePath = damagedSpritePath.Replace("[HEADID]", character.Info.HeadSpriteId.ToString());
                        }

                        damagedSprite = new Sprite(subElement, "", damagedSpritePath);
                        break;
                    case "attack":
                        attack = new Attack(subElement);
                        break;
                    case "damagemodifier":
                        damageModifiers.Add(new DamageModifier(subElement));
                        break;
                }
            }

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
        
        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, bool playSound)
        {
            List<DamageModifier> appliedDamageModifiers = new List<DamageModifier>();

            foreach (DamageModifier damageModifier in damageModifiers)
            {
                if (damageModifier.DamageType == DamageType.None) continue;
                if (damageModifier.DamageType.HasFlag(damageType) && SectorHit(damageModifier.ArmorSector, position))
                {
                    appliedDamageModifiers.Add(damageModifier);
                }
            }
            
            foreach (WearableSprite wearable in WearingItems)
            {
                foreach (DamageModifier damageModifier in wearable.WearableComponent.DamageModifiers)
                {
                    if (damageModifier.DamageType == DamageType.None) continue;
                    if (damageModifier.DamageType.HasFlag(damageType) && SectorHit(damageModifier.ArmorSector, position))
                    {
                        appliedDamageModifiers.Add(damageModifier);
                    }
                }
            }

            foreach (DamageModifier damageModifier in appliedDamageModifiers)
            {
                amount *= damageModifier.DamageMultiplier;
                bleedingAmount *= damageModifier.BleedingMultiplier;
            }

            AddDamageProjSpecific(position, damageType, amount, bleedingAmount, playSound, appliedDamageModifiers);

            return new AttackResult(amount, bleedingAmount, appliedDamageModifiers);
        }

        partial void AddDamageProjSpecific(Vector2 position, DamageType damageType, float amount, float bleedingAmount, bool playSound, List<DamageModifier> appliedDamageModifiers);

        public bool SectorHit(Vector2 armorSector, Vector2 simPosition)
        {
            if (armorSector == Vector2.Zero) return false;
            
            float rot = body.Rotation;
            if (Dir == -1) rot -= MathHelper.Pi;

            Vector2 armorLimits = new Vector2(rot - armorSector.X * Dir, rot - armorSector.Y * Dir);

            float mid = (armorLimits.X + armorLimits.Y) / 2.0f;
            float angleDiff = MathUtils.GetShortestAngle(MathUtils.VectorToAngle(simPosition - SimPosition), mid);

            return (Math.Abs(angleDiff) < (armorSector.Y - armorSector.X) / 2.0f);
        }

        public void Update(float deltaTime)
        {
            UpdateProjSpecific(deltaTime);

            if (LinearVelocity.X > 500.0f)
            {
                //DebugConsole.ThrowError("CHARACTER EXPLODED");
                body.ResetDynamics();
                body.SetTransform(character.SimPosition, 0.0f);           
            }

            if (inWater)
            {
                body.ApplyWaterForces();
            }

            if (isSevered)
            {
                severedFadeOutTimer += deltaTime;
                if (severedFadeOutTimer > SeveredFadeOutTime)
                {
                    body.Enabled = false;
                }
            }

            if (character.IsDead) return;

            SoundTimer -= deltaTime;
        }

        partial void UpdateProjSpecific(float deltaTime);
                
        public void UpdateAttack(float deltaTime, Vector2 attackPosition, IDamageable damageTarget)
        {
            float dist = ConvertUnits.ToDisplayUnits(Vector2.Distance(SimPosition, attackPosition));

            AttackTimer += deltaTime;

            body.ApplyTorque(Mass * character.AnimController.Dir * attack.Torque);

            bool wasHit = false;

            if (damageTarget != null)
            {
                switch (attack.HitDetectionType)
                {
                    case HitDetection.Distance:
                        wasHit = dist < attack.DamageRange;
                        break;
                    case HitDetection.Contact:
                        List<Body> targetBodies = new List<Body>();
                        if (damageTarget is Character)
                        {
                            Character targetCharacter = (Character)damageTarget;
                            foreach (Limb limb in targetCharacter.AnimController.Limbs)
                            {
                                if (!limb.IsSevered && limb.body?.FarseerBody != null) targetBodies.Add(limb.body.FarseerBody);
                            }
                        }
                        else if (damageTarget is Structure)
                        {
                            Structure targetStructure = (Structure)damageTarget;
                            
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
                if (AttackTimer >= attack.Duration && damageTarget != null)
                {
                    attack.DoDamage(character, damageTarget, WorldPosition, 1.0f, (SoundTimer <= 0.0f));
                    SoundTimer = SoundInterval;
                }
            }

            Vector2 diff = attackPosition - SimPosition;
            if (diff.LengthSquared() < 0.00001f) return;
            
            if (attack.ApplyForceOnLimbs != null)
            {
                foreach (int limbIndex in attack.ApplyForceOnLimbs)
                {
                    if (limbIndex < 0 || limbIndex >= character.AnimController.Limbs.Length) continue;

                    Limb limb = character.AnimController.Limbs[limbIndex];
                    Vector2 forcePos = limb.pullJoint == null ? limb.body.SimPosition : limb.pullJoint.WorldAnchorA;
                    limb.body.ApplyLinearImpulse(
                        limb.Mass * attack.Force * Vector2.Normalize(attackPosition - SimPosition), forcePos);
                }
            }
            else
            {
                Vector2 forcePos = pullJoint == null ? body.SimPosition : pullJoint.WorldAnchorA;
                body.ApplyLinearImpulse(Mass * attack.Force *
                    Vector2.Normalize(attackPosition - SimPosition), forcePos);
            }

        }
        
        public void Remove()
        {
            if (sprite != null)
            {
                sprite.Remove();
                sprite = null;
            }
            
            if (damagedSprite != null)
            {
                damagedSprite.Remove();
                damagedSprite = null;
            }

            if (body != null)
            {
                body.Remove();
                body = null;
            }

#if CLIENT
            if (LightSource != null)
            {
                LightSource.Remove();
            }
#endif
        }
    }
}
