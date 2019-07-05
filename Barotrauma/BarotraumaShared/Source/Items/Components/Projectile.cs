using Barotrauma.Networking;
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
        struct HitscanResult
        {
            public Fixture Fixture;
            public Vector2 Point;
            public Vector2 Normal;
            public float Fraction;

            public HitscanResult(Fixture fixture, Vector2 point, Vector2 normal, float fraction)
            {
                Fixture = fixture;
                Point = point;
                Normal = normal;
                Fraction = fraction;
            }
        }

        //continuous collision detection is used while the projectile is moving faster than this
        const float ContinuousCollisionThreshold = 5.0f;

        //a duration during which the projectile won't drop from the body it's stuck to
        private const float PersistentStickJointDuration = 1.0f;

        private float launchImpulse;
        
        private PrismaticJoint stickJoint;
        private Body stickTarget;

        private Attack attack;

        public List<Body> IgnoredBodies;

        private Character user;
        public Character User
        {
            get { return user; }
            set
            {
                user = value;
                attack?.SetUser(user);                
            }
        }

        private float persistentStickJointTimer;

        [Serialize(10.0f, false)]
        public float LaunchImpulse
        {
            get { return launchImpulse; }
            set { launchImpulse = value; }
        }

        [Serialize(0.0f, false)]
        public float LaunchRotation
        {
            get { return MathHelper.ToDegrees(LaunchRotationRadians); }
            set { LaunchRotationRadians = MathHelper.ToRadians(value); }
        }

        public float LaunchRotationRadians
        {
            get;
            private set;
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

        [Serialize(1, false)]
        public int HitScanCount
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

        [Serialize(0.0f, false)]
        public float Spread
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
                attack = new Attack(subElement, item.Name + ", Projectile");
            }
        }

        public override void OnItemLoaded()
        {
            if (attack != null && attack.DamageRange <= 0.0f && item.body != null)
            {
                switch (item.body.BodyShape)
                {
                    case PhysicsBody.Shape.Circle:
                        attack.DamageRange = item.body.radius;
                        break;
                    case PhysicsBody.Shape.Capsule:
                        attack.DamageRange = item.body.height / 2 + item.body.radius;
                        break;
                    case PhysicsBody.Shape.Rectangle:
                        attack.DamageRange = new Vector2(item.body.width / 2.0f, item.body.height / 2.0f).Length();
                        break;
                }
                attack.DamageRange = ConvertUnits.ToDisplayUnits(attack.DamageRange);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character != null && !characterUsable) { return false; }

            for (int i = 0; i < HitScanCount; i++)
            {
                float launchAngle = item.body.Rotation + MathHelper.ToRadians(Rand.Range(-Spread, Spread));
                Vector2 launchDir = new Vector2((float)Math.Cos(launchAngle), (float)Math.Sin(launchAngle));
                if (Hitscan)
                {
                    Vector2 prevSimpos = item.SimPosition;
                    DoHitscan(launchDir);
                    if (i < HitScanCount - 1)
                    {
                        item.SetTransform(prevSimpos, item.body.Rotation);
                    }
                }
                else
                {
                    Launch(launchDir * launchImpulse * item.body.Mass);
                }
            }

            User = character;

            return true;
        }

        private void Launch(Vector2 impulse)
        {
            if (item.AiTarget != null)
            {
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
            }

            item.Drop(null);

            item.body.Enabled = true;            
            item.body.ApplyLinearImpulse(impulse, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            
            item.body.FarseerBody.OnCollision += OnProjectileCollision;
            item.body.FarseerBody.IsBullet = true;

            item.body.CollisionCategories = Physics.CollisionProjectile;
            item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;

            IsActive = true;

            if (stickJoint == null) return;

            if (stickTarget != null)
            {
#if DEBUG
                try
                {
#endif
                    item.body.FarseerBody.RestoreCollisionWith(stickTarget);
#if DEBUG
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to restore collision with stickTarget", e);
                }
#endif

                stickTarget = null;
            }
            GameMain.World.RemoveJoint(stickJoint);
            stickJoint = null;
        }
        
        private void DoHitscan(Vector2 dir)
        {
            float rotation = item.body.Rotation;
            Vector2 simPositon = item.SimPosition;
            item.Drop(null);

            item.body.Enabled = true;
            //set the velocity of the body because the OnProjectileCollision method
            //uses it to determine the direction from which the projectile hit
            item.body.LinearVelocity = dir;
            IsActive = true;

            Vector2 rayStart = simPositon;
            Vector2 rayEnd = simPositon + dir * 1000.0f;

            List<HitscanResult> hits = new List<HitscanResult>();

            hits.AddRange(DoRayCast(rayStart, rayEnd));

            if (item.Submarine != null)
            {
                //shooting indoors, do a hitscan outside as well
                hits.AddRange(DoRayCast(rayStart + item.Submarine.SimPosition, rayEnd + item.Submarine.SimPosition));
            }
            else
            {
                //shooting outdoors, see if we can hit anything inside a sub
                foreach (Submarine submarine in Submarine.Loaded)
                {
                    var inSubHits = DoRayCast(rayStart - submarine.SimPosition, rayEnd - submarine.SimPosition);
                    //transform back to world coordinates
                    for (int i = 0; i < inSubHits.Count; i++)
                    {
                        inSubHits[i] = new HitscanResult(
                            inSubHits[i].Fixture, 
                            inSubHits[i].Point + submarine.SimPosition, 
                            inSubHits[i].Normal, 
                            inSubHits[i].Fraction);
                    }

                    hits.AddRange(inSubHits);
                }
            }

            bool hitSomething = false;
            hits = hits.OrderBy(h => h.Fraction).ToList();
            foreach (HitscanResult h in hits)
            {
                item.body.SetTransform(h.Point, rotation);
                if (OnProjectileCollision(h.Fixture, h.Normal))
                {
                    hitSomething = true;
                    break;
                }
            }

            //the raycast didn't hit anything -> the projectile flew somewhere outside the level and is permanently lost
            if (!hitSomething)
            {
                if (Entity.Spawner == null)
                {
                    item.Remove();
                }
                else
                {
                    Entity.Spawner.AddToRemoveQueue(item);
                }
            }
        }
        
        private List<HitscanResult> DoRayCast(Vector2 rayStart, Vector2 rayEnd)
        {
            List<HitscanResult> hits = new List<HitscanResult>();

            Vector2 dir = rayEnd - rayStart;
            dir = dir.LengthSquared() < 0.00001f ? Vector2.UnitY : Vector2.Normalize(dir);

            //do an AABB query first to see if the start of the ray is inside a fixture
            var aabb = new FarseerPhysics.Collision.AABB(rayStart - Vector2.One * 0.001f, rayStart + Vector2.One * 0.001f);
            GameMain.World.QueryAABB((fixture) =>
            {
                //ignore sensors and items
                if (fixture?.Body == null || fixture.IsSensor) return true;
                if (fixture.UserData is Item) return true;

                //ignore everything else than characters, sub walls and level walls
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) return true;

                hits.Add(new HitscanResult(fixture, rayStart, -dir, 0.0f));
                return true;
            }, ref aabb);

            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                //ignore sensors and items
                if (fixture?.Body == null || fixture.IsSensor) return -1;
                if (fixture.UserData is Item) return -1;

                //ignore everything else than characters, sub walls and level walls
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) return -1;

                hits.Add(new HitscanResult(fixture, point, normal, fraction));

                return hits.Count < 25 ? 1 : 0;
            }, rayStart, rayEnd);

            return hits;
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
                    if (GameMain.World.BodyList.Contains(stickTarget))
                    {
                        item.body.FarseerBody.RestoreCollisionWith(stickTarget);
                    }
                    
                    stickTarget = null;
                }

                if (stickJoint != null)
                {
                    if (GameMain.World.JointList.Contains(stickJoint))
                    {
                        GameMain.World.RemoveJoint(stickJoint);
                    }

                    stickJoint = null;
                }
                
                if (!item.body.FarseerBody.IsBullet) IsActive = false; 
            }           
        }

        private bool OnProjectileCollision(Fixture f1, Fixture f2, Contact contact)
        {
            return OnProjectileCollision(f2, contact.Manifold.LocalNormal);
        }

        private bool OnProjectileCollision(Fixture target, Vector2 collisionNormal)
        {
            if (User != null && User.Removed) User = null;

            if (IgnoredBodies.Contains(target.Body)) return false;

            if (target.UserData is Item) return false;

            if (target.CollisionCategories == Physics.CollisionCharacter && !(target.Body.UserData is Limb))
            {
                return false;
            }

            AttackResult attackResult = new AttackResult();
            Character character = null;
            if (target.Body.UserData is Submarine submarine)
            {
                item.Move(-submarine.Position);
                item.Submarine = submarine;
                item.body.Submarine = submarine;
                return !Hitscan;
            }
            else if (target.Body.UserData is Limb limb)
            {
                //severed limbs don't deactivate the projectile (but may still slow it down enough to make it inactive)
                if (limb.IsSevered)
                {
                    target.Body.ApplyLinearImpulse(item.body.LinearVelocity * item.body.Mass);
                    return true;
                }

                limb.character.LastDamageSource = item;
                if (attack != null) { attackResult = attack.DoDamageToLimb(User, limb, item.WorldPosition, 1.0f); }
                if (limb.character != null) { character = limb.character; }
            }
            else if (target.Body.UserData is Structure structure)
            {
                if (attack != null) { attackResult = attack.DoDamage(User, structure, item.WorldPosition, 1.0f); }
            }

            if (character != null) character.LastDamageSource = item;
            ApplyStatusEffects(ActionType.OnUse, 1.0f, character, target.Body.UserData as Limb, user: user);
            ApplyStatusEffects(ActionType.OnImpact, 1.0f, character, target.Body.UserData as Limb, user: user);
            
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
                }
            }

            if (RemoveOnHit)
            {
                Entity.Spawner.AddToRemoveQueue(item);
            }

            return true;
        }

        private void StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) return;

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, true)
            {
                MotorEnabled = true,
                MaxMotorForce = 30.0f,
                LimitEnabled = true
            };
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
