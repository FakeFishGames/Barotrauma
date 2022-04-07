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

        protected bool Aiming => aiming || aimingMelee;

        public float ArmLength => upperArmLength + forearmLength;

        public abstract GroundedMovementParams WalkParams { get; set; }
        public abstract GroundedMovementParams RunParams { get; set; }
        public abstract SwimParams SwimSlowParams { get; set; }
        public abstract SwimParams SwimFastParams { get; set; }

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
        public bool IsMovingBackwards => !InWater && Math.Sign(targetMovement.X) == -Math.Sign(Dir);

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
                    return TargetMovement.LengthSquared() > MathUtils.Pow2(SwimSlowParams.MovementSpeed);
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

        public enum Animation { None, Climbing, UsingConstruction, Struggle, CPR };
        public Animation Anim;

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

        public abstract void UpdateAnim(float deltaTime);

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
                    return WalkParams;
                case AnimationType.Run:
                    return RunParams;
                case AnimationType.Crouch:
                    if (this is HumanoidAnimController humanAnimController)
                    {
                        return humanAnimController.HumanCrouchParams;
                    }
                    throw new NotImplementedException(type.ToString());
                case AnimationType.SwimSlow:
                    return SwimSlowParams;
                case AnimationType.SwimFast:
                    return SwimFastParams;
                case AnimationType.NotDefined:
                    return null;
                default:
                    throw new NotImplementedException(type.ToString());
            }
        }

        public float GetHeightFromFloor() => GetColliderBottom().Y - FloorY;

        // We need some margin, because if a hatch has closed, it's possible that the height from floor is slightly negative.
        public bool IsAboveFloor => GetHeightFromFloor() > -0.1f;

        public void UpdateUseItem(bool allowMovement, Vector2 handWorldPos)
        {
            useItemTimer = 0.5f;
            Anim = Animation.UsingConstruction;

            if (!allowMovement)
            {
                TargetMovement = Vector2.Zero;
                TargetDir = handWorldPos.X > character.WorldPosition.X ? Direction.Right : Direction.Left;
                float sqrDist = Vector2.DistanceSquared(character.WorldPosition, handWorldPos);
                if (sqrDist > MathUtils.Pow(ConvertUnits.ToDisplayUnits(upperArmLength + forearmLength), 2))
                {
                    TargetMovement = Vector2.Normalize(handWorldPos - character.WorldPosition) * GetCurrentSpeed(false) * Math.Max(character.SpeedMultiplier, 1);
                }
            }

            if (!character.Enabled) { return; }

            Vector2 handSimPos = ConvertUnits.ToSimUnits(handWorldPos);
            if (character.Submarine != null)
            {
                handSimPos -= character.Submarine.SimPosition;
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
        public void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle, float itemAngleRelativeToHoldAngle = 0.0f, bool aimMelee = false)
        {
            aimingMelee = aimMelee;
            if (character.Stun > 0.0f || character.IsIncapacitated)
            {
                aim = false;
            }

            //calculate the handle positions
            Matrix itemTransfrom = Matrix.CreateRotationZ(item.body.Rotation);
            float horizontalOffset = ConvertUnits.ToSimUnits((item.Sprite.size.X / 2 - item.Sprite.Origin.X) * item.Scale);

            //handlePos[0] = ConvertUnits.ToSimUnits(new Vector2(-45,25) * 0.5f);
            //handlePos[1] = ConvertUnits.ToSimUnits(new Vector2(-65,30) * 0.5f);

            transformedHandlePos[0] = Vector2.Transform(new Vector2(handlePos[0].X + horizontalOffset, handlePos[0].Y), itemTransfrom);
            transformedHandlePos[1] = Vector2.Transform(new Vector2(handlePos[1].X + horizontalOffset, handlePos[1].Y), itemTransfrom);

            Limb torso = GetLimb(LimbType.Torso) ?? MainLimb;
            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Vector2 itemPos = aim ? aimPos : holdPos;

            var controller = character.SelectedConstruction?.GetComponent<Controller>();
            bool usingController = controller != null && !controller.AllowAiming;
            bool isClimbing = character.IsClimbing && Math.Abs(character.AnimController.TargetMovement.Y) > 0.01f;
            float itemAngle;
            Holdable holdable = item.GetComponent<Holdable>();
            float torsoRotation = torso.Rotation;

            Item rightHandItem = character.Inventory?.GetItemInLimbSlot(InvSlotType.RightHand);
            bool equippedInRightHand = rightHandItem == item && rightHand != null && !rightHand.IsSevered;
            Item leftHandItem = character.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand);
            bool equippedInLefthand = leftHandItem == item && leftHand != null && !leftHand.IsSevered;
            if (aim && !isClimbing && !usingController && character.Stun <= 0.0f && itemPos != Vector2.Zero && !character.IsIncapacitated)
            {
                Vector2 mousePos = ConvertUnits.ToSimUnits(character.SmoothedCursorPosition);
                Vector2 diff = holdable.Aimable ? (mousePos - AimSourceSimPos) * Dir : Vector2.UnitX;
                holdAngle = MathUtils.VectorToAngle(new Vector2(diff.X, diff.Y * Dir)) - torsoRotation * Dir;
                holdAngle += GetAimWobble(rightHand, leftHand, item);
                itemAngle = torsoRotation + holdAngle * Dir;

                if (holdable.ControlPose)
                {
                    //if holding two items that should control the characters' pose, let the item in the right hand do it
                    bool anotherItemControlsPose = equippedInLefthand && rightHandItem != item && (rightHandItem?.GetComponent<Holdable>()?.ControlPose ?? false);
                    if (!anotherItemControlsPose)
                    {
                        var head = GetLimb(LimbType.Head);
                        if (head != null)
                        {
                            head.body.SmoothRotate(itemAngle, force: 30 * head.Mass);
                        }
                        if (TargetMovement == Vector2.Zero && inWater)
                        {
                            torso.body.AngularVelocity -= torso.body.AngularVelocity * 0.1f;
                            torso.body.ApplyForce(torso.body.LinearVelocity * -0.5f);
                        }
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
                    else if (equippedInLefthand)
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
                else if (equippedInLefthand)
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
                if (equippedInLefthand)
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
                        holdable.Pusher.TargetRotation = holdAngle * Dir;

                        holdable.Pusher.MoveToTargetPosition(true);

                        currItemPos = holdable.Pusher.SimPosition;
                        itemAngle = holdable.Pusher.Rotation;
                    }
                }
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

            if (!isClimbing && !character.IsIncapacitated && itemPos != Vector2.Zero && (aim || !holdable.UseHandRotationForHoldAngle))
            {
                for (int i = 0; i < 2; i++)
                {
                    if (!character.Inventory.IsInLimbSlot(item, i == 0 ? InvSlotType.RightHand : InvSlotType.LeftHand)) { continue; }
#if DEBUG
                    if (handlePos[i].LengthSquared() > ArmLength)
                    {
                        DebugConsole.AddWarning($"Aim position for the item {item.Name} may be incorrect (further than the length of the character's arm)");
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
                wobbleStrength += Character.CharacterHealth.GetLimbDamage(rightHand, afflictionType: "damage");
            }
            if (character.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand) == heldItem)
            {
                wobbleStrength += Character.CharacterHealth.GetLimbDamage(leftHand, afflictionType: "damage");
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
    }
}
