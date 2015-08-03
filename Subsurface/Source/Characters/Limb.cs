using System;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Items.Components;

namespace Subsurface
{
    public enum LimbType
    {
        None, LeftHand, RightHand, LeftArm, RightArm,
        LeftLeg, RightLeg, LeftFoot, RightFoot, Head, Torso, Tail, Legs, RightThigh, LeftThigh, Waist
    };

    class Limb
    {
        private const float LimbDensity = 15;
        private const float LimbAngularDamping = 7;

        public readonly Character character;
        
        //the physics body of the limb
        public PhysicsBody body;
        private Texture2D bodyShapeTexture;

        private readonly int refJointIndex;

        private readonly float steerForce;

        private readonly bool doesFlip;
        
        public Sprite sprite;

        public bool inWater;

        public FixedMouseJoint pullJoint;

        public readonly LimbType type;

        public readonly bool ignoreCollisions;

        //private readonly float maxHealth;
        //private float damage;
        //private float bleeding;

        public readonly float impactTolerance;

        private readonly Vector2 armorSector;
        private readonly float armorValue;

        Sound hitSound;
        //a timer for delaying when a hitsound/attacksound can be played again
        public float soundTimer;
        public const float SoundInterval = 0.2f;

        public readonly Attack attack;

        private Direction dir;

        private Item wearingItem;
        private WearableSprite wearingItemSprite;

        private Vector2 animTargetPos;

        public Texture2D BodyShapeTexture
        {
            get { return bodyShapeTexture; }
        }
        
        public bool DoesFlip
        {
            get { return doesFlip; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(body.Position); }
        }

        public Vector2 SimPosition
        {
            get { return body.Position; }
        }

        public float Rotation
        {
            get { return body.Rotation; }
        }

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

        public Sound HitSound
        {
            get { return hitSound; }
        }
                
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

        //public float Damage
        //{
        //    get { return damage; }
        //    set 
        //    { 
        //        damage = Math.Max(value, 0.0f);
        //        if (damage >=maxHealth) character.Kill();
        //    }
        //}

        //public float MaxHealth
        //{
        //    get { return maxHealth; }
        //}

        //public float Bleeding
        //{
        //    get { return bleeding; }
        //    set { bleeding = MathHelper.Clamp(value, 0.0f, 100.0f); }
        //}

        public Item WearingItem
        {
            get { return wearingItem; }
            set { wearingItem = value; }
        }

        public WearableSprite WearingItemSprite
        {
            get { return wearingItemSprite; }
            set { wearingItemSprite = value; }
        }

        public Limb (Character character, XElement element)
        {
            this.character = character;
            
            dir = Direction.Right;

            doesFlip = ToolBox.GetAttributeBool(element, "flip", false);

            body = new PhysicsBody(element);

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
                body.CollidesWith = Physics.CollisionAll & ~Physics.CollisionCharacter & ~Physics.CollisionMisc;
            }

            impactTolerance = ToolBox.GetAttributeFloat(element, "impacttolerance", 10.0f);

            body.UserData = this;

            refJointIndex = -1;

            if (element.Attribute("type") != null)
            {
                try
                {
                    type = (LimbType)Enum.Parse(typeof(LimbType), element.Attribute("type").Value, true);
                }
                catch
                {
                    type = LimbType.None;
                    DebugConsole.ThrowError("Error in "+element+"! ''"+element.Attribute("type").Value+"'' is not a valid limb type");
                }


                Vector2 jointPos = ToolBox.GetAttributeVector2(element, "pullpos", Vector2.Zero);

                jointPos = ConvertUnits.ToSimUnits(jointPos);

                refJointIndex = ToolBox.GetAttributeInt(element, "refjoint", -1);

                pullJoint = new FixedMouseJoint(body.FarseerBody, jointPos);
                pullJoint.Enabled = false;
                pullJoint.MaxForce = 150.0f * body.Mass;

                Game1.World.AddJoint(pullJoint);
            }
            else
            {
                type = LimbType.None;
            }

            steerForce = ToolBox.GetAttributeFloat(element, "steerforce", 0.0f);

            //maxHealth = Math.Max(ToolBox.GetAttributeFloat(element, "health", 100.0f),1.0f);

            armorSector = ToolBox.GetAttributeVector2(element, "armorsector", Vector2.Zero);
            armorSector.X = MathHelper.ToRadians(armorSector.X);
            armorSector.Y = MathHelper.ToRadians(armorSector.Y);

            armorValue = Math.Max(ToolBox.GetAttributeFloat(element, "armor", 1.0f), 1.0f);
            
            body.BodyType = BodyType.Dynamic;
            body.FarseerBody.AngularDamping = LimbAngularDamping;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "sprite":
                        string spritePath = subElement.Attribute("texture").Value;

                        if (character.Info!=null)
                        {
                            spritePath = spritePath.Replace("[GENDER]", (character.Info.Gender == Gender.Female) ? "f" : "");
                            spritePath = spritePath.Replace("[HEADID]", character.Info.HeadSpriteId.ToString());
                        }


                        sprite = new Sprite(subElement, "", spritePath);
                        break;
                    case "attack":
                        attack = new Attack(subElement);
                        break;
                    case "sound":
                        hitSound = Sound.Load(ToolBox.GetAttributeString(subElement, "file", ""));
                        break;
                }
            }
        }

        public void Move(Vector2 pos, float amount, bool pullFromCenter=false)
        {
            Vector2 pullPos = body.Position;
            if (pullJoint!=null && !pullFromCenter)
            {
                pullPos = pullJoint.WorldAnchorA;
            }

            animTargetPos = pos;

            Vector2 vel = body.LinearVelocity;
            Vector2 deltaPos = pos - pullPos;
            deltaPos *= amount;
            body.ApplyLinearImpulse((deltaPos - vel * 0.5f) * body.Mass, pullPos);
        }

        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, bool playSound)
        {
            DamageSoundType damageSoundType = (damageType == DamageType.Blunt) ? DamageSoundType.LimbBlunt : DamageSoundType.LimbSlash;

            bool hitArmor = false;
            if (armorSector != Vector2.Zero)
            {
                float rot = body.Rotation;
                if (Dir == -1) rot -= MathHelper.Pi;

                Vector2 armorLimits = new Vector2(rot-armorSector.X*Dir, rot-armorSector.Y*Dir);

                float mid = (armorLimits.X + armorLimits.Y) / 2.0f;

                float angleDiff = MathUtils.GetShortestAngle(MathUtils.VectorToAngle(position - SimPosition), mid);

                if (Math.Abs(angleDiff) < (armorSector.Y - armorSector.X) / 2.0f)
                {
                    hitArmor = true;
                    damageSoundType = DamageSoundType.LimbArmor;
                    amount /= armorValue;
                    bleedingAmount /= armorValue;
                }
            }

            if (playSound)
            {
                AmbientSoundManager.PlayDamageSound(damageSoundType, amount, position);
            }

            //Bleeding += bleedingAmount;
            //Damage += amount;

            float bloodAmount = hitArmor ? 0 : (int)Math.Min((int)(amount * 2.0f), 20);
            //if (closestLimb.Damage>=100.0f)
            //{
            //    bloodAmount *= 2;
            //    foreach (var joint in animController.limbJoints)
            //    {
            //        if (!(joint.BodyA == closestLimb.body.FarseerBody) && !(joint.BodyB == closestLimb.body.FarseerBody)) continue;

            //        joint.Enabled = false;
            //        break;                    
            //    }
            //}

            for (int i = 0; i < bloodAmount; i++)
            {
                Vector2 particleVel = SimPosition - position;
                if (particleVel != Vector2.Zero) particleVel = Vector2.Normalize(particleVel);

                Game1.ParticleManager.CreateParticle("blood",
                    SimPosition,
                    particleVel * Rand.Range(1.0f, 3.0f));
            }

            for (int i = 0; i < bloodAmount / 2; i++)
            {
                Game1.ParticleManager.CreateParticle("waterblood", SimPosition, Vector2.Zero);
            }

            return new AttackResult(amount, bleedingAmount, hitArmor);
        }

        public void Update(float deltaTime)
        {
            if (LinearVelocity.X>100.0f)
            {
                DebugConsole.ThrowError("CHARACTER EXPLODED");
                foreach (Limb limb in character.AnimController.limbs)
                {
                    limb.body.ResetDynamics();
                    limb.body.SetTransform(body.Position, 0.0f);
                }                
            }

            if (inWater)
            {
                //buoyancy
                Vector2 buoyancy = new Vector2(0, Mass * 9.6f);

                //drag
                Vector2 velDir = Vector2.Normalize(LinearVelocity);

                Vector2 line = new Vector2((float)Math.Cos(body.Rotation), (float)Math.Sin(body.Rotation));
                line *= ConvertUnits.ToSimUnits(sprite.size.Y);

                Vector2 normal = new Vector2(-line.Y, line.X);
                normal = Vector2.Normalize(-normal);

                float dragDot = Vector2.Dot(normal, velDir);
                Vector2 dragForce = Vector2.Zero;
                if (dragDot > 0)
                {
                    float vel = LinearVelocity.Length();
                    float drag = dragDot * vel * vel
                        * ConvertUnits.ToSimUnits(sprite.size.Y);
                    dragForce = drag * -velDir;
                    if (dragForce.Length() > 100.0f) { }
                }

                body.ApplyForce(dragForce + buoyancy);
                body.ApplyTorque(body.AngularVelocity * body.Mass * -0.05f);
            }

            if (character.IsDead) return;

            soundTimer -= deltaTime;

            //if (MathUtils.RandomFloat(0.0f, 1000.0f) < Bleeding)
            //{
            //    Game1.particleManager.CreateParticle(
            //        !inWater ? "blood" : "waterblood",
            //        SimPosition, Vector2.Zero);
            //}
        }


        public void Draw(SpriteBatch spriteBatch)
        {
            Color color = Color.White;// new Color(1.0f, 1.0f - damage / maxHealth, 1.0f - damage / maxHealth);

            body.Dir = Dir;

            if (wearingItem == null || !wearingItemSprite.HideLimb)
            {
                body.Draw(spriteBatch, sprite, color);
            }
            else
            {
                body.UpdateDrawPosition();
            }
            
            if (wearingItem != null)
            {
                SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

                Vector2 origin = wearingItemSprite.Sprite.Origin;
                if (body.Dir == -1.0f) origin.X = wearingItemSprite.Sprite.SourceRect.Width - origin.X;

                wearingItemSprite.Sprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    color, origin,
                    -body.DrawRotation,
                    1.0f, spriteEffect, sprite.Depth - 0.000001f);
            }

            if (!Game1.DebugDraw) return;

            if (pullJoint!=null)
            {
                Vector2 pos = ConvertUnits.ToDisplayUnits(pullJoint.WorldAnchorB);
                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)pos.Y, 5, 5), Color.Red, true);
            }

            if (bodyShapeTexture == null)
            {
                switch (body.bodyShape)
                {
                    case PhysicsBody.Shape.Rectangle:
                        bodyShapeTexture = GUI.CreateRectangle(
                            (int)ConvertUnits.ToDisplayUnits(body.width), 
                            (int)ConvertUnits.ToDisplayUnits(body.height));
                        break;

                    case PhysicsBody.Shape.Capsule:
                        bodyShapeTexture = GUI.CreateCapsule(
                            (int)ConvertUnits.ToDisplayUnits(body.radius),
                            (int)ConvertUnits.ToDisplayUnits(body.height));
                        break;
                    case PhysicsBody.Shape.Circle:
                        bodyShapeTexture = GUI.CreateCircle((int)ConvertUnits.ToDisplayUnits(body.radius));
                        break;
                }
            }
            spriteBatch.Draw(
                bodyShapeTexture,
                new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                null,
                Color.White,
                -body.DrawRotation,
                new Vector2(bodyShapeTexture.Width / 2, bodyShapeTexture.Height / 2), 1.0f, SpriteEffects.None, 0.0f);
        }
        

        public void Remove()
        {
            sprite.Remove();
            body.Remove();
            if (hitSound!=null) hitSound.Remove();
        }
    }
}
