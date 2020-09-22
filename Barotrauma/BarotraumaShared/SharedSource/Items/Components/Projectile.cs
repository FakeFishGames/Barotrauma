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
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Projectile : ItemComponent, IServerSerializable
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
        struct Impact
        {
            public Fixture Fixture;
            public Vector2 Normal;
            public Vector2 LinearVelocity;

            public Impact(Fixture fixture, Vector2 normal, Vector2 velocity)
            {
                Fixture = fixture;
                Normal = normal;
                LinearVelocity = velocity;
            }
        }

        private readonly Queue<Impact> impactQueue = new Queue<Impact>();

        //continuous collision detection is used while the projectile is moving faster than this
        const float ContinuousCollisionThreshold = 5.0f;

        //a duration during which the projectile won't drop from the body it's stuck to
        private const float PersistentStickJointDuration = 1.0f;
        private PrismaticJoint stickJoint;

        public Attack Attack { get; private set; }

        private Vector2 launchPos;

        private readonly HashSet<Body> hits = new HashSet<Body>();

        public List<Body> IgnoredBodies;

        private Character user;
        public Character User
        {
            get { return user; }
            set
            {
                user = value;
                Attack?.SetUser(user);                
            }
        }

        public IEnumerable<Body> Hits
        {
            get { return hits; }
        }

        private float persistentStickJointTimer;

        [Serialize(10.0f, false, description: "The impulse applied to the physics body of the item when it's launched. Higher values make the projectile faster.")]
        public float LaunchImpulse { get; set; }

        [Serialize(0.0f, false, description: "The rotation of the item relative to the rotation of the weapon when launched (in degrees).")]
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

        [Serialize(false, false, description: "When set to true, the item can stick to any target it hits.")]
        //backwards compatibility, can stick to anything
        public bool DoesStick
        {
            get;
            set;
        }

        [Serialize(false, false, description: "When set to true, the item won't fall of a target it's stuck to unless removed.")]
        public bool StickPermanently
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick to the character it hits.")]
        public bool StickToCharacters
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick to the structure it hits.")]
        public bool StickToStructures
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick to the item it hits.")]
        public bool StickToItems
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Hitscan projectiles cast a ray forwards and immediately hit whatever the ray hits. "+
            "It is recommended to use hitscans for very fast-moving projectiles such as bullets, because using extremely fast launch velocities may cause physics glitches.")]
        public bool Hitscan
        {
            get;
            set;
        }

        [Serialize(1, false, description: "How many hitscans should be done when the projectile is launched. "
            + "Multiple hitscans can be used to simulate weapons that fire multiple projectiles at the same time" +
            " without having to actually use multiple projectile items, for example shotguns.")]
        public int HitScanCount
        {
            get;
            set;
        }

        [Serialize(1, false, description: "How many targets the projectile can hit before it stops.")]
        public int MaxTargetsToHit
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the item be deleted when it hits something.")]
        public bool RemoveOnHit
        {
            get;
            set;
        }

        [Serialize(0.0f, false, description: "Random spread applied to the launch angle of the projectile (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        public Body StickTarget 
        { 
            get; 
            private set; 
        }

        public bool IsStuckToTarget
        {
            get { return StickTarget != null; }
        }

        public Projectile(Item item, XElement element) 
            : base (item, element)
        {
            IgnoredBodies = new List<Body>();

            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("attack", StringComparison.OrdinalIgnoreCase)) { continue; }
                Attack = new Attack(subElement, item.Name + ", Projectile");
            }
        }

        public override void OnItemLoaded()
        {
            if (Attack != null && Attack.DamageRange <= 0.0f && item.body != null)
            {
                switch (item.body.BodyShape)
                {
                    case PhysicsBody.Shape.Circle:
                        Attack.DamageRange = item.body.radius;
                        break;
                    case PhysicsBody.Shape.Capsule:
                        Attack.DamageRange = item.body.height / 2 + item.body.radius;
                        break;
                    case PhysicsBody.Shape.Rectangle:
                        Attack.DamageRange = new Vector2(item.body.width / 2.0f, item.body.height / 2.0f).Length();
                        break;
                }
                Attack.DamageRange = ConvertUnits.ToDisplayUnits(Attack.DamageRange);
            }
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character != null && !characterUsable) { return false; }

            for (int i = 0; i < HitScanCount; i++)
            {
                float launchAngle = item.body.Rotation + MathHelper.ToRadians(Spread * Rand.Range(-0.5f, 0.5f));
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
                    Launch(launchDir * LaunchImpulse * item.body.Mass);
                }
            }

            User = character;

            return true;
        }

        private void Launch(Vector2 impulse)
        {
            hits.Clear();

            if (item.AiTarget != null)
            {
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
            }

            item.Drop(null);

            launchPos = item.SimPosition;

            item.body.Enabled = true;            
            item.body.ApplyLinearImpulse(impulse, maxVelocity: NetConfig.MaxPhysicsBodyVelocity * 0.9f);
            
            item.body.FarseerBody.OnCollision += OnProjectileCollision;
            item.body.FarseerBody.IsBullet = true;

            item.body.CollisionCategories = Physics.CollisionProjectile;
            item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;

            IsActive = true;

            if (stickJoint == null) { return; }

            StickTarget = null;            
            GameMain.World.Remove(stickJoint);
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
                //also in the coordinate space of docked subs
                foreach (Submarine dockedSub in item.Submarine.DockedTo)
                {
                    if (dockedSub == item.Submarine) { continue; }
                    hits.AddRange(DoRayCast(rayStart + item.Submarine.SimPosition - dockedSub.SimPosition, rayEnd + item.Submarine.SimPosition - dockedSub.SimPosition));
                }
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
                if (HandleProjectileCollision(h.Fixture, h.Normal, Vector2.Zero))
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
                if (fixture?.Body == null || fixture.IsSensor) { return true; }
                if (fixture.Body.UserData is Item item && (item.GetComponent<Door>() == null && !item.Prefab.DamagedByProjectiles || item.Condition <= 0)) { return true; }
                if (fixture.Body?.UserData as string == "ruinroom") { return true; }

                //ignore everything else than characters, sub walls and level walls
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) { return true; }

                if (fixture.Body.UserData is VoronoiCell && this.item.Submarine != null) { return true; }

                fixture.Body.GetTransform(out FarseerPhysics.Common.Transform transform);
                if (!fixture.Shape.TestPoint(ref transform, ref rayStart)) { return true; }

                hits.Add(new HitscanResult(fixture, rayStart, -dir, 0.0f));
                return true;
            }, ref aabb);

            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                //ignore sensors and items
                if (fixture?.Body == null || fixture.IsSensor) { return -1; }

                if (fixture.Body.UserData is Item item && (item.GetComponent<Door>() == null && !item.Prefab.DamagedByProjectiles || item.Condition <= 0)) { return -1; }
                if (fixture.Body?.UserData as string == "ruinroom") { return -1; }

                //ignore everything else than characters, sub walls and level walls
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) { return -1; }

                //ignore level cells if the item the point of impact are inside a sub
                if (fixture.Body.UserData is VoronoiCell && this.item.Submarine != null) 
                { 
                    if (Hull.FindHull(ConvertUnits.ToDisplayUnits(point), this.item.CurrentHull) != null)
                    {
                        return -1;
                    }
                }

                hits.Add(new HitscanResult(fixture, point, normal, fraction));

                return hits.Count < 25 ? 1 : 0;
            }, rayStart, rayEnd, Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel);

            return hits;
        }

        public override void Drop(Character dropper)
        {
            if (dropper != null)
            {
                Deactivate();
                Unstick();
            }
            base.Drop(dropper);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            ApplyStatusEffects(ActionType.OnActive, deltaTime, null);

            while (impactQueue.Count > 0)
            {
                var impact = impactQueue.Dequeue();
                HandleProjectileCollision(impact.Fixture, impact.Normal, impact.LinearVelocity);
            }

            if (item.body != null && item.body.FarseerBody.IsBullet)
            {
                if (item.body.LinearVelocity.LengthSquared() < ContinuousCollisionThreshold * ContinuousCollisionThreshold)
                {
                    item.body.FarseerBody.IsBullet = false;
                }
            }
            //projectiles with a stickjoint don't become inactive until the stickjoint is detached
            if (stickJoint == null && !item.body.FarseerBody.IsBullet) 
            { 
                IsActive = false; 
            }

            if (stickJoint == null) { return; }

            if (persistentStickJointTimer > 0.0f && !StickPermanently)
            {
                persistentStickJointTimer -= deltaTime;
                return;
            }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (StickTargetRemoved() ||
                    (!StickPermanently && (stickJoint.JointTranslation < stickJoint.LowerLimit * 0.9f || stickJoint.JointTranslation > stickJoint.UpperLimit * 0.9f)))
                {
                    Unstick();
#if SERVER
                    item.CreateServerEvent(this);                
#endif
                }
            }
        }

        private bool StickTargetRemoved()
        {
            if (StickTarget == null) { return true; }
            if (StickTarget.UserData is Limb limb) { return limb.character.Removed; }
            if (StickTarget.UserData is Entity entity) { return entity.Removed; }
            return false;
        }


        private bool OnProjectileCollision(Fixture f1, Fixture target, Contact contact)
        {
            if (User != null && User.Removed) { User = null; return false; }
            if (IgnoredBodies.Contains(target.Body)) { return false; }
            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }
            if (hits.Contains(target.Body)) { return false; }
            if (target.Body.UserData is Submarine sub)
            {
                Vector2 dir = item.body.LinearVelocity.LengthSquared() < 0.001f ?
                    contact.Manifold.LocalNormal : Vector2.Normalize(item.body.LinearVelocity);

                //do a raycast in the sub's coordinate space to see if it hit a structure
                var wallBody = Submarine.PickBody(
                    item.body.SimPosition - ConvertUnits.ToSimUnits(sub.Position) - dir,
                    item.body.SimPosition - ConvertUnits.ToSimUnits(sub.Position) + dir,
                    collisionCategory: Physics.CollisionWall);
                if (wallBody?.FixtureList?.First() != null && wallBody.UserData is Structure structure &&
                    //ignore the hit if it's behind the position the item was launched from, and the projectile is travelling in the opposite direction
                    Vector2.Dot(item.body.SimPosition - launchPos, dir) > 0) 
                {
                    target = wallBody.FixtureList.First();
                    if (hits.Contains(target.Body)) { return false; }
                }
                else
                {
                    return false;
                }
            }
            else if (target.Body.UserData is Limb limb)
            {
                //severed limbs don't deactivate the projectile (but may still slow it down enough to make it inactive)
                if (limb.IsSevered)
                {
                    target.Body.ApplyLinearImpulse(item.body.LinearVelocity * item.body.Mass);
                    return true;
                }
            }
            else if (target.Body.UserData is Item item)
            {
                if (item.Condition <= 0.0f) { return false; }
            }

            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }

            hits.Add(target.Body);
            impactQueue.Enqueue(new Impact(target, contact.Manifold.LocalNormal, item.body.LinearVelocity));
            IsActive = true;
            if (hits.Count() >= MaxTargetsToHit || target.Body.UserData is VoronoiCell)
            {
                Deactivate();
                return true;
            }
            else
            {
                return false;
            }
        }

        private bool HandleProjectileCollision(Fixture target, Vector2 collisionNormal, Vector2 velocity)
        {
            if (User != null && User.Removed) { User = null; }
            if (IgnoredBodies.Contains(target.Body)) { return false; }
            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
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
                if (limb.IsSevered) { return true; }
                if (limb.character == null || limb.character.Removed) { return false; }

                limb.character.LastDamageSource = item;
                if (Attack != null) { attackResult = Attack.DoDamageToLimb(User, limb, item.WorldPosition, 1.0f); }
                if (limb.character != null) { character = limb.character; }
            }
            else if (target.Body.UserData is Item targetItem)
            {
                if (targetItem.Removed) { return false; }
                if (Attack != null && targetItem.Prefab.DamagedByProjectiles && targetItem.Condition > 0) 
                {
                    attackResult = Attack.DoDamage(User, targetItem, item.WorldPosition, 1.0f); 
                }
            }
            else if (target.Body.UserData is IDamageable damageable)
            {
                if (Attack != null) { attackResult = Attack.DoDamage(User, damageable, item.WorldPosition, 1.0f); }
            }

            if (character != null) { character.LastDamageSource = item; }

#if CLIENT
            PlaySound(ActionType.OnUse, user: user);
            PlaySound(ActionType.OnImpact, user: user);
#endif

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (target.Body.UserData is Limb targetLimb)
                {
                    ApplyStatusEffects(ActionType.OnUse, 1.0f, character, targetLimb, user: user);
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f, character, targetLimb, user: user);
                    var attack = targetLimb.attack;
                    if (attack != null)
                    {
                        // Apply the status effects defined in the limb's attack that was hit
                        foreach (var effect in attack.StatusEffects)
                        {
                            if (effect.type == ActionType.OnImpact)
                            {
                                //effect.Apply(effect.type, 1.0f, targetLimb.character, targetLimb.character, targetLimb.WorldPosition);

                                if (effect.HasTargetType(StatusEffect.TargetType.This))
                                {
                                    effect.Apply(effect.type, 1.0f, targetLimb.character, targetLimb.character, targetLimb.WorldPosition);
                                }
                                if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                                    effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                                {
                                    var targets = new List<ISerializableEntity>();
                                    effect.GetNearbyTargets(targetLimb.WorldPosition, targets);
                                    effect.Apply(ActionType.OnActive, 1.0f, targetLimb.character, targets);
                                }

                            }
                        }
                    }
#if SERVER
                    if (GameMain.NetworkMember.IsServer)
                    {
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, this, targetLimb.character.ID, targetLimb, (ushort)0, item.WorldPosition });
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnImpact, this, targetLimb.character.ID, targetLimb, (ushort)0, item.WorldPosition });
                    }
#endif
                }
                else
                {
                    ApplyStatusEffects(ActionType.OnUse, 1.0f, useTarget: target.Body.UserData as Entity, user: user);
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f, useTarget: target.Body.UserData as Entity, user: user);
#if SERVER
                    if (GameMain.NetworkMember.IsServer)
                    {
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnUse, this, (ushort)0, null, (target.Body.UserData as Entity)?.ID ?? 0, item.WorldPosition });
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnImpact, this, (ushort)0, null, (target.Body.UserData as Entity)?.ID ?? 0, item.WorldPosition });
                    }
#endif
                }
            }

            target.Body.ApplyLinearImpulse(velocity * item.body.Mass);

            if (hits.Count() >= MaxTargetsToHit || hits.LastOrDefault()?.UserData is VoronoiCell)
            {
                Deactivate();
            }

            if (attackResult.AppliedDamageModifiers != null &&
                attackResult.AppliedDamageModifiers.Any(dm => dm.DeflectProjectiles))
            {
                item.body.LinearVelocity *= 0.1f;
            }
            else if (Vector2.Dot(velocity, collisionNormal) < 0.0f && hits.Count() >= MaxTargetsToHit &&
                        target.Body.Mass > item.body.Mass * 0.5f &&
                        (DoesStick ||
                        (StickToCharacters && target.Body.UserData is Limb) ||
                        (StickToStructures && target.Body.UserData is Structure) ||
                        (StickToItems && target.Body.UserData is Item)))                
            {
                Vector2 dir = new Vector2(
                    (float)Math.Cos(item.body.Rotation),
                    (float)Math.Sin(item.body.Rotation));

                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    if (target.Body.UserData is Structure structure && structure.Submarine != item.Submarine && structure.Submarine != null)
                    {
                        StickToTarget(structure.Submarine.PhysicsBody.FarseerBody, dir);
                    }
                    else
                    {
                        StickToTarget(target.Body, dir);
                    }   
                }
#if SERVER
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    item.CreateServerEvent(this);
                }
#endif
                item.body.LinearVelocity *= 0.5f;

                return Hitscan;                
            }
            else
            {
                item.body.LinearVelocity *= 0.5f;
            }

            var containedItems = item.OwnInventory?.Items;
            if (containedItems != null)
            {
                foreach (Item contained in containedItems)
                {
                    if (contained == null) { continue; }
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

        private void Deactivate()
        {
            item.body.FarseerBody.OnCollision -= OnProjectileCollision;
            if ((item.Prefab.DamagedByProjectiles || item.Prefab.DamagedByMeleeWeapons) && item.Condition > 0)
            {
                item.body.CollisionCategories = Physics.CollisionCharacter;
                item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform | Physics.CollisionProjectile;
            }
            else
            {
                item.body.CollisionCategories = Physics.CollisionItem;
                item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;
            }
            IgnoredBodies.Clear();
        }

        private void StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) { return; }

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, true)
            {
                MotorEnabled = true,
                MaxMotorForce = 30.0f,
                LimitEnabled = true
            };

            if (StickPermanently)
            {
                stickJoint.LowerLimit = stickJoint.UpperLimit = 0.0f;
                item.body.ResetDynamics();
            }
            else if (item.Sprite != null)
            {
                stickJoint.LowerLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X * -0.3f * item.Scale);
                stickJoint.UpperLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X * 0.3f * item.Scale);
            }

            persistentStickJointTimer = PersistentStickJointDuration;
            StickTarget = targetBody;
            GameMain.World.Add(stickJoint);

            IsActive = true;
        }

        private void Unstick()
        {
            StickTarget = null;
            if (stickJoint != null)
            {
                if (GameMain.World.JointList.Contains(stickJoint))
                {
                    GameMain.World.Remove(stickJoint);
                }
                stickJoint = null;
            }
            if (!item.body.FarseerBody.IsBullet) { IsActive = false; }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            if (stickJoint != null)
            {
                try
                {
                    GameMain.World.Remove(stickJoint);
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
