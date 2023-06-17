using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent, IServerSerializable
    {
        private ISpatialEntity source;
        private Item target;

        private Vector2? launchDir;

        private void SetSource(ISpatialEntity source)
        {
            this.source = source;
            if (source is Limb sourceLimb)
            {
                sourceLimb.AttachedRope = this;
                float offset = sourceLimb.Params.GetSpriteOrientation() - MathHelper.PiOver2;
                launchDir = VectorExtensions.Forward(sourceLimb.body.TransformedRotation - offset * sourceLimb.character.AnimController.Dir);
            }
        }

        private void ResetSource()
        {
            if (source is Limb sourceLimb && sourceLimb.AttachedRope == this)
            {
                sourceLimb.AttachedRope = null;
            }
            source = null;
        }

        private float snapTimer;

        [Serialize(1.0f, IsPropertySaveable.No, description: "")]
        public float SnapAnimDuration
        {
            get;
            set;
        }

        private float raycastTimer;
        private const float RayCastInterval = 0.2f;

        [Serialize(0.0f, IsPropertySaveable.No, description: "How much force is applied to pull the projectile the rope is attached to.")]
        public float ProjectilePullForce
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How much force is applied to pull the target the rope is attached to.")]
        public float TargetPullForce
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.No, description: "How much force is applied to pull the source the rope is attached to.")]
        public float SourcePullForce
        {
            get;
            set;
        }

        [Serialize(1000.0f, IsPropertySaveable.No, description: "How far the source item can be from the projectile until the rope breaks.")]
        public float MaxLength
        {
            get;
            set;
        }

        [Serialize(360.0f, IsPropertySaveable.No, description: "The maximum angle from the source to the target until the rope breaks.")]
        public float MaxAngle
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Should the rope snap when it collides with a structure/submarine (if not, it will just go through it).")]
        public bool SnapOnCollision
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Should the rope snap when the character drops the aim?")]
        public bool SnapWhenNotAimed
        {
            get;
            set;
        }

        [Serialize(30.0f, IsPropertySaveable.No, description: "How much mass is required for the target to pull the source towards it. Static and kinematic targets are always treated heavy enough.")]
        public float TargetMinMass
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool LerpForces
        {
            get;
            set;
        }

        private bool snapped;
        public bool Snapped
        {
            get { return snapped; }
            set
            {
                if (snapped == value) { return; }
                if (GameMain.NetworkMember != null)
                {
                    if (GameMain.NetworkMember.IsClient)
                    {
                        return;
                    }
                    else
                    {
#if SERVER
                        item.CreateServerEvent(this);
#endif
                    }
                }
                snapped = value;
                if (!snapped)
                {
                    snapTimer = 0;
                }
            }
        }

        public Rope(Item item, ContentXElement element) : base(item, element)
        {
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(ContentXElement element);

        public void Snap() => Snapped = true;
        
        public void Attach(ISpatialEntity source, Item target)
        {
            System.Diagnostics.Debug.Assert(source != null);
            System.Diagnostics.Debug.Assert(target != null);
            this.target = target;
            SetSource(source);
            Snapped = false;
            ApplyStatusEffects(ActionType.OnUse, 1.0f, worldPosition: item.WorldPosition);
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            var user = item.GetComponent<Projectile>()?.User;
            if (source == null || target == null || target.Removed ||
                (source is Entity sourceEntity && sourceEntity.Removed) ||
                (source is Limb limb && limb.Removed) ||
                (user != null && user.Removed))
            {
                ResetSource();
                target = null;
                IsActive = false;
                return;
            }

            if (Snapped)
            {
                snapTimer += deltaTime;
                if (snapTimer >= SnapAnimDuration)
                {
                    IsActive = false;
                }
                return;
            }

            Vector2 diff = target.WorldPosition - source.WorldPosition;
            float lengthSqr = diff.LengthSquared();
            if (lengthSqr > MaxLength * MaxLength)
            {
                Snap();
                return;
            }

            if (MaxAngle < 180 && lengthSqr > 2500)
            {
                if (launchDir == null)
                {
                    launchDir = diff;
                }
                float angle = MathHelper.ToDegrees(VectorExtensions.Angle(launchDir.Value, diff));
                if (angle > MaxAngle)
                {
                    Snap();
                    return;
                }
            }

#if CLIENT
            item.ResetCachedVisibleSize();
#endif
            var projectile = target.GetComponent<Projectile>();
            if (projectile == null) { return; }

            if (SnapOnCollision)
            {
                raycastTimer += deltaTime;
                if (raycastTimer > RayCastInterval)
                {
                    if (Submarine.PickBody(ConvertUnits.ToSimUnits(source.WorldPosition), ConvertUnits.ToSimUnits(target.WorldPosition),
                        collisionCategory: Physics.CollisionLevel | Physics.CollisionWall,
                        customPredicate: (Fixture f) =>
                        {
                            foreach (Body body in projectile.Hits)
                            {
                                Submarine alreadyHitSub = null;
                                if (body.UserData is Structure hitStructure)
                                {
                                    alreadyHitSub = hitStructure.Submarine;
                                }
                                else if (body.UserData is Submarine hitSub)
                                {
                                    alreadyHitSub = hitSub;
                                }
                                if (alreadyHitSub != null)
                                {
                                    if (f.Body?.UserData is MapEntity me && me.Submarine == alreadyHitSub) { return false; }
                                    if (f.Body?.UserData as Submarine == alreadyHitSub) { return false; }
                                }
                            }
                            Submarine targetSub = projectile.StickTarget?.UserData as Submarine ?? target.Submarine;

                            if (f.Body?.UserData is MapEntity mapEntity && mapEntity.Submarine != null)
                            {
                                if (mapEntity.Submarine == targetSub || mapEntity.Submarine == source.Submarine)
                                {
                                    return false;
                                }
                            }
                            else if (f.Body?.UserData is Submarine sub)
                            {
                                if (sub == targetSub || sub == source.Submarine)
                                {
                                    return false;
                                }
                            }
                            return true;
                        }) != null)
                    {
                        Snap();
                        return;
                    }
                    raycastTimer = 0.0f;
                }
            }

            Vector2 forceDir = diff;
            float distance = diff.Length();
            if (distance > 0.001f)
            {
                forceDir = Vector2.Normalize(forceDir);
            }

            if (Math.Abs(ProjectilePullForce) > 0.001f)
            {
                projectile.Item?.body?.ApplyForce(-forceDir * ProjectilePullForce);                
            }

            if (projectile.StickTarget != null)
            {
                float targetMass = float.MaxValue;
                Character targetCharacter = null;
                if (projectile.StickTarget.UserData is Limb targetLimb)
                {
                    targetCharacter = targetLimb.character;
                    targetMass = targetLimb.ragdoll.Mass;
                }
                else if (projectile.StickTarget.UserData is Character character)
                {
                    targetCharacter = character;
                    targetMass = character.Mass;
                }
                else if (projectile.StickTarget.UserData is Item item)
                {
                    targetMass = projectile.StickTarget.Mass;
                }
                if (projectile.StickTarget.BodyType != BodyType.Dynamic)
                {
                    targetMass = float.MaxValue;
                }
                if (targetMass > TargetMinMass)
                {
                    if (Math.Abs(SourcePullForce) > 0.001f)
                    {
                        var sourceBody = GetBodyToPull(source);
                        if (sourceBody != null)
                        {
                            if (user != null && user.InWater)
                            {
                                if (user.IsRagdolled)
                                {
                                    // Reel in towards the target.
                                    user.AnimController.Hang();
                                    float force = LerpForces ? MathHelper.Lerp(0, SourcePullForce, MathUtils.InverseLerp(0, MaxLength / 2, distance)) : SourcePullForce;
                                    sourceBody.ApplyForce(forceDir * force);
                                }
                                // Take the target velocity into account.
                                if (targetCharacter != null)
                                {
                                    var myCollider = user.AnimController.Collider;
                                    var targetCollider = targetCharacter.AnimController.Collider;
                                    if (myCollider.LinearVelocity != Vector2.Zero && targetCollider.LinearVelocity != Vector2.Zero)
                                    {
                                        if (Vector2.Dot(Vector2.Normalize(myCollider.LinearVelocity), Vector2.Normalize(targetCollider.LinearVelocity)) < 0)
                                        {
                                            myCollider.ApplyForce(targetCollider.LinearVelocity * targetCollider.Mass);
                                        }
                                    }
                                }
                                else
                                {
                                    var targetBody = GetBodyToPull(target);
                                    if (targetBody != null)
                                    {
                                        sourceBody.ApplyForce(targetBody.LinearVelocity * sourceBody.Mass);
                                    }
                                }
                            }
                        }
                    }
                }
                if (Math.Abs(TargetPullForce) > 0.001f)
                {
                    var targetBody = GetBodyToPull(target);
                    bool lerpForces = LerpForces;
                    if (!lerpForces && user != null && targetCharacter != null && !user.AnimController.InWater)
                    {
                        if ((forceDir.X < 0) != (user.AnimController.Dir < 0))
                        {
                            // Prevents rubberbanding horizontally when dragging a corpse.
                            lerpForces = true;
                        }
                    }
                    float force = lerpForces ? MathHelper.Lerp(0, TargetPullForce, MathUtils.InverseLerp(0, MaxLength / 3, distance - 50)) : TargetPullForce;
                    targetBody?.ApplyForce(-forceDir * force);
                    var targetRagdoll = targetCharacter?.AnimController;
                    if (targetRagdoll?.Collider != null && (targetRagdoll.InWater || targetRagdoll.OnGround))
                    {
                        targetRagdoll.Collider.ApplyForce(-forceDir * force * 3);
                    }
                }
            }
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            base.UpdateBroken(deltaTime, cam); 
            if (Snapped)
            {
                snapTimer += deltaTime;
                if (snapTimer >= SnapAnimDuration)
                {
                    IsActive = false;
                }
            }
        }

        private PhysicsBody GetBodyToPull(ISpatialEntity target)
        {
            if (target is Item targetItem)
            {
                if (targetItem.ParentInventory is CharacterInventory characterInventory &&
                    characterInventory.Owner is Character ownerCharacter)
                {
                    if (ownerCharacter.Removed) { return null; }
                    return ownerCharacter.AnimController.Collider;
                }
                var projectile = targetItem.GetComponent<Projectile>();
                if (projectile != null && projectile.StickTarget != null)
                {
                    if (projectile.StickTarget.UserData is Structure structure)
                    {
                        return structure.Submarine?.PhysicsBody;
                    }
                    else if (projectile.StickTarget.UserData is Submarine sub)
                    {
                        return sub.PhysicsBody;
                    }
                    else if (projectile.StickTarget.UserData is Item item)
                    {
                        return item.body;
                    }
                    else if (projectile.StickTarget.UserData is Limb limb)
                    {
                        return limb.body;
                    }
                    return null;
                }
                if (targetItem.body != null) { return targetItem.body; }
            }
            else if (target is Limb targetLimb)
            {
                return targetLimb.body;
            }

            return null;
        }
    }
}
