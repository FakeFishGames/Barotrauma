using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class Projectile : ItemComponent
    {
        //continuous collision detection is used while the projectile is moving faster than this
        const float ContinuousCollisionThreshold = 5.0f;

        //a duration during which the projectile won't drop from the body it's stuck to
        private const float PersistentStickJointDuration = 1.0f;

        private float launchImpulse;
        
        private PrismaticJoint stickJoint;
        private Body stickTarget;

        private Attack attack;

        public List<Body> IgnoredBodies;

        public Character User;

        private float persistentStickJointTimer;

        [Serialize(10.0f, false)]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [Serialize(false, false)]
        public bool CharacterUsable
        {
            get { return characterUsable; }
            set { characterUsable = value; }
        }

        [Serialize(false, false)]
        //backwards compatibility, can stick to anything
        public bool DoesStick
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool StickToCharacters
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool StickToStructures
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool StickToItems
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool Hitscan
        {
            get;
            set;
        }

        [Serialize(false, false)]
        public bool RemoveOnHit
        {
            get;
            set;
        }

        public Projectile(Item item, XElement element) 
            : base (item, element)
        {
            IgnoredBodies = new List<Body>();

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "attack") continue;
                attack = new Attack(subElement);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character != null && !characterUsable) return false;

            Vector2 launchDir = new Vector2((float)Math.Cos(item.body.Rotation), (float)Math.Sin(item.body.Rotation));

            if (Hitscan)
            {
                DoHitscan(launchDir);
            }
            else
            {
                Launch(launchDir * launchImpulse * item.body.Mass);
            }

            User = character;

            return true;
        }

        private void Launch(Vector2 impulse)
        {
            item.Drop();

            item.body.Enabled = true;            
            item.body.ApplyLinearImpulse(impulse);
            
            item.body.FarseerBody.OnCollision += OnProjectileCollision;
            item.body.FarseerBody.IsBullet = true;

            item.body.CollisionCategories = Physics.CollisionProjectile;
            item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;

            IsActive = true;

            if (stickJoint == null) return;

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

        private void DoHitscan(Vector2 dir)
        {
            float rotation = item.body.Rotation;
            item.Drop();

            item.body.Enabled = true;
            //set the velocity of the body because the OnProjectileCollision method
            //uses it to determine the direction from which the projectile hit
            item.body.LinearVelocity = dir;
            IsActive = true;

            Vector2 rayStart = item.SimPosition;
            Vector2 rayEnd = item.SimPosition + dir * 1000.0f;

            List<Tuple<Fixture, Vector2, Vector2>> hits = new List<Tuple<Fixture, Vector2, Vector2>>();
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                if (fixture == null || fixture.IsSensor) return -1;

                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) return -1;

                /*item.body.SetTransform(point, rotation);
                if (OnProjectileCollision(fixture, normal))
                {
                    Character.Controlled.AnimController.Teleport(point - Character.Controlled.SimPosition, Vector2.Zero);
                    hitSomething = true;
                    return 0;
                }*/

                hits.Add(new Tuple<Fixture, Vector2, Vector2>(fixture,point,normal));

                return hits.Count<25 ? 1 : 0;
            }, rayStart, rayEnd);

            bool hitSomething = false;
            hits = hits.OrderBy(t => Vector2.DistanceSquared(rayStart, t.Item2)).ToList();
            foreach (Tuple<Fixture, Vector2, Vector2> t in hits)
            {
                Fixture fixture = t.Item1;
                Vector2 point = t.Item2;
                Vector2 normal = t.Item3;
                item.body.SetTransform(point, rotation);
                if (OnProjectileCollision(fixture, normal))
                {
                    hitSomething = true;
                    //Character.Controlled.AnimController.Teleport(point - Character.Controlled.SimPosition, Vector2.Zero);
                    break;
                }
            }

            //the raycast didn't hit anything -> the projectile flew somewhere outside the level and is permanently lost
            if (!hitSomething)
            {
                Entity.Spawner.AddToRemoveQueue(item);
            }
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            ApplyStatusEffects(ActionType.OnActive, deltaTime, null); 

            if (item.body != null && item.body.FarseerBody.IsBullet)
            {
                if (item.body.LinearVelocity.LengthSquared() < ContinuousCollisionThreshold * ContinuousCollisionThreshold)
                {
                    item.body.FarseerBody.IsBullet = false;
                    //projectiles with a stickjoint don't become inactive until the stickjoint is detached
                    if (stickJoint == null) IsActive = false;
                }
            }

            if (stickJoint == null) return;

            if (persistentStickJointTimer > 0.0f)
            {
                persistentStickJointTimer -= deltaTime;
                return;
            }

            if (stickJoint.JointTranslation < stickJoint.LowerLimit * 0.9f || stickJoint.JointTranslation > stickJoint.UpperLimit * 0.9f)  
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
             
                if (!item.body.FarseerBody.IsBullet) IsActive = false; 
            }           
        }

        private bool OnProjectileCollision(Fixture f1, Fixture f2, Contact contact)
        {
            return OnProjectileCollision(f2, contact.Manifold.LocalNormal);
        }

        private bool OnProjectileCollision(Fixture target, Vector2 collisionNormal)
        {
            if (IgnoredBodies.Contains(target.Body)) return false;

            if (target.CollisionCategories == Physics.CollisionCharacter && !(target.Body.UserData is Limb))
            {
                return false;
            }

            AttackResult attackResult = new AttackResult();
            Character character = null;
            if (attack != null)
            {
                var submarine = target.Body.UserData as Submarine;
                if (submarine != null)
                {
                    item.Move(-submarine.Position);
                    item.Submarine = submarine;
                    item.body.Submarine = submarine;
                    return true;
                }

                Limb limb = target.Body.UserData as Limb;
                Structure structure;
                if (limb != null)
                {
                    attackResult = attack.DoDamageToLimb(User, limb, item.WorldPosition, 1.0f);
                    if (limb.character != null)
                        character = limb.character;
                }
                else if ((structure = (target.Body.UserData as Structure)) != null)
                {
                    attackResult = attack.DoDamage(User, structure, item.WorldPosition, 1.0f);
                }
            }

            ApplyStatusEffects(ActionType.OnUse, 1.0f, character);
            ApplyStatusEffects(ActionType.OnImpact, 1.0f, character);
            
            item.body.FarseerBody.OnCollision -= OnProjectileCollision;

            item.body.CollisionCategories = Physics.CollisionItem;
            item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;

            IgnoredBodies.Clear();

            target.Body.ApplyLinearImpulse(item.body.LinearVelocity * item.body.Mass);

            if (attackResult.AppliedDamageModifiers != null &&
                attackResult.AppliedDamageModifiers.Any(dm => dm.DeflectProjectiles))
            {
                item.body.LinearVelocity *= 0.1f;
            }
            else if (Vector2.Dot(item.body.LinearVelocity, collisionNormal) < 0.0f &&
                        (DoesStick ||
                        (StickToCharacters && target.Body.UserData is Limb) ||
                        (StickToStructures && target.Body.UserData is Structure) ||
                        (StickToItems && target.Body.UserData is Item)))                
            {
                Vector2 dir = new Vector2(
                    (float)Math.Cos(item.body.Rotation),
                    (float)Math.Sin(item.body.Rotation));
                
                StickToTarget(target.Body, dir);
                item.body.LinearVelocity *= 0.5f;

                return Hitscan;                
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
                    //contained.Condition = 0.0f; //Let the freaking .xml handle it jeez
                }
            }

            if (RemoveOnHit)
            {
                Item.Spawner.AddToRemoveQueue(item);
            }

            return true;
        }

        private void StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) return;

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, true);
            stickJoint.MotorEnabled = true;
            stickJoint.MaxMotorForce = 30.0f;

            stickJoint.LimitEnabled = true;
            if (item.Sprite != null)
            {
                stickJoint.LowerLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X * -0.3f);
                stickJoint.UpperLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X * 0.3f);
            }

            persistentStickJointTimer = PersistentStickJointDuration;

            item.body.FarseerBody.IgnoreCollisionWith(targetBody);
            stickTarget = targetBody;
            GameMain.World.AddJoint(stickJoint);

            IsActive = true;
        }

        protected override void RemoveComponentSpecific()
        {
            if (stickJoint != null)
            {
                try
                {
                    GameMain.World.RemoveJoint(stickJoint);
                }
                catch
                {
                    //the body that the projectile was stuck to has been removed
                }

                stickJoint = null;
            }

        }
    }
}
