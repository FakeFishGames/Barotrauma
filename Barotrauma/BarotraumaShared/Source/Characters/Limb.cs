using System;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
//using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.IO;

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

        private readonly int refJointIndex;

        private readonly float steerForce;

        private readonly bool doesFlip;
        
        protected readonly Vector2 stepOffset;
        
        public Sprite sprite, damagedSprite;

        public bool inWater;

        public FixedMouseJoint pullJoint;

        public readonly LimbType type;

        public readonly bool ignoreCollisions;
        
        private float damage, burnt;

        private bool isSevered;
        private float severedFadeOutTimer;

        private readonly Vector2 armorSector;
        private readonly float armorValue;
        
        public Vector2? MouthPos;
        
        //a timer for delaying when a hitsound/attacksound can be played again
        public float SoundTimer;
        public const float SoundInterval = 0.4f;

        public readonly Attack attack;

        private Direction dir;

        private List<WearableSprite> wearingItems;

        private Vector2 animTargetPos;

        private float scale;
        
        public float AttackTimer;

        public bool IsSevered
        {
            get { return isSevered; }
            set
            {
                isSevered = value;
                if (isSevered)
                {
                    damage = 100.0f;
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
        
        public float Burnt
        {
            get { return burnt; }
            set { burnt = MathHelper.Clamp(value,0.0f,100.0f); }
        }
        
        public List<WearableSprite> WearingItems
        {
            get { return wearingItems; }
        }
  
        public Limb (Character character, XElement element, float scale = 1.0f)
        {
            this.character = character;

            wearingItems = new List<WearableSprite>();
            
            dir = Direction.Right;

            doesFlip = ToolBox.GetAttributeBool(element, "flip", false);

            this.scale = scale;

            body = new PhysicsBody(element, scale);

            if (ToolBox.GetAttributeBool(element, "ignorecollisions", false))
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


                pullJointPos = ToolBox.GetAttributeVector2(element, "pullpos", Vector2.Zero) * scale;
                pullJointPos = ConvertUnits.ToSimUnits(pullJointPos);

                stepOffset = ToolBox.GetAttributeVector2(element, "stepoffset", Vector2.Zero) * scale;
                stepOffset = ConvertUnits.ToSimUnits(stepOffset);

                refJointIndex = ToolBox.GetAttributeInt(element, "refjoint", -1);

            }
            else
            {
                type = LimbType.None;
            }

            pullJoint = new FixedMouseJoint(body.FarseerBody, pullJointPos);
            pullJoint.Enabled = false;
            pullJoint.MaxForce = ((type == LimbType.LeftHand || type == LimbType.RightHand) ? 400.0f : 150.0f) * body.Mass;

            GameMain.World.AddJoint(pullJoint);

            steerForce = ToolBox.GetAttributeFloat(element, "steerforce", 0.0f);

            //maxHealth = Math.Max(ToolBox.GetAttributeFloat(element, "health", 100.0f),1.0f);

            armorSector = ToolBox.GetAttributeVector2(element, "armorsector", Vector2.Zero);
            armorSector.X = MathHelper.ToRadians(armorSector.X);
            armorSector.Y = MathHelper.ToRadians(armorSector.Y);

            armorValue = Math.Max(ToolBox.GetAttributeFloat(element, "armor", 0.0f), 0.0f);

            if (element.Attribute("mouthpos") != null)
            {
                MouthPos = ConvertUnits.ToSimUnits(ToolBox.GetAttributeVector2(element, "mouthpos", Vector2.Zero));
            }

            body.BodyType = BodyType.Dynamic;
            body.FarseerBody.AngularDamping = LimbAngularDamping;

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
                }
            }

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

        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, bool playSound)
        {
            bool hitArmor = false;
            float totalArmorValue = 0.0f;

            if (armorValue > 0.0f && SectorHit(armorSector, position))
            {
                hitArmor = true;
                totalArmorValue += armorValue;
            }

            foreach (WearableSprite wearable in wearingItems)
            {
                if (wearable.WearableComponent.ArmorValue > 0.0f &&
                    SectorHit(wearable.WearableComponent.ArmorSectorLimits, position))
                {
                    hitArmor = true;
                    totalArmorValue += wearable.WearableComponent.ArmorValue;
                }
            }
            
#if CLIENT
            float bloodAmount = hitArmor || bleedingAmount <= 0.0f ? 0 : (int)Math.Min((int)(amount * 2.0f), 20);

            DamageSoundType damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.LimbBlunt : DamageSoundType.LimbSlash;
        
            if (hitArmor)
            {
                totalArmorValue = Math.Max(totalArmorValue, 0.0f);

                damageSoundType = DamageSoundType.LimbArmor;
                amount = Math.Max(0.0f, amount - totalArmorValue);
                bleedingAmount = Math.Max(0.0f, bleedingAmount - totalArmorValue);
            }

            if (playSound)
            {
                SoundPlayer.PlayDamageSound(damageSoundType, amount, position);
            }

            float bloodParticleAmount = hitArmor || bleedingAmount <= 0.0f ? 0 : (int)Math.Min(amount / 5, 10);
            float bloodParticleSize = MathHelper.Clamp(amount / 50.0f, 0.1f, 1.0f);

            for (int i = 0; i < bloodParticleAmount; i++)
            {
                var blood = GameMain.ParticleManager.CreateParticle(inWater ? "waterblood" : "blood", WorldPosition, Vector2.Zero, 0.0f, character.AnimController.CurrentHull);
                if (blood != null)
                {
                    blood.Size *= bloodParticleSize;
                }
            }

#endif

            damage += Math.Max(amount,bleedingAmount) / character.MaxHealth * 100.0f;

            return new AttackResult(amount, bleedingAmount, hitArmor);
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

            if (!character.IsDead) damage = Math.Max(0.0f, damage - deltaTime * 0.1f);
            if (burnt > 0.0f) Burnt -= deltaTime;

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

            damage = Math.Max(0.0f, damage - deltaTime * 0.1f);
            SoundTimer -= deltaTime;
        }

        partial void UpdateProjSpecific();

        public void ActivateDamagedSprite()
        {
            damage = 100.0f;
        }
        
        public void UpdateAttack(float deltaTime, Vector2 attackPosition, IDamageable damageTarget)
        {
            float dist = ConvertUnits.ToDisplayUnits(Vector2.Distance(SimPosition, attackPosition));

            AttackTimer += deltaTime;

            body.ApplyTorque(Mass * character.AnimController.Dir * attack.Torque);

            if (dist < attack.Range * 0.5f)
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
            if (hitSound != null)
            {                                
                hitSound.Remove();
                hitSound = null;
            }

            if (LightSource != null)
            {
                LightSource.Remove();
            }
#endif
        }
    }
}
