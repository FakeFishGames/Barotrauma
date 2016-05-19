using System;
using System.Collections.Generic;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    class Projectile : ItemComponent
    {
        private float launchImpulse;

        private bool doesStick;
        private PrismaticJoint stickJoint;
        private Body stickTarget;

        private Attack attack;

        public List<Body> IgnoredBodies;

        public Character User;

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
            IgnoredBodies = new List<Body>();

            //launchImpulse = ToolBox.GetAttributeFloat(element, "launchimpulse", 10.0f);
            //characterUsable = ToolBox.GetAttributeBool(element, "characterusable", false);

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "attack") continue;
                attack = new Attack(subElement);
            }

            //bleedingDamage = ToolBox.GetAttributeFloat(element, "bleedingdamage", 0.0f);
            //bluntDamage = ToolBox.GetAttributeFloat(element, "bluntdamage", 0.0f);

            //doesStick = ToolBox.GetAttributeBool(element, "doesstick", false);
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character != null && !characterUsable) return false;

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
            item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;

            item.Drop();

            if (stickJoint == null || !doesStick) return;

            if (stickTarget != null)
            {
                try
                {
                    item.body.FarseerBody.RestoreCollisionWith(stickTarget);
                }
                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Failed to restore collision with stickTarget", e);
#endif
                }

                stickTarget = null;
            }
            GameMain.World.RemoveJoint(stickJoint);
            stickJoint = null;
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (stickJoint != null && stickJoint.JointTranslation < 0.01f)  
            {
                if (stickTarget != null)
                {
                    try
                    {
                        item.body.FarseerBody.RestoreCollisionWith(stickTarget);
                    }
                    catch 
                    {
                        //the body that the projectile was stuck to has been removed
                    }

                    stickTarget = null;
                }

                try
                {
                    GameMain.World.RemoveJoint(stickJoint);
                }
                catch
                {
                    //the body that the projectile was stuck to has been removed
                }

                stickJoint = null; 
             
                IsActive = false; 
            }           
        }

        private bool OnProjectileCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (IgnoredBodies.Contains(f2.Body)) return false;

            AttackResult attackResult = new AttackResult(0.0f, 0.0f);
            if (attack != null)
            {
                var submarine = f2.Body.UserData as Submarine;
                if (submarine != null)
                {
                    item.Move(-submarine.Position);
                    item.Submarine = submarine;
                    item.body.Submarine = submarine;
                    //item.FindHull();
                    return true;
                }

                Limb limb;
                Structure structure;
                if ((limb = (f2.Body.UserData as Limb)) != null)
                {
                    attackResult = attack.DoDamage(User, limb.character, item.WorldPosition, 1.0f);
                }
                else if ((structure = (f2.Body.UserData as Structure)) != null)
                {
                    attackResult = attack.DoDamage(User, structure, item.WorldPosition, 1.0f);
                }
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f);
            ApplyStatusEffects(ActionType.OnImpact, 1.0f);

            item.body.FarseerBody.OnCollision -= OnProjectileCollision;

            item.body.FarseerBody.IsBullet = false;
            item.body.CollisionCategories = Physics.CollisionItem;
            item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;

            IgnoredBodies.Clear();

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
            if (containedItems != null)
            {
                foreach (Item contained in containedItems)
                {
                    if (contained.body != null)
                    {
                        contained.SetTransform(item.SimPosition, contained.body.Rotation);
                    }
                    contained.Condition = 0.0f;
                }
            }

            return f2.CollisionCategories != Physics.CollisionCharacter;
        }

        private bool StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) return false;

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, true);
            stickJoint.MotorEnabled = true;
            stickJoint.MaxMotorForce = 30.0f;

            stickJoint.LimitEnabled = true;
            stickJoint.UpperLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X*0.7f);

            item.body.FarseerBody.IgnoreCollisionWith(targetBody);
            stickTarget = targetBody;
            GameMain.World.AddJoint(stickJoint);

            IsActive = true;

            return false;
        }
    }
}
