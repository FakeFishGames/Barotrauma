using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace Subsurface.Items.Components
{
    class Projectile : ItemComponent
    {
        private float launchImpulse;

        private bool doesStick;
        private PrismaticJoint stickJoint;
        private Body stickTarget;

        Attack attack;

        public List<Body> ignoredBodies;

        [HasDefaultValue(10.0f, false)]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [HasDefaultValue(false, false)]
        public bool CharacterUsable
        {
            get { return characterUsable; }
            set { characterUsable = value; }
        }

        [HasDefaultValue(false, false)]
        public bool DoesStick
        {
            get { return doesStick; }
            set { doesStick = value; }
        }

        public Projectile(Item item, XElement element) 
            : base (item, element)
        {
            ignoredBodies = new List<Body>();

            //launchImpulse = ToolBox.GetAttributeFloat(element, "launchimpulse", 10.0f);
            //characterUsable = ToolBox.GetAttributeBool(element, "characterusable", false);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "attack") continue;
                attack = new Attack(subElement);
            }

            //bleedingDamage = ToolBox.GetAttributeFloat(element, "bleedingdamage", 0.0f);
            //bluntDamage = ToolBox.GetAttributeFloat(element, "bluntdamage", 0.0f);

            //doesStick = ToolBox.GetAttributeBool(element, "doesstick", false);
        }

        //public override void ConstructionActivate(Construction c, Vector2 modifier)
        //{
        //    for (int i = 0; i < item.linkedTo.Count; i++)
        //        item.linkedTo[i].RemoveLinked((MapEntity)item);
        //    item.linkedTo.Clear();

        //    ApplyStatusEffects(StatusEffect.Type.OnUse, 1.0f, null);
            
        //    Launch(modifier+Vector2.Normalize(modifier)*launchImpulse);

        //}

        public override bool Use(Character character = null)
        {
            if (character != null && !characterUsable) return false;

            ApplyStatusEffects(ActionType.OnUse, 1.0f, character);

            Debug.WriteLine(item.body.Rotation);

            Launch(new Vector2(
                (float)Math.Cos(item.body.Rotation), 
                (float)Math.Sin(item.body.Rotation))*launchImpulse*item.body.Mass);

            return true;
        }

        private void Launch(Vector2 impulse)
        {
            item.body.Enabled = true;
            item.body.ApplyLinearImpulse(impulse);
            
            item.body.FarseerBody.OnCollision += OnProjectileCollision;
            item.body.FarseerBody.IsBullet = true;

            item.body.CollisionCategories = Physics.CollisionProjectile;
            item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall;

            item.Drop();

            if (stickJoint != null && doesStick)
            {
                if (stickTarget!=null) item.body.FarseerBody.RestoreCollisionWith(stickTarget);
                Game1.world.RemoveJoint(stickJoint);
                stickJoint = null;
            }
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (stickJoint != null)
            {
                if (stickJoint.JointTranslation < 0.01f)
                {
                    if (stickTarget!=null)
                    {
                        item.body.FarseerBody.RestoreCollisionWith(stickTarget);
                    }

                    Game1.world.RemoveJoint(stickJoint);
                    stickJoint = null;

                    isActive = false;
                }
            }
            else
            {
                isActive = false;
            }
        }

        private bool OnProjectileCollision(Fixture f1, Fixture f2, Contact contact)
        {
            //doesn't collide with items
            //if (f2.Body.UserData is Item) return false;

            if (ignoredBodies.Contains(f2.Body)) return false;

            //Structure structure = f1.Body.UserData as Structure;
            //if (structure!=null && (structure.IsPlatform || structure.StairDirection != Direction.None)) return false;

            //Vector2 force = f1.Body.LinearVelocity * f1.Body.Mass;
            //float forceLength = force.Length();

            //if (forceLength > 20.0f)
            //{
            //    force = force / forceLength * 20.0f;
            //}

            //f2.Body.ApplyLinearImpulse(force);
            //f1.Body.ApplyLinearImpulse(-f1.Body.LinearVelocity * f1.Body.Mass);

            //float damage = f1.Body.LinearVelocity.Length();

            if (attack!=null)
            {
                Limb limb;
                Structure structure;
                if ((limb = (f2.Body.UserData as Limb)) != null)
                {
                    attack.DoDamage(limb.character, item.SimPosition, 0.0f);
                    //limb.Damage += damage;
                    //limb.Bleeding += bleedingDamage;

                    //if (bleedingDamage>0.0f)
                    //{
                    //    for (int i = 0; i < 5; i++ )
                    //    {
                    //        Game1.particleManager.CreateParticle(limb.SimPosition,
                    //            ToolBox.VectorToAngle(-f1.Body.LinearVelocity*0.5f) + ToolBox.RandomFloat(-0.5f, 0.5f), 
                    //            ToolBox.RandomFloat(1.0f, 3.0f), "blood");
                    //    }

                    //    Game1.particleManager.CreateParticle(limb.SimPosition,
                    //        0.0f,
                    //        Vector2.Zero, "waterblood");
                    //}

                    //AmbientSoundManager.PlayDamageSound(DamageType.LimbBlunt, damage, limb.body.FarseerBody);
                }
                else if ((structure = (f2.Body.UserData as Structure)) != null)
                {
                    attack.DoDamage(structure, item.SimPosition, 0.0f);

                    //AmbientSoundManager.PlayDamageSound(DamageType.StructureBlunt, damage, f2.Body);
                }
            }


            item.body.FarseerBody.OnCollision -= OnProjectileCollision;

            item.body.FarseerBody.IsBullet = false;
            item.body.CollisionCategories = Physics.CollisionMisc;
            item.body.CollidesWith = Physics.CollisionWall;

            ignoredBodies.Clear();

            if (doesStick)
            {
                Vector2 normal = contact.Manifold.LocalNormal;
                Vector2 dir = new Vector2(
                    (float)Math.Cos(item.body.Rotation), 
                    (float)Math.Sin(item.body.Rotation));

                if (Vector2.Dot(f1.Body.LinearVelocity, normal)<0 ) return StickToTarget(f2.Body, dir);
            }

            return true;
        }

        private bool StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) return false;

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.Position, axis, true);
            stickJoint.MotorEnabled = true;
            stickJoint.MaxMotorForce = 30.0f;

            stickJoint.LimitEnabled = true;
            stickJoint.UpperLimit = ConvertUnits.ToSimUnits(item.sprite.size.X*0.7f);

            item.body.FarseerBody.IgnoreCollisionWith(targetBody);
            stickTarget = targetBody;
            Game1.world.AddJoint(stickJoint);

            isActive = true;

            return false;
        }
    }
}
