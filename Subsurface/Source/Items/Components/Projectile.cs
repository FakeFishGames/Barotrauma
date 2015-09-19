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

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character != null && !characterUsable) return false;

            //ApplyStatusEffects(ActionType.OnUse, 1.0f, character);

            Debug.WriteLine(item.body.Rotation);

            Launch(new Vector2(
                (float)Math.Cos(item.body.Rotation),
                (float)Math.Sin(item.body.Rotation)) * launchImpulse * item.body.Mass);

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
                GameMain.World.RemoveJoint(stickJoint);
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

                    GameMain.World.RemoveJoint(stickJoint);
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
            if (ignoredBodies.Contains(f2.Body)) return false;

            AttackResult attackResult = new AttackResult(0.0f, 0.0f);
            if (attack!=null)
            {
                Limb limb;
                Structure structure;
                if ((limb = (f2.Body.UserData as Limb)) != null)
                {
                    attackResult = attack.DoDamage(limb.character, item.SimPosition, 1.0f);
                }
                else if ((structure = (f2.Body.UserData as Structure)) != null)
                {
                    attackResult = attack.DoDamage(structure, item.SimPosition, 1.0f);
                }
            }


            item.body.FarseerBody.OnCollision -= OnProjectileCollision;

            item.body.FarseerBody.IsBullet = false;
            item.body.CollisionCategories = Physics.CollisionMisc;
            item.body.CollidesWith = Physics.CollisionWall;

            ignoredBodies.Clear();

            f2.Body.ApplyLinearImpulse(item.body.LinearVelocity * item.body.Mass);

            if (attackResult.HitArmor)
            {
                item.body.LinearVelocity *= 0.1f;
            }
            else if (doesStick)
            {
                Vector2 normal = contact.Manifold.LocalNormal;
                Vector2 dir = new Vector2(
                    (float)Math.Cos(item.body.Rotation), 
                    (float)Math.Sin(item.body.Rotation));

                if (Vector2.Dot(f1.Body.LinearVelocity, normal) < 0.0f) return StickToTarget(f2.Body, dir);
            }
            else
            {
                item.body.LinearVelocity *= 0.5f;
            }

            var containedItems = item.ContainedItems;
            if (containedItems == null) return true;
            foreach (Item contained in containedItems)
            {
                if (contained.body != null)
                {
                    contained.SetTransform(item.SimPosition, contained.body.Rotation);
                }
                contained.Condition = 0.0f;
            }

            return false;
        }

        private bool StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) return false;

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, true);
            stickJoint.MotorEnabled = true;
            stickJoint.MaxMotorForce = 30.0f;

            stickJoint.LimitEnabled = true;
            stickJoint.UpperLimit = ConvertUnits.ToSimUnits(item.sprite.size.X*0.7f);

            item.body.FarseerBody.IgnoreCollisionWith(targetBody);
            stickTarget = targetBody;
            GameMain.World.AddJoint(stickJoint);

            isActive = true;

            return false;
        }
    }
}
