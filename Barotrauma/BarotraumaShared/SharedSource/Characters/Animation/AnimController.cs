using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    abstract class AnimController : Ragdoll
    {
        public Vector2 RightHandIKPos { get; protected set; }
        public Vector2 LeftHandIKPos { get; protected set; }

        protected LimbJoint rightShoulder, leftShoulder;
        protected float upperArmLength, forearmLength;
        protected float useItemTimer;
        protected bool aiming;
        protected bool wasAiming;
        protected bool aimingMelee;
        protected bool wasAimingMelee;

        public bool IsAiming => wasAiming;
        public bool IsAimingMelee => wasAimingMelee;

        protected bool Aiming => aiming || aimingMelee || FlipLockTime > Timing.TotalTime && character.IsKeyDown(InputType.Aim);

        public float ArmLength => upperArmLength + forearmLength;

        public abstract GroundedMovementParams WalkParams { get; set; }
        public abstract GroundedMovementParams RunParams { get; set; }
        public abstract SwimParams SwimSlowParams { get; set; }
        public abstract SwimParams SwimFastParams { get; set; }
        
        protected class AnimSwap
        {
            public readonly AnimationType AnimationType;
            public readonly AnimationParams TemporaryAnimation;
            public readonly float Priority;
            public bool IsActive
            {
                get { return _isActive; }
                set
                {
                    if (value)
                    {
                        expirationTimer = expirationTime;
                    }
                    _isActive = value;
                }
            } 
            private bool _isActive;
            private float expirationTimer;
            private const float expirationTime = 0.1f;
            
            public AnimSwap(AnimationParams temporaryAnimation, float priority)
            {
                AnimationType = temporaryAnimation.AnimationType;
                TemporaryAnimation = temporaryAnimation;
                Priority = priority;
                IsActive = true;
            }
            
            public void Update(float deltaTime)
            {
                expirationTimer -= deltaTime;
                if (expirationTimer <= 0)
                {
                    IsActive = false;
                }
            }
        }
        
        protected readonly Dictionary<AnimationType, AnimSwap> tempAnimations = new Dictionary<AnimationType, AnimSwap>();
        protected readonly HashSet<AnimationType> expiredAnimations = new HashSet<AnimationType>();

        public AnimationParams CurrentAnimationParams
        {
            get
            {
                if (ForceSelectAnimationType == AnimationType.NotDefined)
                {
                    return (InWater || !CanWalk) ? (AnimationParams)CurrentSwimParams : CurrentGroundedParams;
                }
                else
                {
                    return GetAnimationParamsFromType(ForceSelectAnimationType);
                }
            }
        }
        public AnimationType ForceSelectAnimationType { get; set; }
        public GroundedMovementParams CurrentGroundedParams
        {
            get
            {
                if (ForceSelectAnimationType != AnimationType.NotDefined)
                {
                    return GetAnimationParamsFromType(ForceSelectAnimationType) as GroundedMovementParams;
                }
                if (!CanWalk)
                {
                    //DebugConsole.ThrowError($"{character.SpeciesName} cannot walk!");
                    return null;
                }
                else
                {
                    if (this is HumanoidAnimController humanAnimController && humanAnimController.Crouching)
                    {
                        return humanAnimController.HumanCrouchParams;
                    }
                    return IsMovingFast ? RunParams : WalkParams;
                }
            }
        }
        public SwimParams CurrentSwimParams
        {
            get
            {
                if (ForceSelectAnimationType != AnimationType.NotDefined)
                {
                    return GetAnimationParamsFromType(ForceSelectAnimationType) as SwimParams;
                }
                else
                {
                    return IsMovingFast ? SwimFastParams : SwimSlowParams;
                }
            }
        }

        public bool CanWalk => RagdollParams.CanWalk;
        public bool IsMovingBackwards => 
            !InWater && 
            Math.Sign(targetMovement.X) == -Math.Sign(Dir) && 
            CurrentAnimationParams is not FishGroundedParams { Flip: false } &&
            Anim != Animation.Climbing;

        // TODO: define death anim duration in XML
        protected float deathAnimTimer, deathAnimDuration = 5.0f;

        /// <summary>
        /// Note: Presupposes that the slow speed is lower than the high speed. Otherwise will give invalid results.
        /// </summary>
        public bool IsMovingFast
        {
            get
            {
                if (InWater || !CanWalk)
                {
                    return TargetMovement.LengthSquared() > MathUtils.Pow2(SwimSlowParams.MovementSpeed + 0.0001f);
                }
                else
                {
                    return Math.Abs(TargetMovement.X) > (WalkParams.MovementSpeed + RunParams.MovementSpeed) / 2.0f;
                }
            }
        }

        /// <summary>
        /// Note: creates a new list every time, because the params might have changed. If there is a need to access the property frequently, change the implementation to an array, where the slot is updated when the param is updated(?)
        /// Currently it's not simple to implement, since the properties are not implemented here, but in the derived classes. Would require to change the params virtual and to call the base property getter/setter or something.
        /// </summary>
        public List<AnimationParams> AllAnimParams
        {
            get
            {
                if (CanWalk)
                {
                    var anims = new List<AnimationParams> { WalkParams, RunParams, SwimSlowParams, SwimFastParams };
                    if (this is HumanoidAnimController humanAnimController)
                    {
                        anims.Add(humanAnimController.HumanCrouchParams);
                    }
                    return anims;
                }
                else
                {
                    return new List<AnimationParams> { SwimSlowParams, SwimFastParams };
                }
            }
        }

        public enum Animation { None, Climbing, UsingItem, Struggle, CPR, UsingItemWhileClimbing };
        public Animation Anim;

        public bool IsUsingItem => Anim == Animation.UsingItem || Anim == Animation.UsingItemWhileClimbing;
        public bool IsClimbing => Anim == Animation.Climbing || Anim == Animation.UsingItemWhileClimbing;

        public Vector2 AimSourceWorldPos
        {
            get
            {
                Vector2 sourcePos = character.AnimController.AimSourcePos;
                if (character.Submarine != null) { sourcePos += character.Submarine.Position; }
                return sourcePos;
            }
        }

        public Vector2 AimSourcePos => ConvertUnits.ToDisplayUnits(AimSourceSimPos);
        public virtual Vector2 AimSourceSimPos => Collider.SimPosition;

        protected float? GetValidOrNull(AnimationParams p, float? v)
        {
            if (p == null) { return null; }
            if (v == null) { return null; }
            if (!MathUtils.IsValid(v.Value)) { return null; }
            return v.Value;
        }
        protected Vector2? GetValidOrNull(AnimationParams p, Vector2 v)
        {
            if (p == null) { return null; }
            return v;
        }

        public override float? HeadPosition => GetValidOrNull(CurrentGroundedParams, CurrentGroundedParams?.HeadPosition * RagdollParams.JointScale);
        public override float? TorsoPosition => GetValidOrNull(CurrentGroundedParams, CurrentGroundedParams?.TorsoPosition * RagdollParams.JointScale);
        public override float? HeadAngle => GetValidOrNull(CurrentAnimationParams, CurrentAnimationParams?.HeadAngleInRadians);
        public override float? TorsoAngle => GetValidOrNull(CurrentAnimationParams, CurrentAnimationParams?.TorsoAngleInRadians);
        public virtual Vector2? StepSize => GetValidOrNull(CurrentGroundedParams, CurrentGroundedParams.StepSize * RagdollParams.JointScale);

        public bool AnimationTestPose { get; set; }

        public float WalkPos { get; protected set; }

        public AnimController(Character character, string seed, RagdollParams ragdollParams = null) : base(character, seed, ragdollParams) { }
        
        public void UpdateAnimations(float deltaTime)
        {
            UpdateTemporaryAnimations(deltaTime);
            UpdateAnim(deltaTime);
        }

        protected abstract void UpdateAnim(float deltaTime);

        public abstract void DragCharacter(Character target, float deltaTime);

        public virtual float GetSpeed(AnimationType type)
        {
            GroundedMovementParams movementParams;
            switch (type)
            {
                case AnimationType.Walk:
                    if (!CanWalk)
                    {
                        DebugConsole.ThrowError($"{character.SpeciesName} cannot walk!");
                        return 0;
                    }
                    movementParams = WalkParams;
                    break;
                case AnimationType.Run:
                    if (!CanWalk)
                    {
                        DebugConsole.ThrowError($"{character.SpeciesName} cannot run!");
                        return 0;
                    }
                    movementParams = RunParams;
                    break;
                case AnimationType.SwimSlow:
                    return SwimSlowParams.MovementSpeed;
                case AnimationType.SwimFast:
                    return SwimFastParams.MovementSpeed;
                default:
                    throw new NotImplementedException(type.ToString());
            }
            return IsMovingBackwards ? movementParams.MovementSpeed * movementParams.BackwardsMovementMultiplier : movementParams.MovementSpeed;
        }

        public float GetCurrentSpeed(bool useMaxSpeed)
        {
            AnimationType animType;
            if (InWater || !CanWalk)
            {
                if (useMaxSpeed)
                {
                    animType = AnimationType.SwimFast;
                }
                else
                {
                    animType = AnimationType.SwimSlow;
                }
            }
            else
            {
                if (useMaxSpeed)
                {
                    animType = AnimationType.Run;
                }
                else
                {
                    if (this is HumanoidAnimController humanAnimController && humanAnimController.Crouching)
                    {
                        animType = AnimationType.Crouch;
                    }
                    else
                    {
                        animType = AnimationType.Walk;
                    }
                }
            }
            return GetSpeed(animType);
        }

        public AnimationParams GetAnimationParamsFromType(AnimationType type)
        {
            switch (type)
            {
                case AnimationType.Walk:
                    return CanWalk ? WalkParams : null;
                case AnimationType.Run:
                    return CanWalk ? RunParams : null;
                case AnimationType.Crouch:
                    if (this is HumanoidAnimController humanAnimController)
                    {
                        return humanAnimController.HumanCrouchParams;
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Animation params of type {type} not implemented for non-humanoids!");
                        return null;
                    }
                case AnimationType.SwimSlow:
                    return SwimSlowParams;
                case AnimationType.SwimFast:
                    return SwimFastParams;
                case AnimationType.NotDefined:
                default:
                    return null;
            }
        }

        public float GetHeightFromFloor() => GetColliderBottom().Y - FloorY;

        // We need some margin, because if a hatch has closed, it's possible that the height from floor is slightly negative.
        public bool IsAboveFloor => GetHeightFromFloor() > -0.1f;

        public float FlipLockTime { get; private set; }
        public void LockFlipping(float time = 0.2f)
        {
            FlipLockTime = (float)Timing.TotalTime + time;
        }

        public void UpdateUseItem(bool allowMovement, Vector2 handWorldPos)
        {
            useItemTimer = 0.05f;
            StartUsingItem();

            if (!allowMovement)
            {
                TargetMovement = Vector2.Zero;
                TargetDir = handWorldPos.X > character.WorldPosition.X ? Direction.Right : Direction.Left;
                if (InWater)
                {
                    float sqrDist = Vector2.DistanceSquared(character.WorldPosition, handWorldPos);
                    if (sqrDist > MathUtils.Pow(ConvertUnits.ToDisplayUnits(upperArmLength + forearmLength), 2))
                    {
                        TargetMovement = GetTargetMovement(Vector2.Normalize(handWorldPos - character.WorldPosition));
                    }
                }
                else
                {
                    float distX = Math.Abs(handWorldPos.X - character.WorldPosition.X);
                    if (distX > ConvertUnits.ToDisplayUnits(upperArmLength + forearmLength))
                    {
                        TargetMovement = GetTargetMovement(Vector2.UnitX * Math.Sign(handWorldPos.X - character.WorldPosition.X));
                    }
                }
                Vector2 GetTargetMovement(Vector2 dir)
                {
                    return dir * GetCurrentSpeed(false) * Math.Max(character.SpeedMultiplier, 1);
                }
            }

            if (!character.Enabled) { return; }

            Vector2 handSimPos = ConvertUnits.ToSimUnits(handWorldPos);
            if (character.Submarine != null)
            {
                handSimPos -= character.Submarine.SimPosition;
            }

            Vector2 refPos = rightShoulder?.WorldAnchorA ?? leftShoulder?.WorldAnchorA ?? MainLimb.SimPosition;
            Vector2 diff = handSimPos - refPos;
            float dist = diff.Length();
            float maxDist = ArmLength * 0.9f;
            if (dist > maxDist)
            {
                handSimPos = refPos + diff / dist * maxDist;
            }

            var leftHand = GetLimb(LimbType.LeftHand);
            if (leftHand != null)
            {
                leftHand.Disabled = true;
                leftHand.PullJointEnabled = true;
                leftHand.PullJointWorldAnchorB = handSimPos;
            }

            var rightHand = GetLimb(LimbType.RightHand);
            if (rightHand != null)
            {
                rightHand.Disabled = true;
                rightHand.PullJointEnabled = true;
                rightHand.PullJointWorldAnchorB = handSimPos;
            }

            //make the character crouch if using an item some distance below them (= on the floor)
            if (!inWater && 
                character.WorldPosition.Y - handWorldPos.Y > ConvertUnits.ToDisplayUnits(CurrentGroundedParams.TorsoPosition) / 4 &&
                this is HumanoidAnimController humanoidAnimController)
            {
                humanoidAnimController.Crouching = true;
                humanoidAnimController.ForceSelectAnimationType = AnimationType.Crouch;
                character.SetInput(InputType.Crouch, hit: false, held: true);
            }
        }

        public void Grab(Vector2 rightHandPos, Vector2 leftHandPos)
        {
            for (int i = 0; i < 2; i++)
            {
                Limb pullLimb = (i == 0) ? GetLimb(LimbType.LeftHand) : GetLimb(LimbType.RightHand);

                pullLimb.Disabled = true;

                pullLimb.PullJointEnabled = true;
                pullLimb.PullJointWorldAnchorB = (i == 0) ? rightHandPos : leftHandPos;
                pullLimb.PullJointMaxForce = 500.0f;
            }
        }

        private Direction previousDirection;
        private readonly Vector2[] transformedHandlePos = new Vector2[2];
        //TODO: refactor this method, it's way too convoluted
        public void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 itemPos, bool aim, float holdAngle, float itemAngleRelativeToHoldAngle = 0.0f, bool aimMelee = false, Vector2? targetPos = null)
        {
            aimingMelee = aimMelee;
            if (character.Stun > 0.0f || character.IsIncapacitated)
            {
                aim = false;
            }

            //calculate the handle positions
            Matrix itemTransform = Matrix.CreateRotationZ(item.body.Rotation);
            transformedHandlePos[0] = Vector2.Transform(handlePos[0], itemTransform);
            transformedHandlePos[1] = Vector2.Transform(handlePos[1], itemTransform);

            Limb torso = GetLimb(LimbType.Torso) ?? MainLimb;
            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            var controller = character.SelectedItem?.GetComponent<Controller>();
            bool usingController = controller is { AllowAiming: false };
            if (!usingController)
            {
                controller = character.SelectedSecondaryItem?.GetComponent<Controller>();
                usingController = controller is { AllowAiming: false };
            }
            bool isClimbing = character.IsClimbing && Math.Abs(character.AnimController.TargetMovement.Y) > 0.01f;
            float itemAngle;
            Holdable holdable = item.GetComponent<Holdable>();
            float torsoRotation = torso.Rotation;

            Item rightHandItem = character.Inventory?.GetItemInLimbSlot(InvSlotType.RightHand);
            bool equippedInRightHand = rightHandItem == item && rightHand is { IsSevered: false };
            Item leftHandItem = character.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand);
            bool equippedInLeftHand = leftHandItem == item && leftHand is { IsSevered: false };
            if (aim && !isClimbing && !usingController && character.Stun <= 0.0f && itemPos != Vector2.Zero && !character.IsIncapacitated)
            {
                targetPos ??= ConvertUnits.ToSimUnits(character.SmoothedCursorPosition);
                
                Vector2 diff = holdable.Aimable ? 
                    (targetPos.Value - AimSourceSimPos) * Dir : 
                    MathUtils.RotatePoint(Vector2.UnitX, torsoRotation);
                
                holdAngle = MathUtils.VectorToAngle(new Vector2(diff.X, diff.Y * Dir)) - torsoRotation * Dir;
                holdAngle += GetAimWobble(rightHand, leftHand, item);
                itemAngle = torsoRotation + holdAngle * Dir;

                if (holdable.ControlPose)
                {
                    //if holding two items that should control the characters' pose, let the item in the right hand do it
                    bool anotherItemControlsPose = equippedInLeftHand && rightHandItem != item && (rightHandItem?.GetComponent<Holdable>()?.ControlPose ?? false);
                    if (!anotherItemControlsPose && TargetMovement == Vector2.Zero && inWater)
                    {
                        torso.body.AngularVelocity -= torso.body.AngularVelocity * 0.1f;
                        torso.body.ApplyForce(torso.body.LinearVelocity * -0.5f);
                    }
                    aiming = true;
                }
            }
            else
            {
                if (holdable.UseHandRotationForHoldAngle)
                {
                    if (equippedInRightHand)
                    {
                        itemAngle = rightHand.Rotation + holdAngle * Dir;
                    }
                    else if (equippedInLeftHand)
                    {
                        itemAngle = leftHand.Rotation + holdAngle * Dir;
                    }
                    else
                    {
                        itemAngle = torsoRotation + holdAngle * Dir;
                    }
                }
                else
                {
                    itemAngle = torsoRotation + holdAngle * Dir;
                }
            }

            if (rightShoulder == null) { return; }
            Vector2 transformedHoldPos = rightShoulder.WorldAnchorA;
            if (itemPos == Vector2.Zero || isClimbing || usingController)
            {
                if (equippedInRightHand)
                {
                    transformedHoldPos = rightHand.PullJointWorldAnchorA - transformedHandlePos[0];
                    itemAngle = rightHand.Rotation + (holdAngle - rightHand.Params.GetSpriteOrientation() + MathHelper.PiOver2) * Dir;
                }
                else if (equippedInLeftHand)
                {
                    transformedHoldPos = leftHand.PullJointWorldAnchorA - transformedHandlePos[1];
                    itemAngle = leftHand.Rotation + (holdAngle - leftHand.Params.GetSpriteOrientation() + MathHelper.PiOver2) * Dir;
                }
            }
            else
            {
                if (equippedInRightHand)
                {
                    transformedHoldPos = rightShoulder.WorldAnchorA;
                    rightHand.Disabled = true;
                }
                if (equippedInLeftHand)
                {
                    if (leftShoulder == null) { return; }
                    transformedHoldPos = leftShoulder.WorldAnchorA;
                    leftHand.Disabled = true;
                }
                itemPos.X *= Dir;
                transformedHoldPos += Vector2.Transform(itemPos, Matrix.CreateRotationZ(itemAngle));
            }

            item.body.ResetDynamics();

            Vector2 currItemPos = equippedInRightHand ?
                rightHand.PullJointWorldAnchorA - transformedHandlePos[0] :
                leftHand.PullJointWorldAnchorA - transformedHandlePos[1];

            if (!MathUtils.IsValid(currItemPos))
            {
                string errorMsg = "Attempted to move the item \"" + item + "\" to an invalid position in HumanidAnimController.HoldItem: " +
                    currItemPos + ", rightHandPos: " + rightHand.PullJointWorldAnchorA + ", leftHandPos: " + leftHand.PullJointWorldAnchorA +
                    ", handlePos[0]: " + handlePos[0] + ", handlePos[1]: " + handlePos[1] +
                    ", transformedHandlePos[0]: " + transformedHandlePos[0] + ", transformedHandlePos[1]:" + transformedHandlePos[1] +
                    ", item pos: " + item.SimPosition + ", itemAngle: " + itemAngle +
                    ", collider pos: " + character.SimPosition;
                DebugConsole.Log(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "HumanoidAnimController.HoldItem:InvalidPos:" + character.Name + item.Name,
                    GameAnalyticsManager.ErrorSeverity.Error,
                    errorMsg);

                return;
            }

            float targetAngle = MathUtils.WrapAngleTwoPi(itemAngle + itemAngleRelativeToHoldAngle * Dir);
            float currentRotation = MathUtils.WrapAngleTwoPi(item.body.Rotation);
            float itemRotation = MathHelper.SmoothStep(currentRotation, targetAngle, deltaTime * 25);
            if (previousDirection != dir || Math.Abs(targetAngle - currentRotation) > MathHelper.Pi)
            {
                itemRotation = targetAngle;
            }
            item.SetTransform(currItemPos, itemRotation, setPrevTransform: false);
            previousDirection = dir;

            if (holdable.Pusher != null)
            {
                if (character.Stun > 0.0f || character.IsIncapacitated)
                {
                    holdable.Pusher.Enabled = false;
                }
                else
                {
                    if (!holdable.Pusher.Enabled)
                    {
                        holdable.Pusher.Enabled = true;
                        holdable.Pusher.ResetDynamics();
                        holdable.Pusher.SetTransform(currItemPos, itemAngle);
                    }
                    else
                    {
                        holdable.Pusher.TargetPosition = currItemPos;
                        holdable.Pusher.TargetRotation = itemRotation;
                        holdable.Pusher.MoveToTargetPosition(true);
                    }
                }
            }

            if (!isClimbing && !character.IsIncapacitated && itemPos != Vector2.Zero && (aim || !holdable.UseHandRotationForHoldAngle))
            {
                for (int i = 0; i < 2; i++)
                {
                    if (!character.Inventory.IsInLimbSlot(item, i == 0 ? InvSlotType.RightHand : InvSlotType.LeftHand)) { continue; }
#if DEBUG
                    if (handlePos[i].LengthSquared() > ArmLength)
                    {
                        DebugConsole.AddWarning($"Aim position for the item {item.Name} may be incorrect (further than the length of the character's arm)",
                            item.Prefab.ContentPackage);
                    }
#endif
                    HandIK(
                        i == 0 ? rightHand : leftHand, transformedHoldPos + transformedHandlePos[i],
                        CurrentAnimationParams.ArmIKStrength,
                        CurrentAnimationParams.HandIKStrength,
                        maxAngularVelocity: 15.0f);
                }
            }
        }

        private float GetAimWobble(Limb rightHand, Limb leftHand, Item heldItem)
        {
            float wobbleStrength = 0.0f;
            if (character.Inventory?.GetItemInLimbSlot(InvSlotType.RightHand) == heldItem)
            {
                wobbleStrength += Character.CharacterHealth.GetLimbDamage(rightHand, afflictionType: AfflictionPrefab.DamageType);
            }
            if (character.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand) == heldItem)
            {
                wobbleStrength += Character.CharacterHealth.GetLimbDamage(leftHand, afflictionType: AfflictionPrefab.DamageType);
            }
            if (wobbleStrength <= 0.1f) { return 0.0f; }
            wobbleStrength = (float)Math.Min(wobbleStrength, 1.0f);

            float lowFreqNoise = PerlinNoise.GetPerlin((float)Timing.TotalTime / 320.0f, (float)Timing.TotalTime / 240.0f) - 0.5f;
            float highFreqNoise = PerlinNoise.GetPerlin((float)Timing.TotalTime / 40.0f, (float)Timing.TotalTime / 50.0f) - 0.5f;

            return (lowFreqNoise * 1.0f + highFreqNoise * 0.1f) * wobbleStrength;
        }

        public void HandIK(Limb hand, Vector2 pos, float armTorque = 1.0f, float handTorque = 1.0f, float maxAngularVelocity = float.PositiveInfinity)
        {
            Vector2 shoulderPos;

            Limb arm, forearm;
            if (hand.type == LimbType.LeftHand)
            {
                if (leftShoulder == null) { return; }
                shoulderPos = leftShoulder.WorldAnchorA;
                arm = GetLimb(LimbType.LeftArm);
                forearm = GetLimb(LimbType.LeftForearm);
                LeftHandIKPos = pos;
            }
            else
            {
                if (rightShoulder == null) { return; }
                shoulderPos = rightShoulder.WorldAnchorA;
                arm = GetLimb(LimbType.RightArm);
                forearm = GetLimb(LimbType.RightForearm);
                RightHandIKPos = pos;
            }
            if (arm == null) { return; }

            //distance from shoulder to holdpos
            float c = Vector2.Distance(pos, shoulderPos);
            c = MathHelper.Clamp(c, Math.Abs(upperArmLength - forearmLength), forearmLength + upperArmLength - 0.01f);

            float armAngle = MathUtils.VectorToAngle(pos - shoulderPos) + arm.Params.GetSpriteOrientation() - MathHelper.PiOver2;
            float upperArmAngle = MathUtils.SolveTriangleSSS(forearmLength, upperArmLength, c) * Dir;
            float lowerArmAngle = MathUtils.SolveTriangleSSS(upperArmLength, forearmLength, c) * Dir;

            //make sure the arm angle "has the same number of revolutions" as the arm
            while (arm.Rotation - armAngle > MathHelper.Pi)
            {
                armAngle += MathHelper.TwoPi;
            }
            while (arm.Rotation - armAngle < -MathHelper.Pi)
            {
                armAngle -= MathHelper.TwoPi;
            }

            if (arm?.body != null && Math.Abs(arm.body.AngularVelocity) < maxAngularVelocity)
            {
                arm.body.SmoothRotate(armAngle - upperArmAngle, 100.0f * armTorque * arm.Mass, wrapAngle: false);
            }
            float forearmAngle = armAngle + lowerArmAngle;
            if (forearm?.body != null && Math.Abs(forearm.body.AngularVelocity) < maxAngularVelocity)
            {
                forearm.body.SmoothRotate(forearmAngle, 100.0f * handTorque * forearm.Mass, wrapAngle: false);
            }
            if (hand?.body != null && Math.Abs(hand.body.AngularVelocity) < maxAngularVelocity)
            {
                float handAngle = forearm != null ? forearmAngle : armAngle;
                hand.body.SmoothRotate(handAngle, 10.0f * handTorque * hand.Mass, wrapAngle: false);
            }
        }

        public void ApplyPose(Vector2 leftHandPos, Vector2 rightHandPos, Vector2 leftFootPos, Vector2 rightFootPos, float footMoveForce = 10)
        {
            var leftHand = GetLimb(LimbType.LeftHand);
            var rightHand = GetLimb(LimbType.RightHand);
            var waist = GetLimb(LimbType.Waist) ?? GetLimb(LimbType.Torso);
            if (waist == null) { return; }
            Vector2 midPos = waist.SimPosition;
            if (leftHand != null)
            {
                leftHand.Disabled = true;
                leftHandPos.X *= Dir;
                leftHandPos += midPos;
                HandIK(leftHand, leftHandPos);
            }
            if (rightHand != null)
            {
                rightHand.Disabled = true;
                rightHandPos.X *= Dir;
                rightHandPos += midPos;
                HandIK(rightHand, rightHandPos);
            }
            var leftFoot = GetLimb(LimbType.LeftFoot);
            if (leftFoot != null)
            {
                leftFoot.Disabled = true;
                leftFootPos = new Vector2(waist.SimPosition.X + leftFootPos.X * Dir, GetColliderBottom().Y + leftFootPos.Y);
                MoveLimb(leftFoot, leftFootPos, Math.Abs(leftFoot.SimPosition.X - leftFootPos.X) * footMoveForce * leftFoot.Mass, true);
            }
            var rightFoot = GetLimb(LimbType.RightFoot);
            if (rightFoot != null)
            {
                rightFoot.Disabled = true;
                rightFootPos = new Vector2(waist.SimPosition.X + rightFootPos.X * Dir, GetColliderBottom().Y + rightFootPos.Y);
                MoveLimb(rightFoot, rightFootPos, Math.Abs(rightFoot.SimPosition.X - rightFootPos.X) * footMoveForce * rightFoot.Mass, true);
            }
        }

        public void ApplyTestPose()
        {
            var waist = GetLimb(LimbType.Waist) ?? GetLimb(LimbType.Torso);
            if (waist != null)
            {
                ApplyPose(
                    new Vector2(-0.75f, -0.2f),
                    new Vector2(0.75f, -0.2f),
                    new Vector2(-WalkParams.StepSize.X * 0.5f, -0.1f * RagdollParams.JointScale),
                    new Vector2(WalkParams.StepSize.X * 0.5f, -0.1f * RagdollParams.JointScale));
            }
        }

        protected void CalculateArmLengths()
        {
            //calculate arm and forearm length (atm this assumes that both arms are the same size)
            Limb rightForearm = GetLimb(LimbType.RightForearm);
            Limb rightHand = GetLimb(LimbType.RightHand);
            if (rightHand == null) { return; }

            rightShoulder = GetJointBetweenLimbs(LimbType.Torso, LimbType.RightArm) ?? GetJointBetweenLimbs(LimbType.Head, LimbType.RightArm) ?? GetJoint(LimbType.RightArm, new LimbType[] { LimbType.RightHand, LimbType.RightForearm });
            leftShoulder = GetJointBetweenLimbs(LimbType.Torso, LimbType.LeftArm) ?? GetJointBetweenLimbs(LimbType.Head, LimbType.LeftArm) ?? GetJoint(LimbType.LeftArm, new LimbType[] { LimbType.LeftHand, LimbType.LeftForearm });

            Vector2 localAnchorShoulder = Vector2.Zero;
            Vector2 localAnchorElbow = Vector2.Zero;
            if (rightShoulder != null)
            {
                localAnchorShoulder = rightShoulder.LimbA.type == LimbType.RightArm ? rightShoulder.LocalAnchorA : rightShoulder.LocalAnchorB;
            }
            LimbJoint rightElbow = rightForearm == null ?
                GetJointBetweenLimbs(LimbType.RightArm, LimbType.RightHand) :
                GetJointBetweenLimbs(LimbType.RightArm, LimbType.RightForearm);
            if (rightElbow != null)
            {
                localAnchorElbow = rightElbow.LimbA.type == LimbType.RightArm ? rightElbow.LocalAnchorA : rightElbow.LocalAnchorB;
            }
            upperArmLength = Vector2.Distance(localAnchorShoulder, localAnchorElbow);
            if (rightElbow != null)
            {
                if (rightForearm == null)
                {
                    forearmLength = Vector2.Distance(
                        rightHand.PullJointLocalAnchorA,
                        rightElbow.LimbA.type == LimbType.RightHand ? rightElbow.LocalAnchorA : rightElbow.LocalAnchorB);
                }
                else
                {
                    LimbJoint rightWrist = GetJointBetweenLimbs(LimbType.RightForearm, LimbType.RightHand);
                    if (rightWrist != null)
                    {
                        forearmLength = Vector2.Distance(
                            rightElbow.LimbA.type == LimbType.RightForearm ? rightElbow.LocalAnchorA : rightElbow.LocalAnchorB,
                            rightWrist.LimbA.type == LimbType.RightForearm ? rightWrist.LocalAnchorA : rightWrist.LocalAnchorB);

                        forearmLength += Vector2.Distance(
                            rightHand.PullJointLocalAnchorA,
                            rightWrist.LimbA.type == LimbType.RightHand ? rightWrist.LocalAnchorA : rightWrist.LocalAnchorB);
                    }
                }
            }
        }

        protected LimbJoint GetJointBetweenLimbs(LimbType limbTypeA, LimbType limbTypeB)
        {
            return LimbJoints.FirstOrDefault(lj =>
                (lj.LimbA.type == limbTypeA && lj.LimbB.type == limbTypeB) ||
                (lj.LimbB.type == limbTypeA && lj.LimbA.type == limbTypeB));
        }

        protected LimbJoint GetJoint(LimbType matchingType, IEnumerable<LimbType> ignoredTypes)
        {
            return LimbJoints.FirstOrDefault(lj =>
                lj.LimbA.type == matchingType && ignoredTypes.None(t => lj.LimbB.type == t) ||
                lj.LimbB.type == matchingType && ignoredTypes.None(t => lj.LimbB.type == t));
        }

        public override void Recreate(RagdollParams ragdollParams = null)
        {
            base.Recreate(ragdollParams);
            if (Character.Params.CanInteract)
            {
                CalculateArmLengths();
            }
        }

        private void StartAnimation(Animation animation)
        {
            if (animation == Animation.UsingItem)
            {
                Anim = IsClimbing ? Animation.UsingItemWhileClimbing : Animation.UsingItem;
            }
            else if (animation == Animation.Climbing)
            {
                Anim = IsUsingItem ? Animation.UsingItemWhileClimbing : Animation.Climbing;
            }
            else
            {
                Anim = animation;
            }
        }

        private void StopAnimation(Animation animation)
        {
            if (animation == Animation.UsingItem)
            {
                Anim = IsClimbing ? Animation.Climbing : Animation.None;
            }
            else if (animation == Animation.Climbing)
            {
                Anim = IsUsingItem ? Animation.UsingItem : Animation.None;
            }
            else
            {
                Anim = Animation.None;
            }
        }

        public void StartUsingItem() => StartAnimation(Animation.UsingItem);

        public void StartClimbing() => StartAnimation(Animation.Climbing);

        public void StopUsingItem() => StopAnimation(Animation.UsingItem);

        public void StopClimbing() => StopAnimation(Animation.Climbing);
        
        private readonly Dictionary<AnimationType, AnimationParams> defaultAnimations = new Dictionary<AnimationType, AnimationParams>();
        
        /// <summary>
        /// Loads an animation (variation) that automatically resets in 0.1s, unless triggered again.
        /// Meant e.g. for triggering animations in status effects, without having to worry about resetting them.
        /// </summary>
        public bool TryLoadTemporaryAnimation(StatusEffect.AnimLoadInfo animLoadInfo, bool throwErrors)
        {
            AnimationType animType = animLoadInfo.Type;
            if (tempAnimations.TryGetValue(animType, out AnimSwap animSwap))
            {
                if (animLoadInfo.File.TryGet(out string fileName) && animSwap.TemporaryAnimation.FileNameWithoutExtension.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    // Already loaded, keep active
                    animSwap.IsActive = true;
                    return true;
                }
                else if (animLoadInfo.File.TryGet(out ContentPath contentPath) && animSwap.TemporaryAnimation.Path == contentPath)
                {
                    // Already loaded, keep active
                    animSwap.IsActive = true;
                    return true;
                }
                else
                {
                    if (animSwap.Priority >= animLoadInfo.Priority)
                    {
                        // If the priority of the current animation is higher than the new animation, just return and do nothing.
                        // Returning false would tell the status effect to not try again, which is not what we want here, which is why we fake a bit with the return value.
                        return true;
                    }
                    else
                    {
                        // Override any previous animations of the same type.
                        tempAnimations.Remove(animType);   
                    }
                }
            }
            AnimationParams defaultAnimation = GetAnimationParamsFromType(animType);
            if (defaultAnimation == null) { return false; }
            if (!TryLoadAnimation(animType, animLoadInfo.File, out AnimationParams tempParams, throwErrors)) { return false; }
            // Store the default animation, if not yet stored. There should always be just one of the same type.
            defaultAnimations.TryAdd(animType, defaultAnimation);
            tempAnimations.Add(animType, new AnimSwap(tempParams, animLoadInfo.Priority));
            return true;
        }
        
        private void UpdateTemporaryAnimations(float deltaTime)
        {
            if (tempAnimations.None()) { return; }
            foreach ((AnimationType animationType, AnimSwap animSwap) in tempAnimations)
            {
                if (!animSwap.IsActive)
                {
                    if (defaultAnimations.TryGetValue(animSwap.AnimationType, out AnimationParams defaultAnimation))
                    {
                        TrySwapAnimParams(defaultAnimation);
                        expiredAnimations.Add(animationType); 
                    }
                    else
                    {
                        DebugConsole.ThrowError($"[AnimController] Failed to find the default animation parameters of type {animSwap.AnimationType}. Cannot swap back the default animations!");
                        tempAnimations.Clear();
                    }
                }
            }
            foreach (AnimationType anim in expiredAnimations)
            {
                tempAnimations.Remove(anim);
            }
            expiredAnimations.Clear();
            foreach (AnimSwap animSwap in tempAnimations.Values)
            {
                animSwap.Update(deltaTime);
            }
        }
        
        /// <summary>
        /// Loads animations. Non-permanent (= resets on load).
        /// </summary>
        public bool TryLoadAnimation(AnimationType animationType, Either<string, ContentPath> file, out AnimationParams animParams, bool throwErrors)
        {
            animParams = null;
            if (character.IsHumanoid && this is HumanoidAnimController humanAnimController)
            {
                switch (animationType)
                {
                    case AnimationType.Walk:
                        humanAnimController.WalkParams = HumanWalkParams.GetAnimParams(character, file, throwErrors);
                        animParams = humanAnimController.WalkParams;
                        break;
                    case AnimationType.Run:
                        humanAnimController.RunParams = HumanRunParams.GetAnimParams(character, file, throwErrors);
                        animParams = humanAnimController.RunParams;
                        break;
                    case AnimationType.Crouch:
                        humanAnimController.HumanCrouchParams = HumanCrouchParams.GetAnimParams(character, file, throwErrors);
                        animParams = humanAnimController.HumanCrouchParams;
                        break;
                    case AnimationType.SwimSlow:
                        humanAnimController.SwimSlowParams = HumanSwimSlowParams.GetAnimParams(character, file, throwErrors);
                        animParams = humanAnimController.SwimSlowParams;
                        break;
                    case AnimationType.SwimFast:
                        humanAnimController.SwimFastParams = HumanSwimFastParams.GetAnimParams(character, file, throwErrors);
                        animParams = humanAnimController.SwimFastParams;
                        break;
                    default:
                        DebugConsole.ThrowError($"[AnimController] Animation of type {animationType} not implemented!");
                        break;
                }
            }
            else
            {
                switch (animationType)
                {
                    case AnimationType.Walk:
                        if (CanWalk)
                        {
                            character.AnimController.WalkParams = FishWalkParams.GetAnimParams(character, file, throwErrors);
                            animParams = character.AnimController.WalkParams;
                        }
                        break;
                    case AnimationType.Run:
                        if (CanWalk)
                        {
                            character.AnimController.RunParams = FishRunParams.GetAnimParams(character, file, throwErrors);
                            animParams = character.AnimController.RunParams;
                        }
                        break;
                    case AnimationType.SwimSlow:
                        character.AnimController.SwimSlowParams = FishSwimSlowParams.GetAnimParams(character, file, throwErrors);
                        animParams = character.AnimController.SwimSlowParams;
                        break;
                    case AnimationType.SwimFast:
                        character.AnimController.SwimFastParams = FishSwimFastParams.GetAnimParams(character, file, throwErrors);
                        animParams = character.AnimController.SwimFastParams;
                        break;
                    default:
                        DebugConsole.ThrowError($"[AnimController] Animation of type {animationType} not implemented!");
                        break;
                }
            }
            
            bool success = animParams != null;
            if (!file.TryGet(out string fileName))
            {
                if (file.TryGet(out ContentPath contentPath))
                {
                    fileName = contentPath.Value;
                    if (success)
                    {
                        success = contentPath == animParams.Path;
                    }
                }
            }
            else
            {
                if (success)
                {
                    success = animParams.FileNameWithoutExtension.Equals(fileName, StringComparison.OrdinalIgnoreCase);
                }
            }
            if (success)
            {
                DebugConsole.NewMessage($"Animation {fileName} successfully loaded for {character.DisplayName}", Color.LightGreen, debugOnly: true);
            }
            else if (throwErrors)
            {
                DebugConsole.ThrowError($"Animation {fileName} for {character.DisplayName} could not be loaded!");
            }
            return success;
        }
        
        /// <summary>
        /// Simply swaps existing animation parameters as current parameters.
        /// </summary>
        protected bool TrySwapAnimParams(AnimationParams newParams)
        {
            AnimationType animationType = newParams.AnimationType;
            if (character.IsHumanoid && this is HumanoidAnimController humanAnimController)
            {
                switch (animationType)
                {
                    case AnimationType.Walk:
                        if (newParams is HumanWalkParams newWalkParams)
                        {
                            humanAnimController.WalkParams = newWalkParams;   
                        }
                        return true;
                    case AnimationType.Run:
                        if (newParams is HumanRunParams newRunParams)
                        {
                            humanAnimController.HumanRunParams = newRunParams;
                        }
                        break;
                    case AnimationType.Crouch:
                        if (newParams is HumanCrouchParams newCrouchParams)
                        {
                            humanAnimController.HumanCrouchParams = newCrouchParams;
                        }
                        return true;
                    case AnimationType.SwimSlow:
                        if (newParams is HumanSwimSlowParams newSwimSlowParams)
                        {
                            humanAnimController.HumanSwimSlowParams = newSwimSlowParams;
                        }
                        return true;
                    case AnimationType.SwimFast:
                        if (newParams is HumanSwimFastParams newSwimFastParams)
                        {
                            humanAnimController.HumanSwimFastParams = newSwimFastParams;
                        }
                        return true;
                    default:
                        DebugConsole.ThrowError($"[AnimController] Animation of type {animationType} not implemented!");
                        return false;
                }
            }
            else
            {
                switch (animationType)
                {
                    case AnimationType.Walk:
                        if (newParams is FishWalkParams walkParams)
                        {
                            character.AnimController.WalkParams = walkParams;
                        }
                        return true;
                    case AnimationType.Run:
                        if (newParams is FishRunParams runParams)
                        {
                            character.AnimController.RunParams = runParams;
                        }
                        return true;
                    case AnimationType.SwimSlow:
                        if (newParams is FishSwimSlowParams swimSlowParams)
                        {
                            character.AnimController.SwimSlowParams = swimSlowParams;
                        }
                        return true;
                    case AnimationType.SwimFast:
                        if (newParams is FishSwimFastParams swimFastParams)
                        {
                            character.AnimController.SwimFastParams = swimFastParams;
                        }
                        return true;
                    default:
                        DebugConsole.ThrowError($"[AnimController] Animation of type {animationType} not implemented!");
                        break;
                }
            }
            return false;
        }
    }
}
