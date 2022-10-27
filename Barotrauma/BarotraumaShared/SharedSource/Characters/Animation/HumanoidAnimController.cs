using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma
{
    class HumanoidAnimController : AnimController
    {
        public override RagdollParams RagdollParams
        {
            get { return HumanRagdollParams; }
            protected set { HumanRagdollParams = value as HumanRagdollParams; }
        }

        private HumanRagdollParams _ragdollParams;
        public HumanRagdollParams HumanRagdollParams
        {
            get
            {
                if (character.Info == null)
                {
                    if (_ragdollParams == null)
                    {
                        _ragdollParams = RagdollParams.GetDefaultRagdollParams<HumanRagdollParams>(character.SpeciesName);
                    }
                    return _ragdollParams;
                }
                return character.Info.Ragdoll as HumanRagdollParams;                
            }
            protected set
            {
                if (character.Info == null)
                {
                    _ragdollParams = value;
                }
                else
                {
                    character.Info.Ragdoll = value;
                }
            }
        }

        private HumanWalkParams _humanWalkParams;
        public HumanWalkParams HumanWalkParams
        {
            get
            {
                if (_humanWalkParams == null)
                {
                    _humanWalkParams = HumanWalkParams.GetDefaultAnimParams(character);
                }
                return _humanWalkParams;
            }
            set { _humanWalkParams = value; }
        }

        private HumanRunParams _humanRunParams;
        public HumanRunParams HumanRunParams
        {
            get
            {
                if (_humanRunParams == null)
                {
                    _humanRunParams = HumanRunParams.GetDefaultAnimParams(character);
                }
                return _humanRunParams;
            }
            set { _humanRunParams = value; }
        }

        private HumanCrouchParams _humanCrouchParams;
        public HumanCrouchParams HumanCrouchParams
        {
            get
            {
                if (_humanCrouchParams == null)
                {
                    _humanCrouchParams = HumanCrouchParams.GetDefaultAnimParams(character);
                }
                return _humanCrouchParams;
            }
            set { _humanCrouchParams = value; }
        }

        private HumanSwimSlowParams _humanSwimSlowParams;
        public HumanSwimSlowParams HumanSwimSlowParams
        {
            get
            {
                if (_humanSwimSlowParams == null)
                {
                    _humanSwimSlowParams = HumanSwimSlowParams.GetDefaultAnimParams(character);
                }
                return _humanSwimSlowParams;
            }
            set { _humanSwimSlowParams = value; }
        }

        private HumanSwimFastParams _humanSwimFastParams;
        public HumanSwimFastParams HumanSwimFastParams
        {
            get
            {
                if (_humanSwimFastParams == null)
                {
                    _humanSwimFastParams = HumanSwimFastParams.GetDefaultAnimParams(character);
                }
                return _humanSwimFastParams;
            }
            set { _humanSwimFastParams = value; }
        }

        public new HumanGroundedParams CurrentGroundedParams => base.CurrentGroundedParams as HumanGroundedParams;

        public new HumanSwimParams CurrentSwimParams => base.CurrentSwimParams as HumanSwimParams;

        public IHumanAnimation CurrentHumanAnimParams => CurrentAnimationParams as IHumanAnimation;

        public override GroundedMovementParams WalkParams
        {
            get { return HumanWalkParams; }
            set { HumanWalkParams = value as HumanWalkParams; }
        }

        public override GroundedMovementParams RunParams
        {
            get { return HumanRunParams; }
            set { HumanRunParams = value as HumanRunParams; }
        }

        public override SwimParams SwimSlowParams
        {
            get { return HumanSwimSlowParams; }
            set { HumanSwimSlowParams = value as HumanSwimSlowParams; }
        }

        public override SwimParams SwimFastParams
        {
            get { return HumanSwimFastParams; }
            set { HumanSwimFastParams = value as HumanSwimFastParams; }
        }

        public bool Crouching;

        private float upperLegLength = 0.0f, lowerLegLength = 0.0f;

        private readonly float movementLerp;

        private float cprAnimTimer;
        private float cprPump;

        private bool swimming;
        //time until the character can switch from walking to swimming or vice versa
        //prevents rapid switches between swimming/walking if the water level is fluctuating around the minimum swimming depth
        private float swimmingStateLockTimer;

        public float HeadLeanAmount => CurrentGroundedParams.HeadLeanAmount;
        public float TorsoLeanAmount => CurrentGroundedParams.TorsoLeanAmount;
        public Vector2 FootMoveOffset => CurrentGroundedParams.FootMoveOffset * RagdollParams.JointScale;
        public float LegBendTorque => CurrentGroundedParams.LegBendTorque * RagdollParams.JointScale;
        public Vector2 HandMoveOffset => CurrentGroundedParams.HandMoveOffset * RagdollParams.JointScale;

        public override Vector2 AimSourceSimPos
        {
            get
            {
                float shoulderHeight = Collider.height / 2.0f;
                if (inWater)
                {
                    shoulderHeight += 0.4f;
                }
                else if (Crouching)
                {
                    shoulderHeight -= 0.15f;
                    if (Crouching)
                    {
                        bool movingHorizontally = !MathUtils.NearlyEqual(TargetMovement.X, 0.0f);
                        if (!movingHorizontally)
                        {
                            shoulderHeight -= HumanCrouchParams.MoveDownAmountWhenStationary;
                        }
                    }
                }

                return Collider.SimPosition + new Vector2(
                    (float)Math.Sin(-Collider.Rotation),
                    (float)Math.Cos(-Collider.Rotation)) * shoulderHeight;
            }
        }

        public HumanoidAnimController(Character character, string seed, HumanRagdollParams ragdollParams = null) : base(character, seed, ragdollParams)
        {
            // TODO: load from the character info file?
            movementLerp = RagdollParams.MainElement.GetAttributeFloat("movementlerp", 0.4f);
        }

        public override void Recreate(RagdollParams ragdollParams = null)
        {
            base.Recreate(ragdollParams);
            CalculateLegLengths();
        }

        private void CalculateLegLengths()
        {
            //calculate upper and lower leg length (atm this assumes that both legs are the same size)
            LimbType upperLegType = LimbType.RightThigh;
            LimbType lowerLegType = LimbType.RightLeg;
            LimbType footType = LimbType.RightFoot;

            var waistJoint = GetJointBetweenLimbs(LimbType.Waist, upperLegType) ?? GetJointBetweenLimbs(LimbType.Torso, upperLegType);
            Vector2 localAnchorWaist = Vector2.Zero;
            Vector2 localAnchorKnee = Vector2.Zero;
            if (waistJoint != null)
            {
                localAnchorWaist = waistJoint.LimbA.type == upperLegType ? waistJoint.LocalAnchorA : waistJoint.LocalAnchorB;
            }
            LimbJoint kneeJoint = GetJointBetweenLimbs(upperLegType, lowerLegType);
            if (kneeJoint != null)
            {
                localAnchorKnee = kneeJoint.LimbA.type == upperLegType ? kneeJoint.LocalAnchorA : kneeJoint.LocalAnchorB;
            }
            upperLegLength = Vector2.Distance(localAnchorWaist, localAnchorKnee);

            LimbJoint ankleJoint = GetJointBetweenLimbs(lowerLegType, footType);
            if (ankleJoint == null || kneeJoint == null) { return; }
            lowerLegLength = Vector2.Distance(
                kneeJoint.LimbA.type == lowerLegType ? kneeJoint.LocalAnchorA : kneeJoint.LocalAnchorB,
                ankleJoint.LimbA.type == lowerLegType ? ankleJoint.LocalAnchorA : ankleJoint.LocalAnchorB);
            lowerLegLength += Vector2.Distance(
                ankleJoint.LimbA.type == footType ? ankleJoint.LocalAnchorA : ankleJoint.LocalAnchorB,
                GetLimb(footType).PullJointLocalAnchorA);
        }

        public override void UpdateAnim(float deltaTime)
        {
            if (Frozen) { return; }
            if (MainLimb == null) { return; }

            levitatingCollider = !IsHanging;
            ColliderIndex = Crouching && !swimming ? 1 : 0;
            if ((character.SelectedItem?.GetComponent<Controller>()?.ControlCharacterPose ?? false) ||
                (character.SelectedSecondaryItem?.GetComponent<Controller>()?.ControlCharacterPose ?? false) ||
                character.SelectedSecondaryItem?.GetComponent<Ladder>() != null ||
                (ForceSelectAnimationType != AnimationType.Crouch && ForceSelectAnimationType != AnimationType.NotDefined))
            {
                Crouching = false;
                ColliderIndex = 0;
            }
            else if (!Crouching && ColliderIndex == 1) 
            { 
                Crouching = true; 
            }

            //stun (= disable the animations) if the ragdoll receives a large enough impact
            if (strongestImpact > 0.0f)
            {
                character.SetStun(MathHelper.Min(strongestImpact * 0.5f, 5.0f));
                strongestImpact = 0.0f;
                return;
            }

            if (character.IsDead)
            {
                if (deathAnimTimer < deathAnimDuration)
                {
                    deathAnimTimer += deltaTime;
                    UpdateDying(deltaTime);
                }
            }
            else
            {
                deathAnimTimer = 0.0f;
            } 

            if (!character.CanMove)
            {
                levitatingCollider = false;
                Collider.FarseerBody.FixedRotation = false;
                if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                {
                    Collider.Enabled = false;
                    Collider.LinearVelocity = MainLimb.LinearVelocity;
                    Collider.SetTransformIgnoreContacts(MainLimb.SimPosition, MainLimb.Rotation);
                    //reset pull joints to prevent the character from "hanging" mid-air if pull joints had been active when the character was still moving
                    //(except when dragging, then we need the pull joints)
                    if (!character.CanBeDragged || character.SelectedBy == null) { ResetPullJoints(); }
                }
                return;
            }

            //re-enable collider
            if (!Collider.Enabled)
            {
                var lowestLimb = FindLowestLimb();
                
                Collider.SetTransform(new Vector2(
                    Collider.SimPosition.X,
                    Math.Max(lowestLimb.SimPosition.Y + (Collider.radius + Collider.height / 2), Collider.SimPosition.Y)),
                    Collider.Rotation);
                
                Collider.FarseerBody.ResetDynamics();
                Collider.Enabled = true;
            }            

            if (swimming)
            {
                Collider.FarseerBody.FixedRotation = false;
            }
            else if (!Collider.FarseerBody.FixedRotation)
            {
                if (Math.Abs(MathUtils.GetShortestAngle(Collider.Rotation, 0.0f)) > 0.001f)
                {
                    //rotate collider back upright
                    Collider.AngularVelocity = MathUtils.GetShortestAngle(Collider.Rotation, 0.0f) * 10.0f;
                    Collider.FarseerBody.FixedRotation = false;
                }
                else
                {
                    Collider.FarseerBody.FixedRotation = true;
                }
            }
            else
            {
                float angleDiff = MathUtils.GetShortestAngle(Collider.Rotation, 0.0f);
                if (Math.Abs(angleDiff) > 0.001f)
                {
                    Collider.SetTransform(Collider.SimPosition, Collider.Rotation + angleDiff);
                }
            }
             
            if (character.AnimController.AnimationTestPose)
            {
                ApplyTestPose();
            }
            else if (character.SelectedBy == null)
            {
                if (character.LockHands)
                {
                    var leftHand = GetLimb(LimbType.LeftHand);
                    var rightHand = GetLimb(LimbType.RightHand);

                    var waist = GetLimb(LimbType.Waist) ?? GetLimb(LimbType.Torso);

                    rightHand.Disabled = true;
                    leftHand.Disabled = true;

                    Vector2 midPos = waist.SimPosition;
                    Matrix torsoTransform = Matrix.CreateRotationZ(waist.Rotation);

                    midPos += Vector2.Transform(new Vector2(-0.3f * Dir, -0.2f), torsoTransform);
                    if (rightHand.PullJointEnabled) midPos = (midPos + rightHand.PullJointWorldAnchorB) / 2.0f;
                    HandIK(rightHand, midPos, CurrentAnimationParams.ArmIKStrength, CurrentAnimationParams.HandIKStrength);
                    HandIK(leftHand, midPos, CurrentAnimationParams.ArmIKStrength, CurrentAnimationParams.HandIKStrength);
                }
                if (Anim != Animation.UsingItem)
                {
                    if (Anim != Animation.UsingItemWhileClimbing)
                    {
                        ResetPullJoints();
                    }
                    else
                    {
                        ResetPullJoints(l => l.IsLowerBody);
                    }
                }
            }

            if (SimplePhysicsEnabled)
            {
                UpdateStandingSimple();
                IsHanging = false;
                return;
            }

            if (character.SelectedCharacter != null)
            {
                DragCharacter(character.SelectedCharacter, deltaTime);
            }

            switch (Anim)
            {
                case Animation.Climbing:
                case Animation.UsingItemWhileClimbing:
                    levitatingCollider = false;
                    UpdateClimbing();
                    UpdateUseItemTimer();
                    break;
                case Animation.CPR:
                    UpdateCPR(deltaTime);
                    break;
                case Animation.UsingItem:
                default:
                    UpdateUseItemTimer();
                    swimmingStateLockTimer -= deltaTime;
                    if (forceStanding || character.AnimController.AnimationTestPose)
                    {
                        swimming = false;
                    }
                    else if (swimming != inWater && swimmingStateLockTimer <= 0.0f)
                    {
                        //0.5 second delay for switching between swimming and walking
                        //prevents rapid switches between swimming/walking if the water level is fluctuating around the minimum swimming depth
                        swimming = inWater;
                        swimmingStateLockTimer = 0.5f;
                    }
                    if (swimming)
                    {
                        UpdateSwimming();
                    }
                    else if (character.SelectedItem == null || !(character.SelectedSecondaryItem?.GetComponent<Controller>() is { } controller) ||
                             !controller.ControlCharacterPose || !controller.UserInCorrectPosition)
                    {
                        UpdateStanding();
                    }
                    break;
            }

            void UpdateUseItemTimer()
            {
                if (IsUsingItem)
                {
                    useItemTimer -= deltaTime;
                    if (useItemTimer <= 0.0f)
                    {
                        StopUsingItem();
                    }
                }
            }

            if (Timing.TotalTime > LockFlippingUntil && TargetDir != dir && !IsStuck)
            {
                Flip();
            }

            foreach (Limb limb in Limbs)
            {
                limb.Disabled = false;
            }
            wasAiming = aiming;
            aiming = false;
            wasAimingMelee = aimingMelee;
            aimingMelee = false;
            IsHanging = false;
        }

        void UpdateStanding()
        {
            if (CurrentGroundedParams == null) { return; }
            Vector2 handPos;

            Limb leftFoot = GetLimb(LimbType.LeftFoot);
            Limb rightFoot = GetLimb(LimbType.RightFoot);
            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);

            Limb waist = GetLimb(LimbType.Waist);

            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Limb leftLeg = GetLimb(LimbType.LeftLeg);
            Limb rightLeg = GetLimb(LimbType.RightLeg);

            float walkCycleMultiplier = 1.0f;
            if (Stairs != null)
            {
                TargetMovement = new Vector2(MathHelper.Clamp(TargetMovement.X, -1.7f, 1.7f), TargetMovement.Y);                
                walkCycleMultiplier *= 1.5f;                
            }

            float getUpForce = CurrentGroundedParams.GetUpForce / RagdollParams.JointScale;

            Vector2 colliderPos = GetColliderBottom();
            if (Math.Abs(TargetMovement.X) > 1.0f)
            {
                float slowdownAmount = 0.0f;
                if (currentHull != null)
                {
                    //TODO: take into account that the feet aren't necessarily in CurrentHull
                    //full slowdown (1.5f) when water is up to the torso
                    surfaceY = ConvertUnits.ToSimUnits(currentHull.Surface);
                    float bottomPos = Math.Max(colliderPos.Y, ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height));
                    slowdownAmount = MathHelper.Clamp((surfaceY - bottomPos) / TorsoPosition.Value, 0.0f, 1.0f) * 1.5f;
                }

                float maxSpeed = Math.Max(TargetMovement.Length() - slowdownAmount, 1.0f);
                TargetMovement = Vector2.Normalize(TargetMovement) * maxSpeed;
            }

            float walkPosX = (float)Math.Cos(WalkPos);
            float walkPosY = (float)Math.Sin(WalkPos);
            
            Vector2 stepSize = StepSize.Value;
            stepSize.X *= walkPosX;
            stepSize.Y *= walkPosY;

            float footMid = colliderPos.X;

            var herpes = character.CharacterHealth.GetAffliction("spaceherpes", false);
            float herpesAmount = herpes == null ? 0 : herpes.Strength / herpes.Prefab.MaxStrength;
            float legDamage = character.GetLegPenalty(startSum: -0.1f) * 1.1f;
            float limpAmount = MathHelper.Lerp(0, 1, legDamage + herpesAmount);
            if (limpAmount > 0.0f)
            {
                //make the footpos oscillate when limping
                footMid += (Math.Max(Math.Abs(walkPosX) * limpAmount, 0.0f) * Math.Min(Math.Abs(TargetMovement.X), 0.3f)) * Dir;
            }

            movement = overrideTargetMovement == Vector2.Zero ?
                MathUtils.SmoothStep(movement, TargetMovement, movementLerp) :
                overrideTargetMovement;

            if (Math.Abs(movement.X) < 0.005f)
            {
                movement.X = 0.0f;
            }

            movement.Y = 0.0f;

            if (head == null) { return; }
            if (torso == null) { return; }

            bool isNotRemote = true;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { isNotRemote = !character.IsRemotelyControlled; }

            if (onGround && isNotRemote)
            {
                //move slower if collider isn't upright
                float rotationFactor = (float)Math.Abs(Math.Cos(Collider.Rotation));

                Collider.LinearVelocity = new Vector2(
                        movement.X * rotationFactor,
                        Collider.LinearVelocity.Y > 0.0f ? Collider.LinearVelocity.Y * 0.5f : Collider.LinearVelocity.Y);
            }

            getUpForce *= Math.Max(head.SimPosition.Y - colliderPos.Y, 0.5f);

            torso.PullJointEnabled = true;
            head.PullJointEnabled = true;
            if (waist != null)
            {
                waist.PullJointEnabled = true;
            }
            
            bool onSlope = Math.Abs(movement.X) > 0.01f && Math.Abs(floorNormal.X) > 0.1f && Math.Sign(floorNormal.X) != Math.Sign(movement.X);

            bool movingHorizontally = !MathUtils.NearlyEqual(TargetMovement.X, 0.0f);

            if (Stairs != null || onSlope)
            {
                torso.PullJointWorldAnchorB = new Vector2(
                    MathHelper.SmoothStep(torso.SimPosition.X, footMid + movement.X * TorsoLeanAmount, getUpForce * 0.8f),
                    MathHelper.SmoothStep(torso.SimPosition.Y, colliderPos.Y + TorsoPosition.Value - Math.Abs(walkPosX * 0.05f), getUpForce * 2.0f));

                head.PullJointWorldAnchorB = new Vector2(
                    MathHelper.SmoothStep(head.SimPosition.X, footMid + movement.X * HeadLeanAmount, getUpForce * 0.8f),
                    MathHelper.SmoothStep(head.SimPosition.Y, colliderPos.Y + HeadPosition.Value - Math.Abs(walkPosX * 0.05f), getUpForce * 2.0f));

                if (waist != null)
                {
                    waist.PullJointWorldAnchorB = waist.SimPosition - movement * 0.06f;
                }
            }
            else
            {
                if (!onGround)
                {
                    movement = Vector2.Zero;
                }
                
                float stepLift = TargetMovement.X == 0.0f ? 0 : 
                    (float)Math.Sin(WalkPos * CurrentGroundedParams.StepLiftFrequency + MathHelper.Pi * CurrentGroundedParams.StepLiftOffset) * (CurrentGroundedParams.StepLiftAmount / 100);

                float y = colliderPos.Y + stepLift;

                if (!torso.Disabled)
                {
                    if (TorsoPosition.HasValue)  { y += TorsoPosition.Value;  }
                    if (Crouching && !movingHorizontally) { y -= HumanCrouchParams.MoveDownAmountWhenStationary; }
                    torso.PullJointWorldAnchorB =
                        MathUtils.SmoothStep(torso.SimPosition,
                        new Vector2(footMid + movement.X * TorsoLeanAmount, y), getUpForce);
                }

                if (!head.Disabled)
                {
                    y = colliderPos.Y + stepLift * CurrentGroundedParams.StepLiftHeadMultiplier;
                    if (HeadPosition.HasValue) { y += HeadPosition.Value; }
                    if (Crouching && !movingHorizontally) { y -= HumanCrouchParams.MoveDownAmountWhenStationary; }
                    head.PullJointWorldAnchorB =
                        MathUtils.SmoothStep(head.SimPosition,
                        new Vector2(footMid + movement.X * HeadLeanAmount, y), getUpForce * 1.2f);
                }

                if (waist != null && !waist.Disabled)
                {
                    waist.PullJointWorldAnchorB = waist.SimPosition + movement * 0.06f;
                }
            }

            if (TorsoAngle.HasValue && !torso.Disabled)
            {
                float torsoAngle = TorsoAngle.Value;
                float herpesStrength = character.CharacterHealth.GetAfflictionStrength("spaceherpes");
                if (Crouching && !movingHorizontally && !Aiming) { torsoAngle -= HumanCrouchParams.ExtraTorsoAngleWhenStationary; }
                torsoAngle -= herpesStrength / 150.0f;
                torso.body.SmoothRotate(torsoAngle * Dir, CurrentGroundedParams.TorsoTorque);
            }
            if (!head.Disabled)
            {
                if (!Aiming && CurrentGroundedParams.FixedHeadAngle && HeadAngle.HasValue)
                {
                    float headAngle = HeadAngle.Value;
                    if (Crouching && !movingHorizontally) { headAngle -= HumanCrouchParams.ExtraHeadAngleWhenStationary; }
                    head.body.SmoothRotate(headAngle * Dir, CurrentGroundedParams.HeadTorque);
                }
                else
                {
                    RotateHead(head);
                }
            }

            if (!onGround)
            {
                Vector2 move = torso.PullJointWorldAnchorB - torso.SimPosition;

                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    MoveLimb(limb, limb.SimPosition + move, 15.0f, true);
                }

                return;
            }

            Vector2 waistPos = waist != null ? waist.SimPosition : torso.SimPosition;

            if (movingHorizontally)
            {
                //progress the walking animation
                WalkPos -= MathHelper.ToRadians(CurrentAnimationParams.CycleSpeed) * walkCycleMultiplier * movement.X;

                for (int i = -1; i < 2; i += 2)
                {
                    Limb foot = i == -1 ? leftFoot : rightFoot;
                    if (foot == null) { continue; }

                    Vector2 footPos = stepSize * -i;
                    footPos += new Vector2(Math.Sign(movement.X) * FootMoveOffset.X, FootMoveOffset.Y);

                    if (footPos.Y < 0.0f) { footPos.Y = -0.15f; }

                    //make the character limp if the feet are damaged
                    float footAfflictionStrength = character.CharacterHealth.GetAfflictionStrength("damage", foot, true);
                    footPos.X *= MathHelper.Lerp(1.0f, 0.75f, MathHelper.Clamp(footAfflictionStrength / 50.0f, 0.0f, 1.0f));

                    if (CurrentGroundedParams.FootLiftHorizontalFactor > 0)
                    {
                        // Calculate the foot y dynamically based on the foot position relative to the waist,
                        // so that the foot aims higher when it's behind the waist and lower when it's in the front.
                        float xDiff = (foot.SimPosition.X - waistPos.X + FootMoveOffset.X) * Dir;
                        float min = MathUtils.InverseLerp(1, 0, CurrentGroundedParams.FootLiftHorizontalFactor);
                        float max = 1 + MathUtils.InverseLerp(0, 1, CurrentGroundedParams.FootLiftHorizontalFactor);
                        float xFactor = MathHelper.Lerp(min, max, MathUtils.InverseLerp(RagdollParams.JointScale, -RagdollParams.JointScale, xDiff));
                        footPos.Y *= xFactor;
                    }

                    if (onSlope && Stairs == null)
                    {
                        footPos.Y *= 2.0f;
                    }
                    footPos.Y = Math.Min(waistPos.Y - colliderPos.Y - 0.4f, footPos.Y);

#if CLIENT
                    if ((i == 1 && Math.Sign(Math.Sin(WalkPos)) > 0 && Math.Sign(walkPosY) < 0) ||
                        (i == -1 && Math.Sign(Math.Sin(WalkPos)) < 0 && Math.Sign(walkPosY) > 0))
                    {
                        PlayImpactSound(foot);
                    }

#endif

                    if (!foot.Disabled)
                    {
                        foot.DebugRefPos = colliderPos;
                        foot.DebugTargetPos = colliderPos + footPos;
                        MoveLimb(foot, colliderPos + footPos, CurrentGroundedParams.FootMoveStrength);
                        FootIK(foot, colliderPos + footPos, 
                            CurrentGroundedParams.LegBendTorque, CurrentGroundedParams.FootTorque, CurrentGroundedParams.FootAngleInRadians);
                    }
                }

                //calculate the positions of hands
                handPos = torso.SimPosition;
                handPos.X = -walkPosX * CurrentGroundedParams.HandMoveAmount.X;

                float lowerY = CurrentGroundedParams.HandClampY;

                handPos.Y = lowerY + (float)(Math.Abs(Math.Sin(WalkPos - Math.PI * 1.5f) * CurrentGroundedParams.HandMoveAmount.Y));

                Vector2 posAddition = new Vector2(Math.Sign(movement.X) * HandMoveOffset.X, HandMoveOffset.Y);

                if (rightHand != null && !rightHand.Disabled)
                {
                    HandIK(rightHand,
                        torso.SimPosition + posAddition + new Vector2(-handPos.X, (Math.Sign(walkPosX) == Math.Sign(Dir)) ? handPos.Y : lowerY),
                        CurrentGroundedParams.ArmMoveStrength, CurrentGroundedParams.HandMoveStrength);
                }
                if (leftHand != null && !leftHand.Disabled)
                {
                    HandIK(leftHand,
                        torso.SimPosition + posAddition + new Vector2(handPos.X, (Math.Sign(walkPosX) == Math.Sign(-Dir)) ? handPos.Y : lowerY),
                        CurrentGroundedParams.ArmMoveStrength, CurrentGroundedParams.HandMoveStrength);
                }
            }
            else
            {
                for (int i = -1; i < 2; i += 2)
                {
                    Vector2 footPos = colliderPos;
                    if (Crouching)
                    {
                        footPos = new Vector2(Math.Sign(stepSize.X * i) * Dir * 0.35f, colliderPos.Y);
                        if (Math.Sign(footPos.X) != Math.Sign(Dir))
                        {
                            //lift the foot at the back up a bit
                            footPos.Y += 0.15f;
                        }
                        footPos.X += colliderPos.X;
                    }
                    else
                    {
                        footPos = new Vector2(colliderPos.X + stepSize.X * i * 0.2f, colliderPos.Y - 0.1f);
                    }
                    if (Stairs == null)
                    {
                        footPos.Y = Math.Max(Math.Min(FloorY, footPos.Y + 0.5f), footPos.Y);
                    }
                    var foot = i == -1 ? rightFoot : leftFoot;
                    if (foot != null && !foot.Disabled)
                    {
                        foot.DebugRefPos = colliderPos;
                        foot.DebugTargetPos = footPos;
                        float footMoveForce = CurrentGroundedParams.FootMoveStrength;
                        float legBendTorque = CurrentGroundedParams.LegBendTorque;
                        if (Crouching)
                        {
                            // Keeps the pose
                            legBendTorque = 100;
                            footMoveForce *= 2;
                        }
                        MoveLimb(foot, footPos, footMoveForce);
                        FootIK(foot, footPos, legBendTorque, CurrentGroundedParams.FootTorque, CurrentGroundedParams.FootAngleInRadians);
                    }
                }

                for (int i = 0; i < 2; i++)
                {
                    var hand = i == 0 ? rightHand : leftHand;
                    if (hand == null || hand.Disabled) { continue; }

                    var armType = i == 0 ? LimbType.RightArm : LimbType.LeftArm;
                    var foreArmType = i == 0 ? LimbType.RightForearm : LimbType.LeftForearm;

                    //get the upper arm to point downwards
                    var arm = GetLimb(armType);
                    if (arm != null && Math.Abs(arm.body.AngularVelocity) < 10.0f)
                    {
                        arm.body.SmoothRotate(MathHelper.Clamp(-arm.body.AngularVelocity, -0.5f, 0.5f), arm.Mass * 50.0f * CurrentGroundedParams.ArmMoveStrength);
                    }

                    //get the elbow to a neutral rotation
                    if (Math.Abs(hand.body.AngularVelocity) < 10.0f)
                    {
                        var forearm = GetLimb(foreArmType) ?? hand;
                        LimbJoint elbow = GetJointBetweenLimbs(armType, foreArmType) ?? GetJointBetweenLimbs(armType, hand.type);
                        if (elbow != null)
                        {
                            float diff = elbow.JointAngle - (Dir > 0 ? elbow.LowerLimit : elbow.UpperLimit);
                            forearm.body.ApplyTorque(MathHelper.Clamp(-diff, -MathHelper.PiOver2, MathHelper.PiOver2) * forearm.Mass * 100.0f * CurrentGroundedParams.ArmMoveStrength);
                        }
                    }
                    // Try to keep the wrist straight
                    LimbJoint wrist = GetJointBetweenLimbs(foreArmType, hand.type);
                    if (wrist != null)
                    {
                        hand.body.ApplyTorque(MathHelper.Clamp(-wrist.JointAngle, -MathHelper.PiOver2, MathHelper.PiOver2) * hand.Mass * 100f * CurrentGroundedParams.HandMoveStrength);
                    }
                }
            }
        }

        void UpdateStandingSimple()
        {
            if (Math.Abs(movement.X) < 0.005f)
            {
                movement.X = 0.0f;
            }
            movement = MathUtils.SmoothStep(movement, TargetMovement, movementLerp);

            if (InWater)
            {
                Collider.LinearVelocity = movement;
            }
            else if (onGround && (!character.IsRemotelyControlled || (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)))
            {
                Collider.LinearVelocity = new Vector2(
                        movement.X,
                        Collider.LinearVelocity.Y > 0.0f ? Collider.LinearVelocity.Y * 0.5f : Collider.LinearVelocity.Y);                
            }
        }
        
        private float handCyclePos;
        private float legCyclePos;
        void UpdateSwimming()
        {
            if (CurrentSwimParams == null) { return; }
            IgnorePlatforms = true;

            Vector2 footPos, handPos;

            float surfaceLimiter = 1.0f;

            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);
            if (head == null) { return; }
            if (torso == null) { return; }

            const float DisableMovementAboveSurfaceThreshold = 50.0f;

            if (currentHull != null && character.CurrentHull != null)
            {
                float surfacePos = GetSurfaceY();
                float surfaceThreshold = ConvertUnits.ToDisplayUnits(Collider.SimPosition.Y + 1.0f);
                surfaceLimiter = Math.Max(1.0f, surfaceThreshold - surfacePos);
                if (surfaceLimiter > DisableMovementAboveSurfaceThreshold) { return; }  
            }          

            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Limb leftFoot = GetLimb(LimbType.LeftFoot);
            Limb rightFoot = GetLimb(LimbType.RightFoot);
            
            float rotation = MathHelper.WrapAngle(Collider.Rotation);
            rotation = MathHelper.ToDegrees(rotation);
            if (rotation < 0.0f)
            {
                rotation += 360;
            }
            float targetSpeed = TargetMovement.Length();
            if (targetSpeed > 0.1f && !character.IsRemotelyControlled && !Aiming)
            {
                if (!IsUsingItem &&
                    !(character.SelectedItem?.GetComponent<Controller>()?.ControlCharacterPose ?? false) &&
                    !(character.SelectedSecondaryItem?.GetComponent<Controller>()?.ControlCharacterPose ?? false))
                {
                    if (rotation > 20 && rotation < 170)
                    {
                        TargetDir = Direction.Left;
                    }
                    else if (rotation > 190 && rotation < 340)
                    {
                        TargetDir = Direction.Right;
                    }
                }
            }
            if (Aiming)
            {
                Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);
                Vector2 diff = (mousePos - torso.SimPosition) * Dir;
                if (diff.LengthSquared() > MathUtils.Pow2(0.4f))
                {
                    float newRotation = MathHelper.WrapAngle(MathUtils.VectorToAngle(diff) - MathHelper.PiOver4 * Dir);
                    Collider.SmoothRotate(newRotation, CurrentSwimParams.SteerTorque * character.SpeedMultiplier);
                }
            }
            else if (targetSpeed > 0.1f)
            {
                float newRotation = MathUtils.VectorToAngle(TargetMovement) - MathHelper.PiOver2;
                Collider.SmoothRotate(newRotation, CurrentSwimParams.SteerTorque * character.SpeedMultiplier);
            }

            torso.body.MoveToPos(Collider.SimPosition + new Vector2((float)Math.Sin(-Collider.Rotation), (float)Math.Cos(-Collider.Rotation)) * 0.4f, 5.0f);

            movement = MathUtils.SmoothStep(movement, TargetMovement, 0.3f);

            if (TorsoAngle.HasValue)
            {
                torso.body.SmoothRotate(Collider.Rotation + TorsoAngle.Value * Dir, CurrentSwimParams.TorsoTorque);
            }
            else
            {
                torso.body.SmoothRotate(Collider.Rotation, CurrentSwimParams.TorsoTorque);
            }

            if (!Aiming && CurrentSwimParams.FixedHeadAngle && HeadAngle.HasValue)
            {
                head.body.SmoothRotate(Collider.Rotation + HeadAngle.Value * Dir, CurrentSwimParams.HeadTorque);
            }
            else
            {
                RotateHead(head);
            }

            //dont try to move upwards if head is already out of water
            if (surfaceLimiter > 1.0f && TargetMovement.Y > 0.0f)
            {
                if (TargetMovement.X == 0.0f)
                {
                    //pull head above water
                    head.body.SmoothRotate(0.0f, 5.0f);
                    WalkPos += 0.05f;
                }
                else
                {
                    TargetMovement = new Vector2(
                        (float)Math.Sqrt(targetSpeed * targetSpeed - TargetMovement.Y * TargetMovement.Y)
                        * Math.Sign(TargetMovement.X),
                        Math.Max(TargetMovement.Y, TargetMovement.Y * 0.2f));

                    //turn head above the water
                    head.body.ApplyTorque(Dir);
                }

                movement.Y = movement.Y * (1.0f - ((surfaceLimiter - 1.0f) / DisableMovementAboveSurfaceThreshold));
            }

            bool isNotRemote = true;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { isNotRemote = !character.IsRemotelyControlled; }

            if (isNotRemote)
            {
                float t = movementLerp;
                if (targetSpeed > 0.00001f && !SimplePhysicsEnabled)
                {
                    Vector2 forward = VectorExtensions.Forward(Collider.Rotation + MathHelper.PiOver2);
                    float dot = Vector2.Dot(forward, Vector2.Normalize(movement));
                    if (dot < 0)
                    {
                        // Reduce the linear movement speed when not facing the movement direction
                        t = MathHelper.Clamp((1 + dot) / 10, 0.01f, 0.1f);
                    }
                }
                Collider.LinearVelocity = Vector2.Lerp(Collider.LinearVelocity, movement, t);
            }

            WalkPos += movement.Length();
            legCyclePos += Math.Min(movement.LengthSquared() + Collider.AngularVelocity, 1.0f);
            handCyclePos += MathHelper.ToRadians(CurrentSwimParams.HandCycleSpeed) * Math.Sign(movement.X);

            float legMoveMultiplier = 1.0f;
            if (movement.LengthSquared() < 0.001f)
            {
                // Swimming in place (TODO: expose?)
                legMoveMultiplier = 0.3f;
                legCyclePos += 0.4f;
                handCyclePos += 0.1f;
            }

            var waist = GetLimb(LimbType.Waist) ?? GetLimb(LimbType.Torso);
            footPos = waist == null ? Vector2.Zero : waist.SimPosition - new Vector2((float)Math.Sin(-Collider.Rotation), (float)Math.Cos(-Collider.Rotation)) * (upperLegLength + lowerLegLength);
            Vector2 transformedFootPos = new Vector2((float)Math.Sin(legCyclePos / CurrentSwimParams.LegCycleLength) * CurrentSwimParams.LegMoveAmount * legMoveMultiplier, 0.0f);
            transformedFootPos = Vector2.Transform(transformedFootPos, Matrix.CreateRotationZ(Collider.Rotation));

            float legTorque = CurrentSwimParams.LegTorque * character.SpeedMultiplier * (1.2f - character.GetLegPenalty());
            if (rightFoot != null && !rightFoot.Disabled)
            {
                FootIK(rightFoot, footPos - transformedFootPos, legTorque, CurrentSwimParams.FootTorque, CurrentSwimParams.FootAngleInRadians);
            }
            if (leftFoot != null && !leftFoot.Disabled)
            {
                FootIK(leftFoot, footPos + transformedFootPos, legTorque, CurrentSwimParams.FootTorque, CurrentSwimParams.FootAngleInRadians);
            }

            handPos = (torso.SimPosition + head.SimPosition) / 2.0f;

            //at the surface, not moving sideways OR not moving at all
            // -> hands just float around
            if ((!headInWater && TargetMovement.X == 0.0f && TargetMovement.Y > 0) || TargetMovement.LengthSquared() < 0.001f)
            {
                handPos += MathUtils.RotatePoint(Vector2.UnitX * Dir * 0.2f, torso.Rotation);

                float wobbleAmount = 0.1f;

                if (rightHand != null && !rightHand.Disabled)
                {
                    MoveLimb(rightHand, new Vector2(
                        handPos.X + (float)Math.Sin(handCyclePos / 1.5f) * wobbleAmount,
                        handPos.Y + (float)Math.Sin(handCyclePos / 3.5f) * wobbleAmount - 0.25f), CurrentSwimParams.ArmMoveStrength);
                }

                if (leftHand != null && !leftHand.Disabled)
                {
                    MoveLimb(leftHand, new Vector2(
                        handPos.X + (float)Math.Sin(handCyclePos / 2.0f) * wobbleAmount,
                        handPos.Y + (float)Math.Sin(handCyclePos / 3.0f) * wobbleAmount - 0.25f), CurrentSwimParams.ArmMoveStrength);
                }

                return;
            }

            handPos += head.LinearVelocity.ClampLength(1.0f) * 0.1f;

            // Not sure why the params has to be flipped, but it works.
            var handMoveAmount = CurrentSwimParams.HandMoveAmount.Flip();
            var handMoveOffset = CurrentSwimParams.HandMoveOffset.Flip();
            float handPosX = (float)Math.Cos(handCyclePos) * handMoveAmount.X * CurrentAnimationParams.CycleSpeed;
            float handPosY = (float)Math.Sin(handCyclePos) * handMoveAmount.Y * CurrentAnimationParams.CycleSpeed;

            Matrix rotationMatrix = Matrix.CreateRotationZ(torso.Rotation);

            if (rightHand != null && !rightHand.Disabled)
            {
                Vector2 rightHandPos = new Vector2(-handPosX, -handPosY) + handMoveOffset;
                rightHandPos.X = (Dir == 1.0f) ? Math.Max(0.3f, rightHandPos.X) : Math.Min(-0.3f, rightHandPos.X);
                rightHandPos = Vector2.Transform(rightHandPos, rotationMatrix);
                float speedMultiplier = Math.Min(character.SpeedMultiplier * (1 - Character.GetRightHandPenalty()), 1.0f);
                if (character.Inventory != null && character.Inventory.GetItemInLimbSlot(InvSlotType.RightHand) != null)
                {
                    speedMultiplier = Math.Min(speedMultiplier, 0.1f);
                }
                HandIK(rightHand, handPos + rightHandPos, CurrentSwimParams.ArmMoveStrength * speedMultiplier, CurrentSwimParams.HandMoveStrength * speedMultiplier);
                // Try to keep the wrist straight
                LimbJoint wrist = GetJointBetweenLimbs(LimbType.RightForearm, LimbType.RightHand);
                if (wrist != null)
                {
                    rightHand.body.ApplyTorque(MathHelper.Clamp(-wrist.JointAngle, -MathHelper.PiOver2, MathHelper.PiOver2) * rightHand.Mass * 100f * CurrentSwimParams.HandMoveStrength);
                }
            }

            if (leftHand != null && !leftHand.Disabled)
            {
                Vector2 leftHandPos = new Vector2(handPosX, handPosY) + handMoveOffset;
                leftHandPos.X = (Dir == 1.0f) ? Math.Max(0.3f, leftHandPos.X) : Math.Min(-0.3f, leftHandPos.X);
                leftHandPos = Vector2.Transform(leftHandPos, rotationMatrix);
                float speedMultiplier = Math.Min(character.SpeedMultiplier * (1 - Character.GetLeftHandPenalty()), 1.0f);
                if (character.Inventory != null && character.Inventory.GetItemInLimbSlot(InvSlotType.LeftHand) != null)
                {
                    speedMultiplier = Math.Min(speedMultiplier, 0.1f);
                }
                HandIK(leftHand, handPos + leftHandPos, CurrentSwimParams.ArmMoveStrength * speedMultiplier, CurrentSwimParams.HandMoveStrength * speedMultiplier);
                // Try to keep the wrist straight
                LimbJoint wrist = GetJointBetweenLimbs(LimbType.LeftForearm, LimbType.LeftHand);
                if (wrist != null)
                {
                    leftHand.body.ApplyTorque(MathHelper.Clamp(-wrist.JointAngle, -MathHelper.PiOver2, MathHelper.PiOver2) * leftHand.Mass * 100f * CurrentSwimParams.HandMoveStrength);
                }
            }
        }

        private float prevFootPos;

        void UpdateClimbing()
        {
            var ladder = character.SelectedSecondaryItem?.GetComponent<Ladder>();
            if (character.IsIncapacitated)
            {
                Anim = Animation.None;
                return;
            }
            else if (ladder == null)
            {
                StopClimbing();
                return;
            }

            onGround = false;
            IgnorePlatforms = true;

            bool climbFast = targetMovement.Y > 3.0f;
            bool slide = targetMovement.Y < -1.1f;
            Vector2 tempTargetMovement = TargetMovement;
            tempTargetMovement.Y = climbFast ?
                Math.Min(tempTargetMovement.Y, 2.0f) :
                Math.Min(tempTargetMovement.Y, 1.0f);

            movement = MathUtils.SmoothStep(movement, tempTargetMovement, 0.3f);

            Limb leftFoot   = GetLimb(LimbType.LeftFoot);
            Limb rightFoot  = GetLimb(LimbType.RightFoot);
            Limb head       = GetLimb(LimbType.Head);
            Limb torso      = GetLimb(LimbType.Torso);

            Limb leftHand   = GetLimb(LimbType.LeftHand);
            Limb rightHand  = GetLimb(LimbType.RightHand);

            if (leftHand == null || rightHand == null || head == null || torso == null) { return; }

            Vector2 ladderSimPos = ConvertUnits.ToSimUnits(
                ladder.Item.Rect.X + ladder.Item.Rect.Width / 2.0f,
                ladder.Item.Rect.Y);

            Vector2 ladderSimSize = ConvertUnits.ToSimUnits(ladder.Item.Rect.Size.ToVector2());

            float lowestLadderSimPos = ladderSimPos.Y - ladderSimPos.Y;
            var lowestNearbyLadder = GetLowestNearbyLadder(ladder);
            if (lowestNearbyLadder != null && lowestNearbyLadder != ladder)
            {
                ladderSimSize.Y = ConvertUnits.ToSimUnits(ladder.Item.WorldRect.Y - (lowestNearbyLadder.Item.WorldRect.Y - lowestNearbyLadder.Item.Rect.Size.Y));
            }

            float stepHeight = ConvertUnits.ToSimUnits(30.0f);
            if (climbFast) { stepHeight *= 2; }

            if (currentHull == null && ladder.Item.Submarine != null)
            {
                ladderSimPos += ladder.Item.Submarine.SimPosition;
            }
            else if (currentHull?.Submarine != null && currentHull.Submarine != ladder.Item.Submarine && ladder.Item.Submarine != null)
            {
                ladderSimPos += ladder.Item.Submarine.SimPosition - currentHull.Submarine.SimPosition;
            }
            else if (currentHull?.Submarine != null && ladder.Item.Submarine == null)
            {
                ladderSimPos -= currentHull.Submarine.SimPosition;
            }

            float bottomPos = Collider.SimPosition.Y - ColliderHeightFromFloor - Collider.radius - Collider.height / 2.0f;
            float torsoPos = TorsoPosition ?? 0;
            MoveLimb(torso, new Vector2(ladderSimPos.X - 0.35f * Dir, bottomPos + torsoPos), 10.5f);
            float headPos = HeadPosition ?? 0;
            MoveLimb(head, new Vector2(ladderSimPos.X - 0.2f * Dir, bottomPos + headPos), 10.5f);            

            Collider.MoveToPos(new Vector2(ladderSimPos.X - 0.1f * Dir, Collider.SimPosition.Y), 10.5f);
            
            Vector2 handPos = new Vector2(
                ladderSimPos.X,
                bottomPos + torsoPos + movement.Y * 0.1f - ladderSimPos.Y);
            if (climbFast) { handPos.Y -= stepHeight; }

            //prevent the hands from going above the top of the ladders
            handPos.Y = Math.Min(-0.5f, handPos.Y);
            if (!Aiming || !(character.Inventory?.GetItemInLimbSlot(InvSlotType.RightHand)?.GetComponent<Holdable>()?.ControlPose ?? false) || Math.Abs(movement.Y) > 0.01f)
            {
                MoveLimb(rightHand,
                    new Vector2(slide ? handPos.X + ladderSimSize.X * 0.5f : handPos.X,
                    (slide ? handPos.Y : MathUtils.Round(handPos.Y, stepHeight * 2.0f)) + ladderSimPos.Y),
                    5.2f);
                rightHand.body.ApplyTorque(Dir * 2.0f);
            }
            if (!Aiming || !(character.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand)?.GetComponent<Holdable>()?.ControlPose ?? false) || Math.Abs(movement.Y) > 0.01f)
            {
                MoveLimb(leftHand,
                    new Vector2(handPos.X - ladderSimSize.X * 0.5f,
                    (slide ? handPos.Y : MathUtils.Round(handPos.Y - stepHeight, stepHeight * 2.0f) + stepHeight) + ladderSimPos.Y),
                    5.2f); ;
                leftHand.body.ApplyTorque(Dir * 2.0f);
            }

            Vector2 footPos = new Vector2(
                handPos.X - Dir * 0.05f,
                bottomPos + ColliderHeightFromFloor - stepHeight * 2.7f - ladderSimPos.Y);
            if (climbFast) { footPos.Y += stepHeight; }

            //apply torque to the legs to make the knees bend
            Limb leftLeg = GetLimb(LimbType.LeftLeg);
            Limb rightLeg = GetLimb(LimbType.RightLeg);

            //only move the feet if they're above the bottom of the ladders
            //(if not, they'll just dangle in air, and the character holds itself up with it's arms)
            if (footPos.Y > -ladderSimSize.Y - 0.2f && leftFoot != null && rightFoot != null)
            {
                Limb refLimb = GetLimb(LimbType.Waist) ?? GetLimb(LimbType.Torso);
                bool leftLegBackwards = Math.Abs(leftLeg.body.Rotation - refLimb.body.Rotation) > MathHelper.Pi;
                bool rightLegBackwards = Math.Abs(rightLeg.body.Rotation - refLimb.body.Rotation) > MathHelper.Pi;

                if (slide)
                {
                    if (!leftLegBackwards) { MoveLimb(leftFoot, new Vector2(footPos.X - ladderSimSize.X * 0.5f, footPos.Y + ladderSimPos.Y), 15.5f, true); }
                    if (!rightLegBackwards) { MoveLimb(rightFoot, new Vector2(footPos.X, footPos.Y + ladderSimPos.Y), 15.5f, true); }
                }
                else
                {
                    float leftFootPos = MathUtils.Round(footPos.Y + stepHeight, stepHeight * 2.0f) - stepHeight;
                    float prevLeftFootPos = MathUtils.Round(prevFootPos + stepHeight, stepHeight * 2.0f) - stepHeight;
                    if (!leftLegBackwards) { MoveLimb(leftFoot, new Vector2(footPos.X, leftFootPos + ladderSimPos.Y), 15.5f, true); }

                    float rightFootPos = MathUtils.Round(footPos.Y, stepHeight * 2.0f);
                    float prevRightFootPos = MathUtils.Round(prevFootPos, stepHeight * 2.0f);
                    if (!rightLegBackwards) { MoveLimb(rightFoot, new Vector2(footPos.X, rightFootPos + ladderSimPos.Y), 15.5f, true); }
#if CLIENT
                    if (Math.Abs(leftFootPos - prevLeftFootPos) > stepHeight && leftFoot.LastImpactSoundTime < Timing.TotalTime - Limb.SoundInterval)
                    {
                        SoundPlayer.PlaySound("footstep_armor_heavy", leftFoot.WorldPosition, hullGuess: currentHull);
                        leftFoot.LastImpactSoundTime = (float)Timing.TotalTime;
                    }
                    if (Math.Abs(rightFootPos - prevRightFootPos) > stepHeight && rightFoot.LastImpactSoundTime < Timing.TotalTime - Limb.SoundInterval)
                    {
                        SoundPlayer.PlaySound("footstep_armor_heavy", rightFoot.WorldPosition, hullGuess: currentHull);
                        rightFoot.LastImpactSoundTime = (float)Timing.TotalTime;
                    }
#endif
                    prevFootPos = footPos.Y;
                }

                if (!leftLegBackwards) { leftLeg.body.ApplyTorque(Dir * -8.0f); }
                if (!rightLegBackwards) { rightLeg.body.ApplyTorque(Dir * -8.0f); }
            }

            float movementFactor = (handPos.Y / stepHeight) * (float)Math.PI;
            movementFactor = 0.8f + (float)Math.Abs(Math.Sin(movementFactor));

            Vector2 subSpeed = currentHull != null || ladder.Item.Submarine == null
                ? Vector2.Zero : ladder.Item.Submarine.Velocity;

            //reached the top of the ladders -> can't go further up
            Vector2 climbForce = new Vector2(0.0f, movement.Y) * movementFactor;

            if (!InWater) { climbForce.Y += 0.3f * movementFactor; }

            if (character.SimPosition.Y > ladderSimPos.Y) { climbForce.Y = Math.Min(0.0f, climbForce.Y); }
            //reached the bottom -> can't go further down
            float minHeightFromFloor = ColliderHeightFromFloor / 2 + Collider.height;
            if (floorFixture != null && 
                !floorFixture.CollisionCategories.HasFlag(Physics.CollisionStairs) &&
                !floorFixture.CollisionCategories.HasFlag(Physics.CollisionPlatform) &&
                character.SimPosition.Y < standOnFloorY + minHeightFromFloor) 
            { 
                climbForce.Y = MathHelper.Clamp((standOnFloorY + minHeightFromFloor - character.SimPosition.Y) * 5.0f, climbForce.Y, 1.0f); 
            }

            //apply forces to the collider to move the Character up/down
            Collider.ApplyForce((climbForce * 20.0f + subSpeed * 50.0f) * Collider.Mass);
            if (Aiming)
            {
                RotateHead(head);
            }
            else if (Anim == Animation.UsingItemWhileClimbing && character.SelectedItem is { } selectedItem)
            {
                Vector2 diff = (selectedItem.WorldPosition - head.WorldPosition) * Dir;
                float targetRotation = MathHelper.WrapAngle(MathUtils.VectorToAngle(diff) - MathHelper.PiOver4 * Dir);
                head.body.SmoothRotate(targetRotation, force: WalkParams.HeadTorque);
            }
            else
            {
                float movementMultiplier = targetMovement.Y < 0 ? 0 : 1;
                head.body.SmoothRotate(MathHelper.PiOver4 * movementMultiplier * Dir, force: WalkParams.HeadTorque);
            }
            
            if (ladder.Item.Prefab.Triggers.None())
            {
                character.SelectedSecondaryItem = null;
                return;
            }

            Rectangle trigger = ladder.Item.Prefab.Triggers.FirstOrDefault();
            trigger = ladder.Item.TransformTrigger(trigger);

            bool isRemote = false;
            bool isClimbing = true;
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                isRemote = character.IsRemotelyControlled;
            }
            if (isRemote)
            {
                if (Math.Abs(targetMovement.X) > 0.05f ||
                    (TargetMovement.Y < 0.0f && ConvertUnits.ToSimUnits(trigger.Height) + handPos.Y < HeadPosition) || 
                    (TargetMovement.Y > 0.0f && handPos.Y > 0.1f))
                {
                    isClimbing = false;
                }
            }
            else if ((character.IsKeyDown(InputType.Left) || character.IsKeyDown(InputType.Right)) &&
                    (!character.IsKeyDown(InputType.Up) && !character.IsKeyDown(InputType.Down)))
            {
                isClimbing = false;
            }

            if (!isClimbing)
            {
                character.StopClimbing();
                IgnorePlatforms = false;
            }

            Ladder GetLowestNearbyLadder(Ladder currentLadder, float threshold = 16.0f)
            {
                foreach (Ladder ladder in Ladder.List)
                {
                    if (ladder == currentLadder || !ladder.Item.IsInteractable(character)) { continue; }
                    if (Math.Abs(ladder.Item.WorldPosition.X - currentLadder.Item.WorldPosition.X) > threshold) { continue; }
                    if (ladder.Item.WorldPosition.Y > currentLadder.Item.WorldPosition.Y) { continue; }
                    if ((currentLadder.Item.WorldRect.Y - currentLadder.Item.Rect.Height) - ladder.Item.WorldRect.Y > threshold) { continue; }
                    return ladder;
                }
                return null;
            }
        }

        void UpdateDying(float deltaTime)
        {
            //the force/torque used to move the limbs goes from 1 to 0 during the death anim duration
            float strength = 1.0f - deathAnimTimer / deathAnimDuration;

            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);

            if (head != null && head.LinearVelocity.LengthSquared() > 1.0f && !head.IsSevered)
            {
                //if the head is moving, try to protect it with the hands
                Limb leftHand = GetLimb(LimbType.LeftHand);
                Limb rightHand = GetLimb(LimbType.RightHand);

                //move hands in front of the head in the direction of the movement
                Vector2 protectPos = head.SimPosition + Vector2.Normalize(head.LinearVelocity);
                if (rightHand != null && !rightHand.IsSevered)
                {
                    HandIK(rightHand, protectPos, strength * 0.1f);
                }
                if (leftHand != null && !leftHand.IsSevered)
                {
                    HandIK(leftHand, protectPos, strength * 0.1f);
                }
            }

            if (torso == null) { return; }
            //attempt to make legs stay in a straight line with the torso to prevent the character from doing a split
            for (int i = 0; i < 2; i++)
            {
                var thigh = i == 0 ? GetLimb(LimbType.LeftThigh) : GetLimb(LimbType.RightThigh);
                if (thigh == null) { continue; }
                if (thigh.IsSevered) { continue; }
                float thighDiff = Math.Abs(MathUtils.GetShortestAngle(torso.Rotation, thigh.Rotation));
                float diff = torso.Rotation - thigh.Rotation;
                if (MathUtils.IsValid(diff))
                {
                    float thighTorque = thighDiff * thigh.Mass * Math.Sign(diff) * 5.0f;
                    thigh.body.ApplyTorque(thighTorque * strength);
                }               

                var leg = i == 0 ? GetLimb(LimbType.LeftLeg) : GetLimb(LimbType.RightLeg);
                if (leg == null || leg.IsSevered) { continue; }
                float legDiff = Math.Abs(MathUtils.GetShortestAngle(torso.Rotation, leg.Rotation));
                diff = torso.Rotation - leg.Rotation;
                if (MathUtils.IsValid(diff))
                {
                    float legTorque = legDiff * leg.Mass * Math.Sign(diff) * 5.0f;
                    leg.body.ApplyTorque(legTorque * strength);
                }
            }
        }

        private float lastReviveTime;

        private void UpdateCPR(float deltaTime)
        {
            if (character.SelectedCharacter == null || 
                (!character.SelectedCharacter.IsUnconscious && !character.SelectedCharacter.IsDead && character.SelectedCharacter.Stun <= 0.0f))
            {
                Anim = Animation.None;
                return;
            }

            Character target = character.SelectedCharacter;

            Crouching = true;

            Vector2 diff = target.SimPosition - character.SimPosition;
            Limb targetHead = target.AnimController.GetLimb(LimbType.Head);
            Limb targetTorso = target.AnimController.GetLimb(LimbType.Torso);
            if (targetTorso == null)
            {
                Anim = Animation.None;
                return;
            }

            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);
            
            Vector2 headDiff = targetHead == null ? diff : targetHead.SimPosition - character.SimPosition;
            targetMovement = new Vector2(diff.X, 0.0f);
            TargetDir = headDiff.X > 0.0f ? Direction.Right : Direction.Left;

            UpdateStanding();

            Vector2 handPos = targetTorso.SimPosition + Vector2.UnitY * 0.2f;

            Grab(handPos, handPos);

            Vector2 colliderPos = GetColliderBottom();

            float prevVitality = target.Vitality;
            bool wasCritical = prevVitality < 0.0f;
            
            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) //Serverside code
            {
                target.Oxygen += deltaTime * 0.5f; //Stabilize them        
            }

            float cprBoost = character.GetStatValue(StatTypes.CPRBoost);
           
            int skill = (int)character.GetSkillLevel("medical");
            //pump for 15 seconds (cprAnimTimer 0-15), then do mouth-to-mouth for 2 seconds (cprAnimTimer 15-17)
            if (cprAnimTimer > 15.0f && targetHead != null && head != null)
            {
                float yPos = (float)Math.Sin(cprAnimTimer) * 0.2f;
                head.PullJointWorldAnchorB = new Vector2(targetHead.SimPosition.X, targetHead.SimPosition.Y + 0.3f + yPos);
                head.PullJointEnabled = true;
                torso.PullJointWorldAnchorB = new Vector2(torso.SimPosition.X, colliderPos.Y + (TorsoPosition.Value - 0.2f));
                torso.PullJointEnabled = true;

                //Serverside code
                if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                {
                    if (target.Oxygen < -10.0f)
                    {
                        if (cprBoost >= 1f)
                        {
                            //prevent the patient from suffocating no matter how fast their oxygen level is dropping
                            target.Oxygen = Math.Max(target.Oxygen, -10.0f);
                        }
                        else
                        {
                            //stabilize the oxygen level but don't allow it to go positive and revive the character yet
                            float stabilizationAmount = skill * CPRSettings.Active.StabilizationPerSkill;
                            stabilizationAmount = MathHelper.Clamp(stabilizationAmount, CPRSettings.Active.StabilizationMin, CPRSettings.Active.StabilizationMax);
                            character.Oxygen -= 1.0f / stabilizationAmount * deltaTime; //Worse skill = more oxygen required
                            if (character.Oxygen > 0.0f) { target.Oxygen += stabilizationAmount * deltaTime; } //we didn't suffocate yet did we
                        }
                    }
                }
            }
            else
            {
                if (targetHead != null && head != null)
                {
                    head.PullJointWorldAnchorB = new Vector2(targetHead.SimPosition.X, targetHead.SimPosition.Y + 0.8f);
                    head.PullJointEnabled = true;
                }

                torso.PullJointWorldAnchorB = new Vector2(torso.SimPosition.X, colliderPos.Y + (TorsoPosition.Value - 0.1f));
                torso.PullJointEnabled = true;

                if (cprPump >= 1)
                {
                    torso.body.ApplyLinearImpulse(new Vector2(0, -20f), maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    targetTorso.body.ApplyLinearImpulse(new Vector2(0, -20f), maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    cprPump = 0;

                    if (skill < CPRSettings.Active.DamageSkillThreshold)
                    {
                        target.LastDamageSource = null;
                        target.DamageLimb(
                            targetTorso.WorldPosition, targetTorso, 
                            new[] { CPRSettings.Active.InsufficientSkillAffliction.Instantiate((CPRSettings.Active.DamageSkillThreshold - skill) * CPRSettings.Active.DamageSkillMultiplier, source: character) },
                            0.0f, true, 0.0f, attacker: null);
                    }
                    if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) //Serverside code
                    {
                        float reviveChance = skill * CPRSettings.Active.ReviveChancePerSkill;
                        reviveChance = (float)Math.Pow(reviveChance, CPRSettings.Active.ReviveChanceExponent);
                        reviveChance = MathHelper.Clamp(reviveChance, CPRSettings.Active.ReviveChanceMin, CPRSettings.Active.ReviveChanceMax);

                        reviveChance *= 1f + cprBoost;

                        if (Rand.Range(0.0f, 1.0f, Rand.RandSync.ServerAndClient) <= reviveChance)
                        {
                            //increase oxygen and clamp it above zero 
                            // -> the character should be revived if there are no major afflictions in addition to lack of oxygen
                            target.Oxygen = Math.Max(target.Oxygen + 10.0f, 10.0f);
                        }
                    }
                }
                cprPump += deltaTime;
            }

            cprAnimTimer = (cprAnimTimer + deltaTime) % 17;

            //got the character back into a non-critical state, increase medical skill
            //BUT only if it has been more than 10 seconds since the character revived someone
            //otherwise it's easy to abuse the system by repeatedly reviving in a low-oxygen room 
            if (!target.IsDead)
            {
                target.CharacterHealth.CalculateVitality();
                if (wasCritical && target.Vitality > 0.0f && Timing.TotalTime > lastReviveTime + 10.0f)
                {
                    character.Info?.IncreaseSkillLevel("medical".ToIdentifier(), SkillSettings.Current.SkillIncreasePerCprRevive);
                    SteamAchievementManager.OnCharacterRevived(target, character);
                    lastReviveTime = (float)Timing.TotalTime;
#if SERVER
                    GameMain.Server?.KarmaManager?.OnCharacterHealthChanged(target, character, damage: Math.Min(prevVitality - target.Vitality, 0.0f), stun: 0.0f);
#endif
                    //reset attacker, we don't want the character to start attacking us
                    //because we caused a bit of damage to them during CPR
                    target.ForgiveAttacker(character);
                }
            }
        }

        public override void DragCharacter(Character target, float deltaTime)
        {
            if (target == null) { return; }

            Limb torso = GetLimb(LimbType.Torso);
            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Limb targetLeftHand = target.AnimController.GetLimb(LimbType.LeftForearm);
            if (targetLeftHand == null) targetLeftHand = target.AnimController.GetLimb(LimbType.Torso);
            if (targetLeftHand == null) targetLeftHand = target.AnimController.MainLimb;

            Limb targetRightHand = target.AnimController.GetLimb(LimbType.RightForearm);
            if (targetRightHand == null) targetRightHand = target.AnimController.GetLimb(LimbType.Torso);
            if (targetRightHand == null) targetRightHand = target.AnimController.MainLimb;

            if (!target.AllowInput)
            {
                target.AnimController.ResetPullJoints();
            }

            if (IsClimbing)
            {
                //cannot drag up ladders if the character is conscious
                if (target.AllowInput && (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient))
                {
                    character.DeselectCharacter();
                    return;
                }
                Limb targetTorso = target.AnimController.GetLimb(LimbType.Torso);
                if (targetTorso == null)
                {
                    targetTorso = target.AnimController.MainLimb;
                }
                if (target.AnimController.Dir != Dir)
                {
                    target.AnimController.Flip();
                }
                Vector2 transformedTorsoPos = torso.SimPosition;
                if (character.Submarine == null && target.Submarine != null)
                {
                    transformedTorsoPos -= target.Submarine.SimPosition;
                }
                else if (character.Submarine != null && target.Submarine == null)
                {
                    transformedTorsoPos += character.Submarine.SimPosition;
                }
                else if (character.Submarine != null && target.Submarine != null && character.Submarine != target.Submarine)
                {
                    transformedTorsoPos += character.Submarine.SimPosition;
                    transformedTorsoPos -= target.Submarine.SimPosition;
                }

                targetTorso.PullJointEnabled = true;
                targetTorso.PullJointWorldAnchorB = transformedTorsoPos + (Vector2.UnitX * -Dir) * 0.2f;
                targetTorso.PullJointMaxForce = 5000.0f;

                if (!targetLeftHand.IsSevered)
                {
                    targetLeftHand.PullJointEnabled = true;
                    targetLeftHand.PullJointWorldAnchorB = transformedTorsoPos + (new Vector2(1 * Dir, 1)) * 0.2f;
                    targetLeftHand.PullJointMaxForce = 5000.0f;
                }
                if (!targetRightHand.IsSevered)
                {
                    targetRightHand.PullJointEnabled = true;
                    targetRightHand.PullJointWorldAnchorB = transformedTorsoPos + (new Vector2(1 * Dir, 1)) * 0.2f;
                    targetRightHand.PullJointMaxForce = 5000.0f;
                }

                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                {
                    Collider.ResetDynamics();
                }

                target.AnimController.IgnorePlatforms = true;
            }
            else
            {
                //only grab with one hand when swimming
                leftHand.Disabled = true;
                if (!inWater)
                {
                    rightHand.Disabled = true;
                }

                for (int i = 0; i < 2; i++)
                {
                    Limb targetLimb = target.AnimController.GetLimb(LimbType.Torso);
                    if (i == 0)
                    {
                        if (!targetLeftHand.IsSevered)
                        {
                            targetLimb = targetLeftHand;
                        }
                        else if (!targetRightHand.IsSevered)
                        {
                            targetLimb = targetRightHand;
                        }
                    }
                    else
                    {
                        if (!targetRightHand.IsSevered)
                        {
                            targetLimb = targetRightHand;
                        }
                        else if (!targetLeftHand.IsSevered)
                        {
                            targetLimb = targetLeftHand;
                        }
                    }

                    Limb pullLimb = i == 0 ? leftHand : rightHand;

                    if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                    {
                        //stop dragging if there's something between the pull limb and the target limb
                        Vector2 sourceSimPos = pullLimb.SimPosition;
                        Vector2 targetSimPos = targetLimb.SimPosition;
                        if (character.Submarine != null && character.SelectedCharacter.Submarine == null)
                        {
                            targetSimPos -= character.Submarine.SimPosition;
                        }
                        else if (character.Submarine == null && character.SelectedCharacter.Submarine != null)
                        {
                            sourceSimPos -= character.SelectedCharacter.Submarine.SimPosition;
                        }
                        else if (character.Submarine != null && character.SelectedCharacter.Submarine != null && character.Submarine != character.SelectedCharacter.Submarine)
                        {
                            targetSimPos += character.SelectedCharacter.Submarine.SimPosition;
                            targetSimPos -= character.Submarine.SimPosition;
                        }
                        var body = Submarine.CheckVisibility(sourceSimPos, targetSimPos, ignoreSubs: true);
                        if (body != null)
                        {
                            character.DeselectCharacter();
                            return;
                        }
                    }

                    //only pull with one hand when swimming
                    if (i > 0 && inWater) { continue; }
                    
                    Vector2 diff = ConvertUnits.ToSimUnits(targetLimb.WorldPosition - pullLimb.WorldPosition);

                    Vector2 targetAnchor;
                    float targetForce;
                    pullLimb.PullJointEnabled = true;
                    if (targetLimb.type == LimbType.Torso || targetLimb == target.AnimController.MainLimb)
                    {
                        Vector2 pullLimbAnchor = targetLimb.SimPosition;
                        pullLimb.PullJointMaxForce = 5000.0f;
                        if (!character.HasAbilityFlag(AbilityFlags.MoveNormallyWhileDragging))
                        {
                            targetMovement *= MathHelper.Clamp(Mass / target.Mass, 0.5f, 1.0f);
                        }
                            
                        Vector2 shoulderPos = rightShoulder.WorldAnchorA;
                        Vector2 dragDir = inWater ? Vector2.Normalize(targetLimb.SimPosition - shoulderPos) : Vector2.UnitY;
                        if (!MathUtils.IsValid(dragDir)) { dragDir = Vector2.UnitY; }

                        targetAnchor = shoulderPos - dragDir * ConvertUnits.ToSimUnits(upperArmLength + forearmLength);
                        targetForce = 200.0f;
                        if (target.Submarine != character.Submarine)
                        {
                            if (character.Submarine == null)
                            {
                                pullLimbAnchor += target.Submarine.SimPosition;
                                targetAnchor -= target.Submarine.SimPosition;
                            }
                            else if (target.Submarine == null)
                            {
                                pullLimbAnchor -= character.Submarine.SimPosition;
                                targetAnchor += character.Submarine.SimPosition;
                            }
                            else
                            {
                                pullLimbAnchor -= target.Submarine.SimPosition;
                                pullLimbAnchor += character.Submarine.SimPosition;
                                targetAnchor -= character.Submarine.SimPosition;
                                targetAnchor += target.Submarine.SimPosition;
                            }
                        }
                        pullLimb.PullJointWorldAnchorB = pullLimbAnchor;
                    }
                    else
                    {
                        pullLimb.PullJointWorldAnchorB = pullLimb.SimPosition + diff;
                        pullLimb.PullJointMaxForce = 5000.0f;
                        targetAnchor = targetLimb.SimPosition - diff;
                        targetForce = 5000.0f;
                    }

                    targetLimb.PullJointEnabled = true;
                    targetLimb.PullJointMaxForce = targetForce;
                    targetLimb.PullJointWorldAnchorB = targetAnchor;
                    targetLimb.Disabled = true;                    

                    if (diff.LengthSquared() > 0.1f)
                    {
                        target.AnimController.movement = -diff;
                    }
                }

                float dist = ConvertUnits.ToSimUnits(Vector2.Distance(target.WorldPosition, WorldPosition));
                //let the target break free if it's moving away and gets far enough
                if ((GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) && dist > 1.4f && target.AllowInput &&
                    Vector2.Dot(target.WorldPosition - WorldPosition, target.AnimController.TargetMovement) > 0)
                {
                    character.DeselectCharacter();
                    return;
                }

                //limit movement if moving away from the target
                if (!character.HasAbilityFlag(AbilityFlags.MoveNormallyWhileDragging) && Vector2.Dot(target.WorldPosition - WorldPosition, targetMovement) < 0)
                {
                    targetMovement *= MathHelper.Clamp(1.5f - dist, 0.0f, 1.0f);
                }
                
                if (!target.AllowInput)
                {
                    target.AnimController.Stairs = Stairs;
                    target.AnimController.IgnorePlatforms = IgnorePlatforms;
                    target.AnimController.TargetMovement = TargetMovement;
                }
                else if (target is AICharacter && target != Character.Controlled)
                {
                    if (target.AnimController.Dir > 0 == WorldPosition.X > target.WorldPosition.X)
                    {
                        target.AnimController.LockFlippingUntil = (float)Timing.TotalTime + 0.5f;
                    }
                    else
                    {
                        target.AnimController.TargetDir = WorldPosition.X > target.WorldPosition.X ? Direction.Right : Direction.Left;
                    }
                    //make the target stand 0.5 meters away from this character, on the side they're currently at
                    Vector2 movement = (character.SimPosition + Vector2.UnitX * 0.5f * Math.Sign(target.SimPosition.X - character.SimPosition.X)) - target.SimPosition;
                    target.AnimController.TargetMovement = movement.LengthSquared() > 0.01f ? movement : Vector2.Zero;
                }
            }
        }

        private void RotateHead(Limb head)
        {
            Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);
            Vector2 dir = (mousePos - head.SimPosition) * Dir;
            float rot = MathUtils.VectorToAngle(dir);
            var neckJoint = GetJointBetweenLimbs(LimbType.Head, LimbType.Torso);
            if (neckJoint != null)
            {
                float offset = MathUtils.WrapAnglePi(GetLimb(LimbType.Torso).body.Rotation);
                float lowerLimit = neckJoint.LowerLimit + offset;
                float upperLimit = neckJoint.UpperLimit + offset;
                float min = Math.Min(lowerLimit, upperLimit);
                float max = Math.Max(lowerLimit, upperLimit);
                rot = Math.Clamp(rot, min, max);
            }
            head.body.SmoothRotate(rot, CurrentAnimationParams.HeadTorque);
        }

        private void FootIK(Limb foot, Vector2 pos, float legTorque, float footTorque, float footAngle)
        {
            if (!MathUtils.IsValid(pos))
            {
                string errorMsg = "Invalid foot position in FootIK (" + pos + ")\n" + Environment.StackTrace.CleanupStackTrace();
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                GameAnalyticsManager.AddErrorEventOnce("FootIK:InvalidPos", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }

            Limb upperLeg, lowerLeg;
            if (foot.type == LimbType.LeftFoot)
            {
                upperLeg = GetLimb(LimbType.LeftThigh);
                lowerLeg = GetLimb(LimbType.LeftLeg);
            }
            else
            {
                upperLeg = GetLimb(LimbType.RightThigh);
                lowerLeg = GetLimb(LimbType.RightLeg);
            }
            Limb torso = GetLimb(LimbType.Torso);
            LimbJoint waistJoint = GetJointBetweenLimbs(LimbType.Waist, upperLeg.type) ?? GetJointBetweenLimbs(LimbType.Torso, upperLeg.type);
            Vector2 waistPos = Vector2.Zero;
            if (waistJoint != null)
            {
                waistPos = waistJoint.LimbA == upperLeg ? waistJoint.WorldAnchorA : waistJoint.WorldAnchorB;
            }

            //distance from waist joint to the target position
            float c = Vector2.Distance(pos, waistPos);
            c = Math.Max(c, Math.Abs(upperLegLength - lowerLegLength));

            float legAngle = MathUtils.VectorToAngle(pos - waistPos) + MathHelper.PiOver2;
            if (!MathUtils.IsValid(legAngle))
            {
                string errorMsg = "Invalid leg angle (" + legAngle + ") in FootIK. Waist pos: " + waistPos + ", target pos: " + pos + "\n" + Environment.StackTrace.CleanupStackTrace();
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                GameAnalyticsManager.AddErrorEventOnce("FootIK:InvalidAngle", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }

            //make sure the angle "has the same number of revolutions" as the torso
            //(e.g. we don't want to rotate the legs to 0 if the torso is at 360, because that'd blow up the hip joints) 
            while (torso.Rotation - legAngle > MathHelper.Pi)
            {
                legAngle += MathHelper.TwoPi;
            }
            while (torso.Rotation - legAngle < -MathHelper.Pi)
            {
                legAngle -= MathHelper.TwoPi;
            }
            
            //if the distance is longer than the length of the upper and lower leg, we'll just have to extend them directly towards the target
            float upperLegAngle = c >= upperLegLength + lowerLegLength ? 0.0f : MathUtils.SolveTriangleSSS(lowerLegLength, upperLegLength, c);
            float lowerLegAngle = c >= upperLegLength + lowerLegLength ? 0.0f : MathUtils.SolveTriangleSSS(upperLegLength, lowerLegLength, c);

            upperLeg.body.SmoothRotate((legAngle + upperLegAngle * Dir), upperLeg.Mass * legTorque, wrapAngle: false);
            lowerLeg.body.SmoothRotate((legAngle - lowerLegAngle * Dir), lowerLeg.Mass * legTorque, wrapAngle: false);
            foot.body.SmoothRotate((legAngle - (lowerLegAngle + footAngle) * Dir), foot.Mass * footTorque, wrapAngle: false);
        }

        public override void Flip()
        {
            base.Flip();

            WalkPos = -WalkPos;

            Limb torso = GetLimb(LimbType.Torso);

            Vector2 difference;

            Matrix torsoTransform = Matrix.CreateRotationZ(torso.Rotation);

            foreach (Item heldItem in character.HeldItems)
            {
                if (heldItem?.body != null && !heldItem.Removed && heldItem.GetComponent<Holdable>() != null)
                {
                    heldItem.FlipX(relativeToSub: false);
                }
                // TODO: was this added by a mistake?
                //heldItem.FlipX(relativeToSub: false);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) { continue; }

                bool mirror = false;
                bool flipAngle = false;
                bool wrapAngle = false;

                switch (limb.type)
                {
                    case LimbType.LeftHand:
                    case LimbType.LeftArm:
                    case LimbType.LeftForearm:
                    case LimbType.RightHand:
                    case LimbType.RightArm:
                    case LimbType.RightForearm:
                        flipAngle = true;
                        break;
                    case LimbType.LeftThigh:
                    case LimbType.LeftLeg:
                    case LimbType.LeftFoot:
                    case LimbType.RightThigh:
                    case LimbType.RightLeg:
                    case LimbType.RightFoot:
                        mirror = Crouching && !inWater;
                        flipAngle = (limb.DoesFlip || Crouching) && !inWater;
                        wrapAngle = !inWater;
                        break;
                    default:
                        flipAngle = limb.DoesFlip && !inWater;
                        wrapAngle = !inWater;
                        break;
                }

                Vector2 position = limb.SimPosition;

                if (!limb.PullJointEnabled && mirror)
                {
                    difference = limb.body.SimPosition - torso.SimPosition;
                    difference = Vector2.Transform(difference, torsoTransform);
                    difference.Y = -difference.Y;

                    position = torso.SimPosition + Vector2.Transform(difference, -torsoTransform);

                    //TrySetLimbPosition(limb, limb.SimPosition, );
                }

                float angle = flipAngle ? -limb.body.Rotation : limb.body.Rotation;
                if (wrapAngle) { angle = MathUtils.WrapAnglePi(angle); }
                
                TrySetLimbPosition(limb, Collider.SimPosition, position, angle);
            }
        }

        public override float GetSpeed(AnimationType type)
        {
            if (type == AnimationType.Crouch)
            {
                if (!CanWalk)
                {
                    DebugConsole.ThrowError($"{character.SpeciesName} cannot crouch!");
                    return 0;
                }
                return IsMovingBackwards ? HumanCrouchParams.MovementSpeed * HumanCrouchParams.BackwardsMovementMultiplier : HumanCrouchParams.MovementSpeed;
            }
            return base.GetSpeed(type);
        }
    }
}
