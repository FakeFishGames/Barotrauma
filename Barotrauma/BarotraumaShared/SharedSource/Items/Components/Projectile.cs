﻿using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Projectile : ItemComponent, IServerSerializable
    {
        private static readonly ImmutableArray<float> spreadPool;
        static Projectile()
        {
            MTRandom random = new MTRandom(0);
            spreadPool = Enumerable.Range(0, byte.MaxValue + 1).Select(f => (float)random.NextDouble() - 0.5f).ToImmutableArray();            
        }

        public static byte SpreadCounter { get; private set; }

        public static void ResetSpreadCounter()
        {
            SpreadCounter = 0;
        }

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

        public const float WaterDragCoefficient = 0.1f;

        private readonly Queue<Impact> impactQueue = new Queue<Impact>();

        private bool removePending;

        private byte spreadIndex;

        //continuous collision detection is used while the projectile is moving faster than this
        const float ContinuousCollisionThreshold = 5.0f;

        private Joint stickJoint;
        private Vector2 jointAxis;

        public Attack Attack { get; private set; }

        private Vector2 launchPos;

        private readonly HashSet<Body> hits = new HashSet<Body>();

        public List<Body> IgnoredBodies;

        /// <summary>
        /// The item that launched this projectile (if any)
        /// </summary>
        public Item Launcher;

        private Character stickTargetCharacter;

        private Character _user;
        public Character User
        {
            get { return _user; }
            set
            {
                _user = value;
                Attack?.SetUser(_user);                
            }
        }

        public Character Attacker { get; set; }

        public IEnumerable<Body> Hits
        {
            get { return hits; }
        }

        [Serialize(10.0f, IsPropertySaveable.No, description: "The impulse applied to the physics body of the item when it's launched. Higher values make the projectile faster.")]
        public float LaunchImpulse { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The random percentage modifier used to add variance to the launch impulse.")]
        public float ImpulseSpread { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No, description: "The rotation of the item relative to the rotation of the weapon when launched (in degrees).")]

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

        [Serialize(false, IsPropertySaveable.No, description: "When set to true, the item can stick to any target it hits.")]
        //backwards compatibility, can stick to anything
        public bool DoesStick
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the projectile stick to characters.")]
        public bool StickToCharacters
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the projectile stick to walls.")]
        public bool StickToStructures
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the projectile stick to items.")]
        public bool StickToItems
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the projectile stick to doors. Caution: may cause issues.")]
        public bool StickToDoors
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Can the item stick even to deflective targets.")]
        public bool StickToDeflective
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "")]
        public bool StickToLightTargets
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Hitscan projectiles cast a ray forwards and immediately hit whatever the ray hits. "+
                                                              "It is recommended to use hitscans for very fast-moving projectiles such as bullets, because using extremely fast launch velocities may cause physics glitches.")]
        public bool Hitscan
        {
            get;
            set;
        }

        [Serialize(1, IsPropertySaveable.No, description: "How many hitscans should be done when the projectile is launched. "
            + "Multiple hitscans can be used to simulate weapons that fire multiple projectiles at the same time" +
            " without having to actually use multiple projectile items, for example shotguns.")]
        public int HitScanCount
        {
            get;
            set;
        }

        [Serialize(1, IsPropertySaveable.No, description: "How many targets the projectile can hit before it stops.")]
        public int MaxTargetsToHit
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Should the item be deleted when it hits something.")]
        public bool RemoveOnHit
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "Random spread applied to the launch angle of the projectile (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "Override random spread with static spread; projectiles are launched with an equal amount of angle between them. Only applies when firing multiple projectiles.")]
        public bool StaticSpread
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No)]
        public bool FriendlyFire
        {
            get;
            set;
        }

        private float deactivationTimer;

        [Serialize(0f, IsPropertySaveable.No)]
        public float DeactivationTime
        {
            get;
            set;
        }

        private float stickTimer;
        [Serialize(0f, IsPropertySaveable.No)]
        public float StickDuration
        {
            get;
            set;
        }

        [Serialize(-1f, IsPropertySaveable.No)]
        public float MaxJointTranslation
        {
            get;
            set;
        }
        private float maxJointTranslationInSimUnits = -1;

        [Serialize(true, IsPropertySaveable.No)]
        public bool Prismatic
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description:"Enable only if you want to make the projectile ignore collisions with other projectiles when it's shot. Doesn't have any effect, if the item is not set to be damaged by projectiles.")]
        public bool IgnoreProjectilesWhileActive
        {
            get;
            set;
        }

        public Body StickTarget 
        { 
            get; 
            private set; 
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DamageDoors
        {
            get;
            set;
        }

        public bool IsStuckToTarget => StickTarget != null;

        private Category originalCollisionCategories;
        private Category originalCollisionTargets;

        public Projectile(Item item, ContentXElement element)
            : base (item, element)
        {
            IgnoredBodies = new List<Body>();

            foreach (var subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("attack", StringComparison.OrdinalIgnoreCase)) { continue; }
                Attack = new Attack(subElement, item.Name + ", Projectile", item);
            }

            if (item.body == null)
            {
                DebugConsole.ThrowError($"Error in projectile definition ({item.Name}): No body defined!");
                return;
            }

            spreadIndex = SpreadCounter;
            SpreadCounter++;

            InitProjSpecific(element);
        }
        partial void InitProjSpecific(ContentXElement element);

        public override void OnItemLoaded()
        {
            if (item.body == null) { return; }
            if (Attack != null && Attack.DamageRange <= 0.0f)
            {
                switch (item.body.BodyShape)
                {
                    case PhysicsBody.Shape.Circle:
                        Attack.DamageRange = item.body.Radius;
                        break;
                    case PhysicsBody.Shape.Capsule:
                        Attack.DamageRange = item.body.Height / 2 + item.body.Radius;
                        break;
                    case PhysicsBody.Shape.Rectangle:
                        Attack.DamageRange = new Vector2(item.body.Width / 2.0f, item.body.Height / 2.0f).Length();
                        break;
                }
                Attack.DamageRange = ConvertUnits.ToDisplayUnits(Attack.DamageRange);
            }
            originalCollisionCategories = item.body.CollisionCategories;
            originalCollisionTargets = item.body.CollidesWith;
        }

        public float GetSpreadFromPool()
        {
            spreadIndex = (byte)MathUtils.PositiveModulo(spreadIndex, spreadPool.Length);
            return spreadPool[spreadIndex];
        }

        private void Launch(Character user, Vector2 simPosition, float rotation, float damageMultiplier = 1f, float launchImpulseModifier = 0f)
        {
            if (Item.body == null) { return; }
            Item.body.ResetDynamics();
            Item.SetTransform(simPosition, rotation);
            if (Attack != null)
            {
                Attack.DamageMultiplier = damageMultiplier;
            }
            // Set user for hitscan projectiles to work properly.
            User = user;
            // Need to set null for non-characterusable items.
            Use(character: null, launchImpulseModifier);
            // Set user for normal projectiles to work properly.
            User = user;
            if (Item.Removed) { return; }
            launchPos = simPosition;
            //set the rotation of the projectile again because dropping the projectile resets the rotation
            Item.SetTransform(simPosition, rotation + (Item.body.Dir * LaunchRotationRadians));
            if (DeactivationTime > 0)
            {
                deactivationTimer = DeactivationTime;
            }
        }

        public void Shoot(Character user, Vector2 weaponPos, Vector2 spawnPos, float rotation, List<Body> ignoredBodies, bool createNetworkEvent, float damageMultiplier = 1f, float launchImpulseModifier = 0f)
        {
            //add the limbs of the shooter to the list of bodies to be ignored
            //so that the player can't shoot himself
            IgnoredBodies = ignoredBodies;
            Vector2 projectilePos = weaponPos;
            //make sure there's no obstacles between the base of the weapon (or the shoulder of the character) and the end of the barrel
            if (Submarine.PickBody(weaponPos, spawnPos, IgnoredBodies, Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking, 
                customPredicate: (Fixture f) =>  { return IgnoredBodies == null || !IgnoredBodies.Contains(f.Body); }) == null)
            {
                //no obstacles -> we can spawn the projectile at the barrel
                projectilePos = spawnPos;
            }
            else if ((weaponPos - spawnPos).LengthSquared() > 0.0001f)
            {
                //spawn the projectile body.GetMaxExtent() away from the position where the raycast hit the obstacle
                Vector2 newPos = weaponPos - Vector2.Normalize(spawnPos - projectilePos) * Math.Max(Item.body.GetMaxExtent(), 0.1f);
                if (MathUtils.IsValid(newPos))
                {
                    projectilePos = newPos;
                }
            }
            Launch(user, projectilePos, rotation, damageMultiplier, launchImpulseModifier);
            if (createNetworkEvent && !Item.Removed && GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
#if SERVER
                launchRot = rotation;
                Item.CreateServerEvent(this, new EventData(launch: true, spreadCounter: (byte)(spreadIndex - 1)));
#endif
            }
        }

        public bool Use(Character character = null, float launchImpulseModifier = 0f)
        {
            if (character != null && !characterUsable) { return false; }
            if (item.body == null) { return false; }
            //can't launch if already launched
            if (StickTarget != null || IsActive) { return false; }

            float initialRotation = item.body.Rotation;
            for (int i = 0; i < HitScanCount; i++)
            {
                float launchAngle;
                if (StaticSpread)
                {
                    launchAngle = initialRotation + MathHelper.ToRadians(i - ((float)(HitScanCount - 1) / 2)) * Spread;
                }
                else
                {
                    launchAngle = initialRotation + MathHelper.ToRadians(Spread * GetSpreadFromPool());
                }
                spreadIndex++;

                Vector2 launchDir = new Vector2((float)Math.Cos(launchAngle), (float)Math.Sin(launchAngle));
                if (Hitscan)
                {
                    Vector2 prevSimpos = item.SimPosition;
                    item.body.SetTransformIgnoreContacts(item.body.SimPosition, launchAngle);
                    DoHitscan(launchDir);
                    if (i < HitScanCount - 1)
                    {
                        item.SetTransform(prevSimpos, item.body.Rotation);
                    }
                }
                else
                {
                    item.body.SetTransform(item.body.SimPosition, launchAngle);
                    float modifiedLaunchImpulse = (LaunchImpulse + launchImpulseModifier) * (1 + Rand.Range(-ImpulseSpread, ImpulseSpread));
                    DoLaunch(launchDir * modifiedLaunchImpulse);
                }
            }
            User = character;
            ApplyStatusEffects(ActionType.OnUse, 1.0f, User, user: User);
            return true;
        }

        public override bool Use(float deltaTime, Character character = null) => Use(character);

        private void DoLaunch(Vector2 impulse)
        {
            hits.Clear();

            if (item.AiTarget != null)
            {
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
            }

            item.Drop(null, createNetworkEvent: false);
            Item.WaterDragCoefficient = WaterDragCoefficient;

            launchPos = item.SimPosition;

            item.body.Enabled = true;            
            if (item.body.BodyType == BodyType.Kinematic)
            {
                item.body.LinearVelocity = impulse;
            }
            else
            {
                impulse *= item.body.Mass;
                item.body.ApplyLinearImpulse(impulse, maxVelocity: NetConfig.MaxPhysicsBodyVelocity * 0.95f);
            }
            
            item.body.FarseerBody.OnCollision += OnProjectileCollision;
            item.body.FarseerBody.IsBullet = true;

            EnableProjectileCollisions();

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
            Vector2 rayStartWorld = item.WorldPosition;
            item.Drop(null);
            Item.WaterDragCoefficient = WaterDragCoefficient;

            item.body.Enabled = true;
            //set the velocity of the body because the OnProjectileCollision method
            //uses it to determine the direction from which the projectile hit
            item.body.LinearVelocity = dir;
            IsActive = true;

            Vector2 rayStart = simPositon;
            Vector2 rayEnd = rayStart + dir * 500.0f;

            float worldDist = 1000.0f;
#if CLIENT
            worldDist = Screen.Selected?.Cam?.WorldView.Width ?? GameMain.GraphicsWidth;
#endif
            Vector2 rayEndWorld = rayStartWorld + dir * worldDist;

            List<HitscanResult> hits = new List<HitscanResult>();
            hits.AddRange(DoRayCast(rayStart, rayEnd, submarine: item.Submarine));

            if (item.Submarine != null)
            {
                //shooting indoors, do a hitscan outside as well
                hits.AddRange(DoRayCast(rayStart + item.Submarine.SimPosition, rayEnd + item.Submarine.SimPosition, submarine: null));
                //do a hitscan in other subs' coordinate spaces
                RayCastInOtherSubs(rayStart + item.Submarine.SimPosition, rayEnd + item.Submarine.SimPosition);
            }
            else
            {
                RayCastInOtherSubs(rayStart, rayEnd);
            }

            void RayCastInOtherSubs(Vector2 rayStart, Vector2 rayEnd)
            {
                //shooting outdoors, see if we can hit anything inside a sub
                foreach (Submarine submarine in Submarine.Loaded)
                {
                    if (submarine == item.Submarine) { continue; }
                    var inSubHits = DoRayCast(rayStart - submarine.SimPosition, rayEnd - submarine.SimPosition, submarine);
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

            int hitCount = 0;
            Vector2 lastHitPos = item.WorldPosition;
            hits = hits.OrderBy(h => h.Fraction).ToList();
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                item.SetTransform(h.Point, rotation);
                item.UpdateTransform();
                if (HandleProjectileCollision(h.Fixture, h.Normal, Vector2.Zero))
                {
                    hitCount++;
                    if (hitCount >= MaxTargetsToHit || i == hits.Count - 1)
                    {
                        LaunchProjSpecific(rayStartWorld, item.WorldPosition);
                        break;
                    }
                }
            }
            //the raycast didn't hit anything (or didn't hit enough targets to stop the projectile) -> the projectile flew somewhere outside the level and is permanently lost
            if (hitCount < MaxTargetsToHit)
            {
                item.body.SetTransformIgnoreContacts(item.body.SimPosition, rotation);
                LaunchProjSpecific(rayStartWorld, rayEndWorld);
                if (Entity.Spawner == null)
                {
                    item.Remove();
                }
                else
                {
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                    {
                        //clients aren't allowed to remove items by themselves, so lets hide the projectile until the server tells us to remove it
                        item.HiddenInGame = Hitscan;
                    }
                    else
                    {
                        Entity.Spawner.AddItemToRemoveQueue(item);
                    }
                }
            }
        }
        
        private List<HitscanResult> DoRayCast(Vector2 rayStart, Vector2 rayEnd, Submarine submarine)
        {
            List<HitscanResult> hits = new List<HitscanResult>();

            Vector2 dir = rayEnd - rayStart;
            dir = dir.LengthSquared() < 0.00001f ? Vector2.UnitY : Vector2.Normalize(dir);

            //do an AABB query first to see if the start of the ray is inside a fixture
            var aabb = new FarseerPhysics.Collision.AABB(rayStart - Vector2.One * 0.001f, rayStart + Vector2.One * 0.001f);
            GameMain.World.QueryAABB((fixture) =>
            {
                if (fixture?.Body.UserData is LevelObject levelObj)
                {
                    if (!levelObj.Prefab.TakeLevelWallDamage) { return true; }
                }
                else if (fixture?.Body == null || fixture.IsSensor) 
                { 
                    //ignore sensors
                    return true; 
                }
                if (fixture.Body.UserData is VineTile) { return true; }
                if (fixture.CollidesWith == Category.None) { return true; }

                if (fixture.Body.UserData as string == "ruinroom" || fixture.Body.UserData is Hull || fixture.UserData is Hull) { return true; }

                //if doing the raycast in a submarine's coordinate space, ignore anything that's not in that sub
                if (submarine != null)
                {
                    if (fixture.Body.UserData is VoronoiCell) { return true; }
                    if (fixture.Body.UserData is Entity entity && entity.Submarine != submarine) { return true; }
                }

                if (fixture.Body.UserData is VoronoiCell && (this.item.Submarine != null || submarine != null)) { return true; }

                if (fixture.Body.UserData is Item item)
                {
                    if (item == Item) { return true; }
                    if (item.Condition <= 0) { return true; }
                    if (!item.Prefab.DamagedByProjectiles && item.GetComponent<Door>() == null) { return true; }
                }
                else if (fixture.Body.UserData is Holdable { CanPush: false })
                {
                    // Ignore holdables that can't push -> shouldn't block
                    return true;
                }
                else
                {
                    // TODO: This might make us ignore something we don't want to ignore?
                    // Not item -> ignore everything else than characters, sub walls and level walls
                    if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                        !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                        !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) { return true; }
                }

                fixture.Body.GetTransform(out FarseerPhysics.Common.Transform transform);
                if (!fixture.Shape.TestPoint(ref transform, ref rayStart)) { return true; }

                hits.Add(new HitscanResult(fixture, rayStart, -dir, 0.0f));
                return true;
            }, ref aabb);

            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                //ignore sensors and items
                if (fixture?.Body.UserData is LevelObject levelObj)
                {
                    if (!levelObj.Prefab.TakeLevelWallDamage) { return -1; }
                }
                else if (fixture?.Body == null || fixture.IsSensor)
                {
                    //ignore sensors
                    return -1;
                }
                if (fixture.Body.UserData is VineTile) { return -1; }
                if (fixture.CollidesWith == Category.None) { return -1; }
                if (fixture.Body.UserData is Item item)
                {
                    if (item.Condition <= 0) { return -1; }
                    if (!item.Prefab.DamagedByProjectiles && item.GetComponent<Door>() == null) { return -1; }
                }
                if (fixture.Body.UserData as string == "ruinroom" || fixture.Body?.UserData is Hull || fixture.UserData is Hull) { return -1; }

                //if doing the raycast in a submarine's coordinate space, ignore anything that's not in that sub
                if (submarine != null)
                {
                    if (fixture.Body.UserData is VoronoiCell) { return -1; }
                    if (fixture.Body.UserData is Entity entity && entity.Submarine != submarine) { return -1; }
                    if (fixture.Body.UserData is Limb limb && limb.character?.Submarine != submarine) { return -1; }
                }

                // Ignore holdables that can't push -> shouldn't block
                if (fixture.Body.UserData is Holdable { CanPush: false })
                {
                    return -1;
                }

                //ignore level cells if the item and the point of impact are inside a sub
                if (fixture.Body.UserData is VoronoiCell) 
                { 
                    if (Hull.FindHull(ConvertUnits.ToDisplayUnits(point), this.item.CurrentHull) != null && this.item.Submarine != null)
                    {
                        return -1;
                    }
                }

                if (hits.Count > 50)
                {
                    float furthestHit = 0.0f;
                    int furthestHitIndex = -1;
                    for (int i = 0; i < hits.Count; i++)
                    {
                        if (hits[i].Fraction > furthestHit)
                        {
                            furthestHitIndex = i;
                            furthestHit = hits[i].Fraction;
                        }
                    }
                    if (furthestHitIndex > -1)
                    {
                        hits.RemoveAt(furthestHitIndex);
                    }
                }

                hits.Add(new HitscanResult(fixture, point, normal, fraction));

                return 1;
            }, rayStart, rayEnd, Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking | Physics.CollisionProjectile);

            return hits;
        }

        public override void Drop(Character dropper, bool setTransform = true)
        {
            Item.ResetWaterDragCoefficient();
            if (dropper != null)
            {
                DisableProjectileCollisions();
                Unstick();
            }
            base.Drop(dropper, setTransform);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (DeactivationTime > 0)
            {
                deactivationTimer -= deltaTime;
                if (deactivationTimer < 0)
                {
                    DisableProjectileCollisions();
                }
            }
            while (impactQueue.Count > 0)
            {
                var impact = impactQueue.Dequeue();
                HandleProjectileCollision(impact.Fixture, impact.Normal, impact.LinearVelocity);
            }

            if (!removePending)
            {
                Entity useTarget = lastTarget?.Body.UserData is Limb limb ? limb.character : lastTarget?.Body.UserData as Entity;
                ApplyStatusEffects(ActionType.OnActive, deltaTime, useTarget: useTarget, user: _user);
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
                if (DeactivationTime > 0 && deactivationTimer > 0)
                {
                    DisableProjectileCollisions();
                }
            }

            if (stickJoint == null) { return; }

            if (StickDuration > 0 && stickTimer > 0)
            {
                stickTimer -= deltaTime;
                return;
            }

            float absoluteMaxTranslation = 100;
            // Update the item's transform to make sure it's inside the same sub as the target (or outside)
            if (StickTarget?.UserData is Limb target && target.Submarine != item.Submarine || stickJoint is PrismaticJoint prismaticJoint && Math.Abs(prismaticJoint.JointTranslation) > absoluteMaxTranslation)
            {
                item.UpdateTransform();
            }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (StickTargetRemoved() || stickJoint is PrismaticJoint pJoint && Math.Abs(pJoint.JointTranslation) > maxJointTranslationInSimUnits)
                {
                    Unstick();
#if SERVER
                    item.CreateServerEvent(this, new EventData(launch: false));                
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
            if (IgnoredBodies != null && IgnoredBodies.Contains(target.Body)) { return false; }
            if (originalCollisionCategories == Category.None && originalCollisionTargets == Category.None) { return false; }
            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }
            if (target.IsSensor) { return false; }
            if (hits.Contains(target.Body)) { return false; }
            if (target.Body.UserData is Submarine)
            {
                if (ShouldIgnoreSubmarineCollision(ref target, contact)) { return false; }
            }
            else if (target.Body.UserData is Limb limb)
            {
                if (limb.IsSevered)
                {
                    //push the severed limb around a bit, but let the projectile pass through it
                    limb.body?.ApplyLinearImpulse(item.body.LinearVelocity * item.body.Mass * 0.1f, item.SimPosition);
                    return false;
                }
                if (!FriendlyFire && User != null && limb.character.IsFriendly(User))
                {
                    return false;
                }
            }
            else if (target.Body.UserData is Item item)
            {
                if (item.Condition <= 0.0f) { return false; }
                if (!item.Prefab.DamagedByProjectiles)
                {
                    if (item.GetComponent<Door>() == null)
                    {
                        return false;
                    }
                }
            }
            else if (target.Body.UserData is Holdable { CanPush: false })
            {
                // Ignore holdables that can't push -> shouldn't block
                return false;
            }

            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }

            hits.Add(target.Body);
            impactQueue.Enqueue(new Impact(target, contact.Manifold.LocalNormal, item.body.LinearVelocity));
            IsActive = true;
            if (RemoveOnHit)
            {
                item.body.FarseerBody.ResetDynamics();
            }
            if (hits.Count >= MaxTargetsToHit || target.Body.UserData is VoronoiCell)
            {
                DisableProjectileCollisions();
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Should the collision with the target submarine be ignored (e.g. did the projectile collide with the wall behind the turret when being launched)
        /// </summary>
        /// <param name="target">Fixture the projectile hit</param>
        /// <param name="contact">Contact between the projectile and the target</param>
        /// <returns>True if the target isn't a submarine or if the collision happened behind the launch position of the projectile</returns>
        public bool ShouldIgnoreSubmarineCollision(Fixture target, Contact contact)
        {
            return ShouldIgnoreSubmarineCollision(ref target, contact);
        }

        private bool ShouldIgnoreSubmarineCollision(ref Fixture target, Contact contact)
        {
            //not in the projectile category: the projectile has not been launched (e.g. just dropped from an inventory)
            if (item.body.CollisionCategories != Physics.CollisionProjectile) 
            { 
                return false; 
            }
            if (target.Body.UserData is Submarine sub)
            {
                //hit an item in a different sub -> no need to ignore, we can process the impact with this info
                //(if it wasn't, we'll move the projectile to that sub's coordinate space and let it hit what it hits there)
                if (Launcher?.Submarine != sub && target.UserData is Item) 
                { 
                    return false; 
                }

                Vector2 dir = item.body.LinearVelocity.LengthSquared() < 0.001f ?
                contact.Manifold.LocalNormal : Vector2.Normalize(item.body.LinearVelocity);

                //do a raycast in the sub's coordinate space to see if it hit a structure
                var wallBody = Submarine.PickBody(
                    item.body.SimPosition - ConvertUnits.ToSimUnits(sub.Position) - dir,
                    item.body.SimPosition - ConvertUnits.ToSimUnits(sub.Position) + dir,
                    collisionCategory: Physics.CollisionWall);
                if (wallBody?.FixtureList?.First() != null && (wallBody.UserData is Structure || wallBody.UserData is Item) &&
                    //ignore the hit if it's behind the position the item was launched from, and the projectile is travelling in the opposite direction
                    Vector2.Dot(item.body.SimPosition - launchPos, dir) > 0)
                {
                    target = wallBody.FixtureList.First();
                    if (hits.Contains(target.Body))
                    {
                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }
            return false;
        }

        private readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();
        private Fixture lastTarget;

        private bool HandleProjectileCollision(Fixture target, Vector2 collisionNormal, Vector2 velocity)
        {
            if (User != null && User.Removed) { User = null; }
            if (IgnoredBodies != null && IgnoredBodies.Contains(target.Body)) { return false; }
            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }
            lastTarget = target;

            int remainingHits = Math.Max(MaxTargetsToHit - hits.Count, 0);
            float speedMultiplier = Math.Min(0.4f + remainingHits * 0.1f, 1.0f);
            float deflectedSpeedMultiplier = 0.1f;

            AttackResult attackResult = new AttackResult();
            Character character = null;
            if (target.Body.UserData is Submarine submarine && target.UserData is not Barotrauma.Item)
            {
                item.Move(-submarine.Position, ignoreContacts: false);
                item.Submarine = submarine;
                item.body.Submarine = submarine;
                return !Hitscan;
            }
            else if (target.Body.UserData is Limb limb)
            {
                if (!FriendlyFire && User != null && limb.character.IsFriendly(User))
                {
                    return false;
                }
                // when hitting limbs with piercing ammo, don't lose as much speed
                if (MaxTargetsToHit > 1)
                {
                    speedMultiplier = 1f;
                    deflectedSpeedMultiplier = 0.8f;
                }
                if (limb.IsSevered || limb.character == null || limb.character.Removed) { return false; }

                limb.character.LastDamageSource = item;
                if (Attack != null) { attackResult = Attack.DoDamageToLimb(User ?? Attacker, limb, item.WorldPosition, 1.0f); }
                if (limb.character != null) { character = limb.character; }
            }
            else if ((target.Body.UserData as Item ?? (target.Body.UserData as ItemComponent)?.Item ?? target.UserData as Item) is Item targetItem)
            {
                if (targetItem.Removed) { return false; }
                //hit the external collider of an item (turret?) of the same sub -> ignore
                if (target.UserData is Item && targetItem.Submarine != null && targetItem.Submarine == Launcher?.Submarine) { return false; }
                if (Attack != null && (targetItem.Prefab.DamagedByProjectiles || DamageDoors && targetItem.GetComponent<Door>() != null) && targetItem.Condition > 0) 
                {
                    attackResult = Attack.DoDamage(User ?? Attacker, targetItem, item.WorldPosition, 1.0f);
#if CLIENT
                    if (attackResult.Damage > 0.0f && targetItem.Prefab.ShowHealthBar)
                    {
                        Character.Controlled?.UpdateHUDProgressBar(targetItem,
                            targetItem.WorldPosition,
                            targetItem.Condition / targetItem.MaxCondition,
                            emptyColor: GUIStyle.HealthBarColorLow,
                            fullColor: GUIStyle.HealthBarColorHigh,
                            textTag: targetItem.Prefab.ShowNameInHealthBar ? targetItem.Name : string.Empty);
                    }
#endif
                }
            }
            else if (target.Body.UserData is IDamageable damageable)
            {
                if (Attack != null) 
                {
                    Vector2 pos = item.WorldPosition;
                    if (item.Submarine == null && damageable is Structure structure && structure.Submarine != null && Vector2.DistanceSquared(item.WorldPosition, structure.WorldPosition) > 10000.0f * 10000.0f)
                    {
                        item.Submarine = structure.Submarine;
                    }
                    attackResult = Attack.DoDamage(User ?? Attacker, damageable, pos, 1.0f); 
                }
            }
            else if (target.Body.UserData is VoronoiCell voronoiCell && voronoiCell.IsDestructible && Attack != null && Math.Abs(Attack.LevelWallDamage) > 0.0f)
            {
                if (Level.Loaded?.ExtraWalls.Find(w => w.Body == target.Body) is DestructibleLevelWall destructibleWall)
                {
                    attackResult = Attack.DoDamage(User ?? Attacker, destructibleWall, item.WorldPosition, 1.0f);
                }
            }

            if (character != null) { character.LastDamageSource = item; }

            ActionType conditionalActionType = ActionType.OnSuccess;
            if (User != null && Rand.Range(0.0f, 0.5f) > DegreeOfSuccess(User))
            {
                conditionalActionType = ActionType.OnFailure;
            }
#if CLIENT
            PlaySound(conditionalActionType, user: User);
            PlaySound(ActionType.OnImpact, user: User);
#endif

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (target.Body.UserData is Limb targetLimb)
                {
                    ApplyStatusEffects(conditionalActionType, 1.0f, character, targetLimb, useTarget: character, user: User);
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f, character, targetLimb, useTarget: character, user: User);
                    var attack = targetLimb.attack;
                    if (attack != null)
                    {
                        // Apply the status effects defined in the limb's attack that was hit
                        foreach (var effect in attack.StatusEffects)
                        {
                            if (effect.type == ActionType.OnImpact)
                            {
                                if (effect.HasTargetType(StatusEffect.TargetType.This))
                                {
                                    effect.Apply(effect.type, 1.0f, User, User);
                                }
                                if (effect.HasTargetType(StatusEffect.TargetType.Character) || effect.HasTargetType(StatusEffect.TargetType.UseTarget))
                                {
                                    effect.Apply(effect.type, 1.0f, targetLimb.character, targetLimb.character);
                                }
                                if (effect.HasTargetType(StatusEffect.TargetType.Limb))
                                {
                                    effect.Apply(effect.type, 1.0f, targetLimb.character, targetLimb);
                                }
                                if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                                    effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                                {
                                    targets.Clear();
                                    effect.AddNearbyTargets(targetLimb.WorldPosition, targets);
                                    effect.Apply(effect.type, 1.0f, targetLimb.character, targets);
                                }
                            }
                        }
                    }
                    if (GameMain.NetworkMember is { IsServer: true } server)
                    {
                        server.CreateEntityEvent(item, new Item.ApplyStatusEffectEventData(conditionalActionType, this, targetLimb.character, targetLimb, useTarget: targetLimb.character, item.WorldPosition));
                        server.CreateEntityEvent(item, new Item.ApplyStatusEffectEventData(ActionType.OnImpact, this, targetLimb.character, targetLimb, useTarget: targetLimb.character, item.WorldPosition));
                    }
                }
                else
                {
                    ApplyStatusEffects(conditionalActionType, 1.0f, useTarget: target.Body.UserData as Entity, user: User);
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f, useTarget: target.Body.UserData as Entity, user: User);
                    if (GameMain.NetworkMember is { IsServer: true } server)
                    {
                        server.CreateEntityEvent(item, new Item.ApplyStatusEffectEventData(conditionalActionType, this, useTarget: target.Body.UserData as Entity, worldPosition: item.WorldPosition));
                        server.CreateEntityEvent(item, new Item.ApplyStatusEffectEventData(ActionType.OnImpact, this, useTarget: target.Body.UserData as Entity, worldPosition: item.WorldPosition));
                    }
                }
            }

            target.Body.ApplyLinearImpulse(velocity * item.body.Mass);
            target.Body.LinearVelocity = target.Body.LinearVelocity.ClampLength(NetConfig.MaxPhysicsBodyVelocity * 0.5f);

            if (hits.Count >= MaxTargetsToHit || hits.LastOrDefault()?.UserData is VoronoiCell)
            {
                DisableProjectileCollisions();
            }

            if (attackResult.AppliedDamageModifiers != null && attackResult.AppliedDamageModifiers.Any(dm => dm.DeflectProjectiles) && !StickToDeflective)
            {
                item.body.LinearVelocity *= deflectedSpeedMultiplier;
            }
            else if (   remainingHits <= 0 &&
                        stickJoint == null && StickTarget == null &&
                        StickToStructures && target.Body.UserData is Structure ||
                        ((StickToLightTargets || target.Body.Mass > item.body.Mass * 0.5f) &&
                        (DoesStick ||
                        (StickToCharacters && (target.Body.UserData is Limb || target.Body.UserData is Character)) ||
                        (target.Body.UserData is Item i && (i.GetComponent<Door>() != null ? StickToDoors : StickToItems)))))
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
                    item.CreateServerEvent(this, new EventData(launch: false));
                }
#endif
                item.body.LinearVelocity *= speedMultiplier;

                return Hitscan;                
            }
            else
            {
                item.body.LinearVelocity *= speedMultiplier;
            }

            var containedItems = item.OwnInventory?.AllItems;
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
                removePending = true;
                item.HiddenInGame = true;
                item.body.FarseerBody.Enabled = false;
                Entity.Spawner?.AddItemToRemoveQueue(item);                
            }

            return true;
        }

        private void EnableProjectileCollisions()
        {
            if (item.body.CollisionCategories != Category.None)
            {
                item.body.CollisionCategories = Physics.CollisionProjectile;
                item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking;
            }
            if (item.Prefab.DamagedByProjectiles && !IgnoreProjectilesWhileActive)
            {
                if (item.body.CollisionCategories == Category.None) { item.body.CollisionCategories = Physics.CollisionCharacter; }
                item.body.CollidesWith |= Physics.CollisionProjectile;
            }
        }

        private void DisableProjectileCollisions()
        {
            if (item?.body?.FarseerBody == null) { return; }
            item.body.FarseerBody.OnCollision -= OnProjectileCollision;
            if (originalCollisionCategories != Category.None && originalCollisionTargets != Category.None)
            {
                item.body.CollisionCategories = originalCollisionCategories;
                item.body.CollidesWith = originalCollisionTargets;
            }
            else
            {
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
            }
            IgnoredBodies?.Clear();
        }

        private void StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) { return; }
            jointAxis = axis;
            item.body.ResetDynamics();
            if (Prismatic)
            {
                stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, useWorldCoordinates: true)
                {
                    MotorEnabled = true,
                    MaxMotorForce = 30.0f,
                    LimitEnabled = true,
                    Breakpoint = 1000.0f
                };

                if (maxJointTranslationInSimUnits == -1)
                {
                    if (item.Sprite != null && MaxJointTranslation < 0)
                    {
                        MaxJointTranslation = item.Sprite.size.X / 2 * item.Scale;
                    }
                    MaxJointTranslation = Math.Min(MaxJointTranslation, 1000);
                    maxJointTranslationInSimUnits = ConvertUnits.ToSimUnits(MaxJointTranslation);
                }
            }
            else
            {
                stickJoint = new WeldJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, item.body.SimPosition, useWorldCoordinates: true)
                {
                    FrequencyHz = 10.0f,
                    DampingRatio = 0.5f
                };
            }
            stickTimer = StickDuration;
            StickTarget = targetBody;
            GameMain.World.Add(stickJoint);
            IsActive = true;
            if (targetBody.UserData is Limb limb)
            {
                stickTargetCharacter = limb.character;
                stickTargetCharacter.AttachedProjectiles.Add(this);
            }
        }

        public void Unstick()
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
            if (!item.body.FarseerBody.IsBullet)
            {
                IsActive = false;
                if (DeactivationTime > 0 && deactivationTimer > 0)
                {
                    DisableProjectileCollisions();
                }
            }
            item.GetComponent<Rope>()?.Snap();
            if (stickTargetCharacter != null)
            {
                stickTargetCharacter.AttachedProjectiles.Remove(this);
                stickTargetCharacter = null;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            if (IsStuckToTarget || stickJoint != null || stickTargetCharacter != null)
            {
                Unstick();
            }
        }
        partial void LaunchProjSpecific(Vector2 startLocation, Vector2 endLocation);
    }
}
