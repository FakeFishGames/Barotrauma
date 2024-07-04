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
        private float currentRopeLength;

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
        
        [Serialize(200.0f, IsPropertySaveable.No, description: "At which distance the user stops pulling the target?")]
        public float MinPullDistance
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
        
        private bool isReelingIn;
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
                else if (target != null && source != null && target != source)
                {
#if CLIENT
                    // Play a sound at both ends. Initially tested playing the sound in the middle when the rope snaps in the middle,
                    // but I think it's more important to ensure that the players hear the sound.
                    PlaySound(snapSound, source.WorldPosition);
                    PlaySound(snapSound, target.WorldPosition);
#endif
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
            UpdateProjSpecific();
            isReelingIn = false;
            Character user = item.GetComponent<Projectile>()?.User;
            if (source == null || target == null || target.Removed ||
                source is Entity { Removed: true } ||
                source is Limb { Removed: true } ||
                user is { Removed: true })
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

            Vector2 diff = target.WorldPosition - GetSourcePos(useDrawPosition: false);
            float lengthSqr = diff.LengthSquared();
            if (lengthSqr > MaxLength * MaxLength)
            {
                Snap();
                return;
            }

            if (MaxAngle < 180 && lengthSqr > 2500)
            {
                launchDir ??= diff;
                float angle = MathHelper.ToDegrees(launchDir.Value.Angle(diff));
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
            currentRopeLength = diff.Length();
            if (currentRopeLength > 0.001f)
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
                switch (projectile.StickTarget.UserData)
                {
                    case Limb targetLimb:
                        targetCharacter = targetLimb.character;
                        targetMass = targetLimb.ragdoll.Mass;
                        break;
                    case Character character:
                        targetCharacter = character;
                        targetMass = character.Mass;
                        break;
                    case Item _:
                        targetMass = projectile.StickTarget.Mass;
                        break;
                }
                if (projectile.StickTarget.BodyType != BodyType.Dynamic)
                {
                    targetMass = float.MaxValue;
                }
                // Currently can only apply pull forces to the source, when it's a character, not e.g. when the item would be auto-operated by an AI. Might have to change this.
                if (user != null)
                {
                    if (!snapped)
                    {
                        user.AnimController.HoldToRope();
                        if (targetCharacter != null)
                        {
                            targetCharacter.AnimController.DragWithRope();
                        }
                        if (user.InWater)
                        {
                            user.AnimController.HangWithRope();
                        }
                    }
                    if (Math.Abs(SourcePullForce) > 0.001f && targetMass > TargetMinMass)
                    {
                        // This should be the main collider.
                        var sourceBody = GetBodyToPull(source);
                        if (sourceBody != null)
                        {
                            isReelingIn = user.InWater && user.IsRagdolled || !user.InWater && targetCharacter is { IsIncapacitated: false };
                            if (isReelingIn)
                            {
                                float pullForce = SourcePullForce;
                                if (!user.InWater)
                                {
                                    // Apply a tiny amount to the character holding the rope, so that the connection "feels" more real.
                                    pullForce *= 0.1f;
                                }
                                float lengthFactor = MathUtils.InverseLerp(0, MaxLength / 2, currentRopeLength);
                                float force = LerpForces ? MathHelper.Lerp(0, pullForce, lengthFactor) : pullForce;
                                sourceBody.ApplyForce(forceDir * force);
                                // Take the target velocity into account.
                                PhysicsBody targetBody = GetBodyToPull(target);
                                if (targetBody != null)
                                {
                                    if (targetCharacter != null)
                                    {
                                        if (targetBody.LinearVelocity != Vector2.Zero && sourceBody.LinearVelocity != Vector2.Zero)
                                        {
                                            Vector2 targetDir = Vector2.Normalize(targetBody.LinearVelocity);
                                            float movementDot = Vector2.Dot(Vector2.Normalize(sourceBody.LinearVelocity), targetDir);
                                            if (movementDot < 0)
                                            {
                                                // Pushing to a different dir -> add some counter force
                                                const float multiplier = 5;
                                                float inverseLengthFactor = MathHelper.Lerp(1, 0, lengthFactor);
                                                sourceBody.ApplyForce(targetBody.LinearVelocity * Math.Min(targetBody.Mass * multiplier, 250) * sourceBody.Mass * -movementDot * inverseLengthFactor);
                                            }
                                            float forceDot = Vector2.Dot(forceDir, targetDir);
                                            if (forceDot > 0)
                                            {
                                                // Pulling to the same dir -> add extra force
                                                float targetSpeed = targetBody.LinearVelocity.Length();
                                                const float multiplier = 25;
                                                sourceBody.ApplyForce(forceDir * targetSpeed * sourceBody.Mass * multiplier * forceDot * lengthFactor);
                                            }
                                            float colliderMainLimbDistance = Vector2.Distance(sourceBody.SimPosition, user.AnimController.MainLimb.SimPosition);
                                            const float minDist = 1;
                                            const float maxDist = 10;
                                            if (colliderMainLimbDistance > minDist)
                                            {
                                                // Move the ragdoll closer to the collider, if it's too far (the correction force in HumanAnimController is not enough -> the ragdoll would lag behind and get teleported).
                                                float correctionForce = MathHelper.Lerp(10.0f, NetConfig.MaxPhysicsBodyVelocity, MathUtils.InverseLerp(minDist, maxDist, colliderMainLimbDistance));
                                                Vector2 targetPos = sourceBody.SimPosition + new Vector2((float)Math.Sin(-sourceBody.Rotation), (float)Math.Cos(-sourceBody.Rotation)) * 0.4f;
                                                user.AnimController.MainLimb.MoveToPos(targetPos, correctionForce);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        sourceBody.ApplyForce(targetBody.LinearVelocity * sourceBody.Mass);
                                    }
                                }
                            }
                        }
                    }
                }

                if (Math.Abs(TargetPullForce) > 0.001f && user is not { IsRagdolled: true})
                {
                    PhysicsBody targetBody = GetBodyToPull(target);
                    if (targetBody == null) { return; }
                    bool lerpForces = LerpForces;
                    float maxVelocity = NetConfig.MaxPhysicsBodyVelocity * 0.25f;
                    // The distance where we start pulling with max force.
                    float maxPullDistance = MaxLength / 3;
                    float minPullDistance = MinPullDistance;
                    const float absoluteMinPullDistance = 50;
                    if (targetCharacter != null)
                    {
                        if (targetCharacter.IsRagdolled || targetCharacter.IsUnconscious)
                        {
                            if (!targetCharacter.InWater)
                            {
                                // Limits the velocity of ragdolled characters on ground/air, because otherwise they tend to move with too high forces.
                                maxVelocity = NetConfig.MaxPhysicsBodyVelocity * 0.075f;
                            }
                        }
                        else
                        {
                            // Target alive and kicking -> Use the absolute min pull distance and full forces to pull.
                            // Keep some lerping, because it results into smoothing when the target is close by.
                            minPullDistance = absoluteMinPullDistance;
                            maxPullDistance = 200;
                        }
                    }
                    minPullDistance = MathHelper.Max(minPullDistance, absoluteMinPullDistance);
                    if (currentRopeLength < minPullDistance) { return; }
                    maxPullDistance = MathHelper.Max(minPullDistance * 2, maxPullDistance);
                    float force = lerpForces
                        ? MathHelper.Lerp(0, TargetPullForce, MathUtils.InverseLerp(minPullDistance, maxPullDistance, currentRopeLength))
                        : TargetPullForce;
                    targetBody.ApplyForce(-forceDir * force, maxVelocity);
                    AnimController targetRagdoll = targetCharacter?.AnimController;
                    if (targetRagdoll?.Collider != null)
                    {
                        isReelingIn = true;
                        if (targetRagdoll.InWater || targetRagdoll.OnGround)
                        {
                            float forceMultiplier = 1;
                            if (!targetCharacter.IsRagdolled && !targetCharacter.IsIncapacitated)
                            {
                                // Pulling the main collider requires higher forces when the target is trying to move away.
                                Vector2 targetMovement = targetCharacter.AnimController.TargetMovement;
                                float dot = Vector2.Dot(Vector2.Normalize(targetMovement), forceDir);
                                if (dot > 0)
                                {
                                    const float constMultiplier = 2.5f;
                                    float targetVelocity = targetMovement.Length();
                                    float massFactor = Math.Max((float)Math.Log(targetCharacter.Mass / 10), 1);
                                    forceMultiplier = Math.Max(targetVelocity * massFactor * constMultiplier * dot, 1);
                                }
                            }
                            targetRagdoll.Collider.ApplyForce(-forceDir * force * forceMultiplier, maxVelocity);   
                        }
                    }
                }
            }
        }
        
        partial void UpdateProjSpecific();

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

        /// <summary>
        /// Get the position the rope starts from (taking into account barrel positions if needed)
        /// </summary>
        /// <param name="useDrawPosition">Should the interpolated draw position be used? If not, the WorldPosition is used.</param>
        private Vector2 GetSourcePos(bool useDrawPosition = false)
        {
            Vector2 sourcePos = source.WorldPosition;
            if (source is Item sourceItem)
            {
                if (useDrawPosition)
                {
                    sourcePos = sourceItem.DrawPosition;
                }
                if (!sourceItem.Removed)
                {
                    if (sourceItem.GetComponent<Turret>() is { } turret)
                    {
                        sourcePos = new Vector2(sourceItem.WorldRect.X + turret.TransformedBarrelPos.X, sourceItem.WorldRect.Y - turret.TransformedBarrelPos.Y);
                    }
                    else if (sourceItem.GetComponent<RangedWeapon>() is { } weapon)
                    {
                        sourcePos += ConvertUnits.ToDisplayUnits(weapon.TransformedBarrelPos);
                    }
                }
            }
            else if (useDrawPosition && source is Limb sourceLimb && sourceLimb.body != null)
            {
                sourcePos = sourceLimb.body.DrawPosition;                
            }
            return sourcePos;
        }


        private static PhysicsBody GetBodyToPull(ISpatialEntity target)
        {
            if (target is Item targetItem)
            {
                if (targetItem.ParentInventory is CharacterInventory { Owner: Character ownerCharacter })
                {
                    if (ownerCharacter.Removed) { return null; }
                    return ownerCharacter.AnimController.Collider;
                }
                var projectile = targetItem.GetComponent<Projectile>();
                if (projectile is { StickTarget: not null })
                {
                    return projectile.StickTarget.UserData switch
                    {
                        Structure structure => structure.Submarine?.PhysicsBody,
                        Submarine sub => sub.PhysicsBody,
                        Item item => item.body,
                        Limb limb => limb.body,
                        _ => null
                    };
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
