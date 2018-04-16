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
    
    partial class Limb : ISerializableEntity
    {
        private const float LimbDensity = 15;
        private const float LimbAngularDamping = 7;

        //how long it takes for severed limbs to fade out
        private const float SeveredFadeOutTime = 10.0f;

        public readonly Character character;
        
        //the physics body of the limb
        public PhysicsBody body;

        private readonly int refJointIndex;

        private readonly float steerForce;

        private readonly bool doesFlip;
        
        protected readonly Vector2 stepOffset;
        
        public Sprite sprite, damagedSprite;

        public bool inWater;

        public FixedMouseJoint pullJoint;

        public readonly LimbType type;

        public readonly bool ignoreCollisions;
        
        private float damageOverlayStrength, burnOverLayStrength;

        private bool isSevered;
        private float severedFadeOutTimer;
                
        public Vector2? MouthPos;
        
        public readonly Attack attack;

        private Direction dir;

        private List<WearableSprite> wearingItems;

        private Vector2 animTargetPos;

        private float scale;

        private List<DamageModifier> damageModifiers;
        
        public float AttackTimer;

        public readonly int HealthIndex;
        
        public readonly float AttackPriority;

        public bool IsSevered
        {
            get { return isSevered; }
            set
            {
                isSevered = value;
                if (isSevered)
                {
                    damageOverlayStrength = 100.0f;
                }
            }
        }

        public bool DoesFlip
        {
            get { return doesFlip; }
        }

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

        public float Scale
        {
            get { return scale; }
        }

        //where an animcontroller is trying to pull the limb, only used for debug visualization
        public Vector2 AnimTargetPos
        {
            get { return animTargetPos; }
        }

        public float SteerForce
        {
            get { return steerForce; }
        }

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

        public int RefJointIndex
        {
            get { return refJointIndex; }
        }

        public Vector2 StepOffset
        {
            get { return stepOffset; }
        }
        
        public float DamageOverlayStrength
        {
            get { return damageOverlayStrength; }
            set { damageOverlayStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public float BurnOverlayStrength
        {
            get { return burnOverLayStrength; }
            set { burnOverLayStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        /*public float Damage
        {
            get { return character.CharacterHealth.GetDamage(this); }
            set { character.CharacterHealth.SetDamage(this, value); }
        }

        public float BurnDamage
        {
            get { return character.CharacterHealth.GetBurnDamage(this); }
            set { character.CharacterHealth.SetBurnDamage(this, value); }
        }

        public float BleedingAmount
        {
            get { return character.CharacterHealth.GetBleedingAmount(this); }
            set { character.CharacterHealth.SetBleedingAmount(this, value); }
        }*/

        public List<WearableSprite> WearingItems
        {
            get { return wearingItems; }
        }

        public string Name
        {
            get { return type.ToString(); }
        }

        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get;
            private set;
        }

        public Limb(Character character, XElement element, float scale = 1.0f)
        {
            this.character = character;
            wearingItems = new List<WearableSprite>();            
            dir = Direction.Right;
            this.scale = scale;

            HealthIndex = element.GetAttributeInt("healthindex", 0);
            AttackPriority = element.GetAttributeFloat("attackpriority", 0);

            doesFlip = element.GetAttributeBool("flip", false);

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
                body.CollidesWith = Physics.CollisionAll & ~Physics.CollisionCharacter & ~Physics.CollisionItem & ~Physics.CollisionItemBlocking;
            }
            
            body.UserData = this;
            refJointIndex = -1;
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

                refJointIndex = element.GetAttributeInt("refjoint", -1);

            }
            else
            {
                type = LimbType.None;
            }

            pullJoint = new FixedMouseJoint(body.FarseerBody, pullJointPos);
            pullJoint.Enabled = false;
            pullJoint.MaxForce = ((type == LimbType.LeftHand || type == LimbType.RightHand) ? 400.0f : 150.0f) * body.Mass;

            GameMain.World.AddJoint(pullJoint);

            steerForce = element.GetAttributeFloat("steerforce", 0.0f);
            
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

            SerializableProperties = SerializableProperty.GetProperties(this);

            InitProjSpecific(element);
        }
        partial void InitProjSpecific(XElement element);

        public void MoveToPos(Vector2 pos, float force, bool pullFromCenter=false)
        {
            Vector2 pullPos = body.SimPosition;
            if (pullJoint!=null && !pullFromCenter)
            {
                pullPos = pullJoint.WorldAnchorA;
            }

            animTargetPos = pos;

            body.MoveToPos(pos, force, pullPos);
        }

        public AttackResult AddDamage(Vector2 position, float damage, float bleedingDamage, float burnDamage, bool playSound)
        {
            List<Affliction> afflictions = new List<Affliction>();
            if (damage > 0.0f) afflictions.Add(AfflictionPrefab.InternalDamage.Instantiate(damage));
            if (bleedingDamage > 0.0f) afflictions.Add(AfflictionPrefab.Bleeding.Instantiate(bleedingDamage));
            if (burnDamage > 0.0f) afflictions.Add(AfflictionPrefab.Burn.Instantiate(burnDamage));

            return AddDamage(position, afflictions, playSound);
        }

        public AttackResult AddDamage(Vector2 position, List<Affliction> afflictions, bool playSound)
        {
            List<DamageModifier> appliedDamageModifiers = new List<DamageModifier>();
            //create a copy of the original affliction list to prevent modifying the afflictions of an Attack/StatusEffect etc
            afflictions = new List<Affliction>(afflictions);
            for (int i = 0; i < afflictions.Count; i++)
            {
                foreach (DamageModifier damageModifier in damageModifiers)
                {
                    if (!damageModifier.MatchesAffliction(afflictions[i])) continue;
                    if (SectorHit(damageModifier.ArmorSector, position))
                    {
                        afflictions[i] = afflictions[i].CreateMultiplied(damageModifier.DamageMultiplier);
                        appliedDamageModifiers.Add(damageModifier);
                    }
                }

                foreach (WearableSprite wearable in wearingItems)
                {
                    foreach (DamageModifier damageModifier in wearable.WearableComponent.DamageModifiers)
                    {
                        if (!damageModifier.MatchesAffliction(afflictions[i])) continue;
                        if (SectorHit(damageModifier.ArmorSector, position))
                        {
                            afflictions[i] = afflictions[i].CreateMultiplied(damageModifier.DamageMultiplier);
                            appliedDamageModifiers.Add(damageModifier);
                        }
                    }
                }
            }

#if CLIENT
            float bleedingDamage = afflictions.FindAll(a => a is AfflictionBleeding).Sum(a => a.GetVitalityDecrease(character.CharacterHealth));
            float damage = afflictions.FindAll(a => a.Prefab.AfflictionType == "damage").Sum(a => a.GetVitalityDecrease(character.CharacterHealth));

            if (playSound)
            {
                string damageSoundType = (bleedingDamage > damage) ? "LimbSlash" : "LimbBlunt";

                foreach (DamageModifier damageModifier in appliedDamageModifiers)
                {
                    if (!string.IsNullOrWhiteSpace(damageModifier.DamageSound))
                    {
                        damageSoundType = damageModifier.DamageSound;
                        break;
                    }
                }
                
                SoundPlayer.PlayDamageSound(damageSoundType, Math.Max(damage, bleedingDamage), position);
            }
            
            if (character.UseBloodParticles)
            {
                float bloodParticleAmount = (int)Math.Min(bleedingDamage * 5, 10);
                float bloodParticleSize = MathHelper.Clamp(bleedingDamage, 0.1f, 1.0f);

                for (int i = 0; i < bloodParticleAmount; i++)
                {
                    var blood = GameMain.ParticleManager.CreateParticle(inWater ? "waterblood" : "blood", WorldPosition, Vector2.Zero, 0.0f, character.AnimController.CurrentHull);
                    if (blood != null)
                    {
                        blood.Size *= bloodParticleSize;
                    }
                }

                if (bloodParticleAmount > 0 && character.CurrentHull != null)
                {
                    character.CurrentHull.AddDecal("blood", WorldPosition, MathHelper.Clamp(bloodParticleSize, 0.5f, 1.0f));
                }
            }
#endif

            return new AttackResult(afflictions, this, appliedDamageModifiers);
        }

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
            UpdateProjSpecific();
            
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

            damageOverlayStrength = Math.Max(0.0f, damageOverlayStrength - deltaTime * 0.1f);
        }

        partial void UpdateProjSpecific();

        public void ActivateDamagedSprite()
        {
            damageOverlayStrength = 100.0f;
        }
        
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
#if CLIENT
                    bool playSound = LastAttackSoundTime < Timing.TotalTime - SoundInterval;
                    attack.DoDamage(character, damageTarget, WorldPosition, 1.0f, playSound);
                    if (playSound)
                    {
                        LastAttackSoundTime = (float)SoundInterval;
                    }
#else
                    attack.DoDamage(character, damageTarget, WorldPosition, 1.0f, false);
#endif
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
