using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class FishAnimController : AnimController
    {
        public override RagdollParams RagdollParams
        {
            get { return FishRagdollParams; }
            protected set { FishRagdollParams = value as FishRagdollParams; }
        }

        private FishRagdollParams _ragdollParams;
        public FishRagdollParams FishRagdollParams
        {
            get
            {
                if (_ragdollParams == null)
                {
                    _ragdollParams = FishRagdollParams.GetDefaultRagdollParams(character.VariantOf ?? character.SpeciesName);
                    if (character.VariantOf != null)
                    {
                        _ragdollParams.ApplyVariantScale(character.Params.VariantFile);
                    }
                }
                return _ragdollParams;
            }
            protected set
            {
                _ragdollParams = value;
            }
        }

        private FishWalkParams _fishWalkParams;
        public FishWalkParams FishWalkParams
        {
            get
            {
                if (_fishWalkParams == null)
                {
                    _fishWalkParams = FishWalkParams.GetDefaultAnimParams(character);
                }
                return _fishWalkParams;
            }
            set { _fishWalkParams = value; }
        }

        private FishRunParams _fishRunParams;
        public FishRunParams FishRunParams
        {
            get
            {
                if (_fishRunParams == null)
                {
                    _fishRunParams = FishRunParams.GetDefaultAnimParams(character);
                }
                return _fishRunParams;
            }
            set { _fishRunParams = value; }
        }

        private FishSwimSlowParams _fishSwimSlowParams;
        public FishSwimSlowParams FishSwimSlowParams
        {
            get
            {
                if (_fishSwimSlowParams == null)
                {
                    _fishSwimSlowParams = FishSwimSlowParams.GetDefaultAnimParams(character);
                }
                return _fishSwimSlowParams;
            }
            set { _fishSwimSlowParams = value; }
        }

        private FishSwimFastParams _fishSwimFastParams;
        public FishSwimFastParams FishSwimFastParams
        {
            get
            {
                if (_fishSwimFastParams == null)
                {
                    _fishSwimFastParams = FishSwimFastParams.GetDefaultAnimParams(character);
                }
                return _fishSwimFastParams;
            }
            set { _fishSwimFastParams = value; }
        }

        public IFishAnimation CurrentFishAnimation => CurrentAnimationParams as IFishAnimation;
        public new FishGroundedParams CurrentGroundedParams => base.CurrentGroundedParams as FishGroundedParams;
        public new FishSwimParams CurrentSwimParams => base.CurrentSwimParams as FishSwimParams;

        public float? TailAngle => GetValidOrNull(CurrentAnimationParams, CurrentFishAnimation?.TailAngleInRadians);
        public float FootTorque => CurrentAnimationParams.FootTorque;
        public float HeadTorque => CurrentAnimationParams.HeadTorque;
        public float TorsoTorque => CurrentAnimationParams.TorsoTorque;
        public float TailTorque => CurrentFishAnimation.TailTorque;
        public float HeadMoveForce => CurrentGroundedParams.HeadMoveForce;
        public float TorsoMoveForce => CurrentGroundedParams.TorsoMoveForce;
        public float FootMoveForce => CurrentGroundedParams.FootMoveForce;

        public override GroundedMovementParams WalkParams
        {
            get { return FishWalkParams; }
            set { FishWalkParams = value as FishWalkParams; }
        }

        public override GroundedMovementParams RunParams
        {
            get { return FishRunParams; }
            set { FishRunParams = value as FishRunParams; }
        }

        public override SwimParams SwimSlowParams
        {
            get { return FishSwimSlowParams; }
            set { FishSwimSlowParams = value as FishSwimSlowParams; }
        }

        public override SwimParams SwimFastParams
        {
            get { return FishSwimFastParams; }
            set { FishSwimFastParams = value as FishSwimFastParams; }
        }

        private float flipTimer, flipCooldown;

        public FishAnimController(Character character, string seed, FishRagdollParams ragdollParams = null) : base(character, seed, ragdollParams) { }

        public override void UpdateAnim(float deltaTime)
        {
            if (Frozen) return;
            if (MainLimb == null) { return; }
            var mainLimb = MainLimb;

            levitatingCollider = !IsHanging;

            if (!character.CanMove)
            {
                levitatingCollider = false;
                Collider.FarseerBody.FixedRotation = false;
                if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
                {
                    Collider.Enabled = false;
                    Collider.LinearVelocity = mainLimb.LinearVelocity;
                    Collider.SetTransformIgnoreContacts(mainLimb.SimPosition, mainLimb.Rotation);
                    //reset pull joints to prevent the character from "hanging" mid-air if pull joints had been active when the character was still moving
                    //(except when dragging, then we need the pull joints)
                    if (!character.CanBeDragged || character.SelectedBy == null) { ResetPullJoints(); }
                }
                if (character.IsDead && deathAnimTimer < deathAnimDuration)
                {
                    deathAnimTimer += deltaTime;
                    UpdateDying(deltaTime);
                }
                else if (!InWater && !CanWalk && character.AllowInput)
                {
                    //cannot walk but on dry land -> wiggle around
                    UpdateDying(deltaTime);
                }
                return;
            }
            else
            {
                deathAnimTimer = 0.0f;
            }

            //re-enable collider
            if (!Collider.Enabled)
            {
                var lowestLimb = FindLowestLimb();

                Collider.SetTransform(new Vector2(
                    Collider.SimPosition.X,
                    Math.Max(lowestLimb.SimPosition.Y + (Collider.radius + Collider.height / 2), Collider.SimPosition.Y)),
                    0.0f);

                Collider.Enabled = true;
            }

            ResetPullJoints();

            if (strongestImpact > 0.0f)
            {
                character.Stun = MathHelper.Clamp(strongestImpact * 0.5f, character.Stun, 5.0f);
                strongestImpact = 0.0f;
            }

            if (aiming)
            {
                TargetMovement = TargetMovement.ClampLength(2);
            }

            if (inWater && !forceStanding)
            {
                Collider.FarseerBody.FixedRotation = false;
                UpdateSineAnim(deltaTime);
            }
            else if (RagdollParams.CanWalk && (currentHull != null || forceStanding))
            {
                if (CurrentGroundedParams != null)
                {
                    //rotate collider back upright
                    float standAngle = CurrentGroundedParams.ColliderStandAngleInRadians * Dir;
                    if (Math.Abs(MathUtils.GetShortestAngle(Collider.Rotation, standAngle)) > 0.001f)
                    {
                        Collider.AngularVelocity = MathUtils.GetShortestAngle(Collider.Rotation, standAngle) * 60.0f;
                        Collider.FarseerBody.FixedRotation = false;
                    }
                    else
                    {
                        Collider.FarseerBody.FixedRotation = true;
                    }
                }
                UpdateWalkAnim(deltaTime);
            }
            if (character.SelectedCharacter != null)
            {
                DragCharacter(character.SelectedCharacter, deltaTime);
                return;
            }
            if (character.AnimController.AnimationTestPose)
            {
                ApplyTestPose();
            }
            //don't flip when simply physics is enabled
            if (SimplePhysicsEnabled) { return; }
            
            if (!character.IsRemotelyControlled && (character.AIController == null || character.AIController.CanFlip) && !aiming)
            {
                if (!inWater || (CurrentSwimParams != null && CurrentSwimParams.Mirror))
                {
                    if (targetMovement.X > 0.1f && targetMovement.X > Math.Abs(targetMovement.Y) * 0.2f)
                    {
                        TargetDir = Direction.Right;
                    }
                    else if (targetMovement.X < -0.1f && targetMovement.X < -Math.Abs(targetMovement.Y) * 0.2f)
                    {
                        TargetDir = Direction.Left;
                    }
                }
                else
                {
                    float rotation = MathHelper.WrapAngle(Collider.Rotation);
                    rotation = MathHelper.ToDegrees(rotation);
                    if (rotation < 0.0f)
                    {
                        rotation += 360;
                    }
                    if (rotation > 20 && rotation < 160)
                    {
                        TargetDir = Direction.Left;
                    }
                    else if (rotation > 200 && rotation < 340)
                    {
                        TargetDir = Direction.Right;
                    }
                }
            }

            if (!CurrentFishAnimation.Flip) { return; }
            if (IsStuck) { return; }
            if (character.AIController != null && !character.AIController.CanFlip) { return; }

            flipCooldown -= deltaTime;
            if (TargetDir != Direction.None && TargetDir != dir)
            {
                flipTimer += deltaTime;
                // Speed reductions are not taken into account here. It's intentional: an ai character cannot flip if it's heavily paralyzed (for example).
                float requiredSpeed = CurrentAnimationParams.MovementSpeed / 2;
                if (CurrentHull != null)
                {
                    // Enemy movement speeds are halved inside submarines
                    requiredSpeed /= 2;
                }
                bool isMovingFastEnough = Math.Abs(MainLimb.LinearVelocity.X) > requiredSpeed;
                bool isTryingToMoveHorizontally = Math.Abs(TargetMovement.X) > Math.Abs(TargetMovement.Y);
                if ((flipTimer > CurrentFishAnimation.FlipDelay && flipCooldown <= 0.0f && ((isMovingFastEnough && isTryingToMoveHorizontally) || IsMovingBackwards))
                    || character.IsRemotePlayer)
                {
                    Flip();
                    if (!inWater || (CurrentSwimParams != null && CurrentSwimParams.Mirror))
                    {
                        Mirror(CurrentSwimParams != null ? CurrentSwimParams.MirrorLerp : true);
                    }
                    flipTimer = 0.0f;
                    flipCooldown = CurrentFishAnimation.FlipCooldown;
                }
            }
            else
            {
                flipTimer = 0.0f;
            }
            wasAiming = aiming;
            aiming = false;
            wasAimingMelee = aimingMelee;
            aimingMelee = false;
        }

        private bool CanDrag(Character target)
        {
            return Mass / target.Mass > 0.1f;
        }

        private float eatTimer = 0.0f;

        public override void DragCharacter(Character target, float deltaTime)
        {
            if (target == null) { return; }     
            Limb mouthLimb = GetLimb(LimbType.Head);
            if (mouthLimb == null) { return; }

            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient)
            {
                //stop dragging if there's something between the pull limb and the target
                Vector2 sourceSimPos = SimplePhysicsEnabled ? character.SimPosition : mouthLimb.SimPosition;
                Vector2 targetSimPos = target.SimPosition;
                if (character.Submarine != null && character.SelectedCharacter.Submarine == null)
                {
                    targetSimPos -= character.Submarine.SimPosition;
                }
                else if (character.Submarine == null && character.SelectedCharacter.Submarine != null)
                {
                    sourceSimPos -= character.SelectedCharacter.Submarine.SimPosition;
                }
                var body = Submarine.CheckVisibility(sourceSimPos, targetSimPos, ignoreSubs: true);
                if (body != null)
                {
                    character.DeselectCharacter();
                    return;
                }
            }

            float dmg = character.Params.EatingSpeed;
            float eatSpeed = dmg / ((float)Math.Sqrt(Math.Max(target.Mass, 1)) * 10);
            eatTimer += deltaTime * eatSpeed;

            Vector2 mouthPos = SimplePhysicsEnabled ? character.SimPosition : GetMouthPosition().Value;
            Vector2 attackSimPosition = character.Submarine == null ? ConvertUnits.ToSimUnits(target.WorldPosition) : target.SimPosition;

            Vector2 limbDiff = attackSimPosition - mouthPos;
            float extent = Math.Max(mouthLimb.body.GetMaxExtent(), 1);
            if (limbDiff.LengthSquared() < extent * extent)
            {
                //pull the target character to the position of the mouth
                //(+ make the force fluctuate to waggle the character a bit)
                float dragForce = MathHelper.Clamp(eatSpeed * 10, 0, 40);
                if (dragForce > 0.1f)
                {
                    Vector2 targetPos = mouthPos;
                    if (target.Submarine != null && character.Submarine == null)
                    {
                        targetPos -= target.Submarine.SimPosition;
                    }
                    else if (target.Submarine == null && character.Submarine != null)
                    {
                        targetPos += character.Submarine.SimPosition;
                    }
                    target.AnimController.MainLimb.body.SmoothRotate(mouthLimb.Rotation, dragForce * 2);
                    if (!target.AnimController.SimplePhysicsEnabled)
                    {
                        target.AnimController.MainLimb.MoveToPos(targetPos, (float)(Math.Sin(eatTimer) + dragForce));
                    }
                    target.AnimController.Collider.MoveToPos(targetPos, (float)(Math.Sin(eatTimer) + dragForce));
                }

                if (InWater)
                {
                    //pull the character's mouth to the target character (again with a fluctuating force)
                    float pullStrength = (float)(Math.Sin(eatTimer) * Math.Max(Math.Sin(eatTimer * 0.5f), 0.0f));
                    mouthLimb.body.ApplyForce(limbDiff * mouthLimb.Mass * 50.0f * pullStrength);
                }
                else
                {
                    float force = (float)Math.Sin(eatTimer * 100) * mouthLimb.Mass;
                    mouthLimb.body.ApplyLinearImpulse(Vector2.UnitY * force * 2, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    mouthLimb.body.ApplyTorque(-force * 50);
                }

                if (Character.CanEat)
                {
                    var jaw = GetLimb(LimbType.Jaw);
                    if (jaw != null)
                    {
                        jaw.body.ApplyTorque(-(float)Math.Sin(eatTimer * 150) * jaw.Mass * 25);
                    }

                    character.ApplyStatusEffects(ActionType.OnEating, deltaTime);

                    float particleFrequency = MathHelper.Clamp(eatSpeed / 2, 0.02f, 0.5f);
                    if (Rand.Value() < particleFrequency / 6)
                    {
                        target.AnimController.MainLimb.AddDamage(target.SimPosition, dmg, 0, 0, false);
                    }
                    if (Rand.Value() < particleFrequency)
                    {
                        target.AnimController.MainLimb.AddDamage(target.SimPosition, 0, dmg, 0, false);
                    }
                    if (eatTimer % 1.0f < 0.5f && (eatTimer - deltaTime * eatSpeed) % 1.0f > 0.5f)
                    {
                        static bool CanBeSevered(LimbJoint j) => !j.IsSevered && j.CanBeSevered && j.LimbA != null && !j.LimbA.IsSevered && j.LimbB != null && !j.LimbB.IsSevered;
                        //keep severing joints until there is only one limb left
                        var nonSeveredJoints = target.AnimController.LimbJoints.Where(CanBeSevered);
                        if (nonSeveredJoints.None())
                        {
                            //small monsters don't eat the contents of the character's inventory
                            if (Mass < target.AnimController.Mass)
                            {
                                target.Inventory?.AllItemsMod.ForEach(it => it?.Drop(dropper: null));
                            }

                            //only one limb left, the character is now full eaten
                            Entity.Spawner?.AddToRemoveQueue(target);

                            if (Character.AIController is EnemyAIController enemyAi)
                            {
                                enemyAi.PetBehavior?.OnEat("dead", 1.0f);
                            }

                            character.SelectedCharacter = null;
                        }
                        else //sever a random joint
                        {
                            target.AnimController.SeverLimbJoint(nonSeveredJoints.GetRandom());
                        }
                    }
                }                
            }
            else
            {
                character.SelectedCharacter = null;
            }
        }

        public bool reverse;

        void UpdateSineAnim(float deltaTime)
        {
            if (CurrentSwimParams == null) { return; }
            movement = TargetMovement;
            bool isMoving = movement.LengthSquared() > 0.00001f;
            var mainLimb = MainLimb;
            if (isMoving)
            {
                float t = 0.5f;
                if (!SimplePhysicsEnabled && CurrentSwimParams.RotateTowardsMovement)
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
            //limbs are disabled when simple physics is enabled, no need to move them
            if (SimplePhysicsEnabled) { return; }
            mainLimb.PullJointEnabled = true;

            if (aiming && movement.Length() <= 0.1f)
            {
                Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);
                Vector2 diff = (mousePos - (GetLimb(LimbType.Torso) ?? MainLimb).SimPosition) * Dir;
                TargetMovement = new Vector2(0.0f, -0.1f);
                float newRotation = MathUtils.VectorToAngle(diff);
                Collider.SmoothRotate(newRotation, CurrentSwimParams.SteerTorque * character.SpeedMultiplier);
            }

            if (!isMoving)
            {
                WalkPos = MathHelper.SmoothStep(WalkPos, MathHelper.PiOver2, deltaTime * 5);
                mainLimb.PullJointWorldAnchorB = Collider.SimPosition;
            }
            else
            {
                Vector2 transformedMovement = reverse ? -movement : movement;
                float movementAngle = MathUtils.VectorToAngle(transformedMovement) - MathHelper.PiOver2;
                float mainLimbAngle = 0;
                if (mainLimb.type == LimbType.Torso && TorsoAngle.HasValue)
                {
                    mainLimbAngle = TorsoAngle.Value;
                }
                else if (mainLimb.type == LimbType.Head && HeadAngle.HasValue)
                {
                    mainLimbAngle = HeadAngle.Value;
                }
                mainLimbAngle *= Dir;
                while (mainLimb.Rotation - (movementAngle + mainLimbAngle) > MathHelper.Pi)
                {
                    movementAngle += MathHelper.TwoPi;
                }
                while (mainLimb.Rotation - (movementAngle + mainLimbAngle) < -MathHelper.Pi)
                {
                    movementAngle -= MathHelper.TwoPi;
                }
                if (CurrentSwimParams.RotateTowardsMovement)
                {
                    Collider.SmoothRotate(movementAngle, CurrentSwimParams.SteerTorque * character.SpeedMultiplier);
                    if (TorsoAngle.HasValue)
                    {
                        Limb torso = GetLimb(LimbType.Torso);
                        if (torso != null)
                        {
                            SmoothRotateWithoutWrapping(torso, movementAngle + TorsoAngle.Value * Dir, mainLimb, TorsoTorque);
                        }
                    }
                    if (HeadAngle.HasValue)
                    {
                        Limb head = GetLimb(LimbType.Head);
                        if (head != null)
                        {
                            SmoothRotateWithoutWrapping(head, movementAngle + HeadAngle.Value * Dir, mainLimb, HeadTorque);
                        }
                    }
                    if (TailAngle.HasValue)
                    {
                        bool isAngleApplied = false;
                        foreach (var limb in Limbs)
                        {
                            if (limb.IsSevered) { continue; }
                            if (limb.type != LimbType.Tail) { continue; }
                            if (!limb.Params.ApplyTailAngle) { continue; }
                            RotateTail(limb);
                            isAngleApplied = true;
                        }
                        if (!isAngleApplied)
                        {
                            RotateTail(GetLimb(LimbType.Tail));
                        }

                        void RotateTail(Limb tail)
                        {
                            if (tail == null) { return; }
                            float? mainLimbTargetAngle = null;
                            if (mainLimb.type == LimbType.Torso)
                            {
                                mainLimbTargetAngle = TorsoAngle;
                            }
                            else if (mainLimb.type == LimbType.Head)
                            {
                                mainLimbTargetAngle = HeadAngle;
                            }
                            float torque = TailTorque;
                            float maxMultiplier = CurrentSwimParams.TailTorqueMultiplier;
                            if (mainLimbTargetAngle.HasValue && maxMultiplier > 1)
                            {
                                float diff = Math.Abs(mainLimb.Rotation - tail.Rotation);
                                float offset = Math.Abs(mainLimbTargetAngle.Value - TailAngle.Value);
                                torque *= MathHelper.Lerp(1, maxMultiplier, MathUtils.InverseLerp(0, MathHelper.PiOver2, diff - offset));
                            }
                            SmoothRotateWithoutWrapping(tail, movementAngle + TailAngle.Value * Dir, mainLimb, torque);
                        }
                    }
                }
                else
                {
                    movementAngle = Dir > 0 ? -MathHelper.PiOver2 : MathHelper.PiOver2;
                    if (reverse)
                    {
                        movementAngle = MathUtils.WrapAngleTwoPi(movementAngle - MathHelper.Pi);
                    }
                    if (mainLimb.type == LimbType.Head && HeadAngle.HasValue)
                    {
                        Collider.SmoothRotate(HeadAngle.Value * Dir, CurrentSwimParams.SteerTorque * character.SpeedMultiplier);
                    }
                    else if (mainLimb.type == LimbType.Torso && TorsoAngle.HasValue)
                    {
                        Collider.SmoothRotate(TorsoAngle.Value * Dir, CurrentSwimParams.SteerTorque * character.SpeedMultiplier);
                    }
                    if (TorsoAngle.HasValue)
                    {
                        Limb torso = GetLimb(LimbType.Torso);
                        torso?.body.SmoothRotate(TorsoAngle.Value * Dir, TorsoTorque);
                    }
                    if (HeadAngle.HasValue)
                    {
                        Limb head = GetLimb(LimbType.Head);
                        head?.body.SmoothRotate(HeadAngle.Value * Dir, HeadTorque);
                    }
                    if (TailAngle.HasValue)
                    {
                        bool isAngleApplied = false;
                        foreach (var limb in Limbs)
                        {
                            if (limb.IsSevered) { continue; }
                            if (limb.type != LimbType.Tail) { continue; }
                            if (!limb.Params.ApplyTailAngle) { continue; }
                            RotateTail(limb);
                            isAngleApplied = true;
                        }
                        if (!isAngleApplied)
                        {
                            RotateTail(GetLimb(LimbType.Tail));
                        }

                        void RotateTail(Limb tail)
                        {
                            if (tail != null)
                            {
                                tail.body.SmoothRotate(TailAngle.Value * Dir, TailTorque);
                            }
                        }
                    }
                }

                var waveLength = Math.Abs(CurrentSwimParams.WaveLength * RagdollParams.JointScale);
                var waveAmplitude = Math.Abs(CurrentSwimParams.WaveAmplitude * character.SpeedMultiplier);
                if (waveLength > 0 && waveAmplitude > 0)
                {
                    WalkPos -= transformedMovement.Length() / Math.Abs(waveLength);
                    WalkPos = MathUtils.WrapAngleTwoPi(WalkPos);
                }

                foreach (var limb in Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    switch (limb.type)
                    {
                        case LimbType.LeftFoot:
                        case LimbType.RightFoot:
                            if (CurrentSwimParams.FootAnglesInRadians.ContainsKey(limb.Params.ID))
                            {
                                SmoothRotateWithoutWrapping(limb, movementAngle + CurrentSwimParams.FootAnglesInRadians[limb.Params.ID] * Dir, mainLimb, FootTorque);
                            }
                            break;
                        case LimbType.Tail:
                            if (waveLength > 0 && waveAmplitude > 0)
                            {
                                float waveRotation = (float)Math.Sin(WalkPos * limb.Params.SineFrequencyMultiplier);
                                limb.body.ApplyTorque(waveRotation * limb.Mass * waveAmplitude * limb.Params.SineAmplitudeMultiplier);
                            }
                            break;
                    }
                }

                for (int i = 0; i < Limbs.Length; i++)
                {
                    var limb = Limbs[i];
                    if (limb.IsSevered) { continue; }
                    if (limb.SteerForce <= 0.0f) { continue; }
                    if (!Collider.PhysEnabled) { continue; }
                    Vector2 pullPos = limb.PullJointWorldAnchorA;
                    limb.body.ApplyForce(movement * limb.SteerForce * limb.Mass * Math.Max(character.SpeedMultiplier, 1), pullPos);
                }

                Vector2 mainLimbDiff = mainLimb.PullJointWorldAnchorB - mainLimb.SimPosition;
                if (CurrentSwimParams.UseSineMovement)
                {
                    mainLimb.PullJointWorldAnchorB = Vector2.SmoothStep(
                        mainLimb.PullJointWorldAnchorB,
                        Collider.SimPosition,
                        mainLimbDiff.LengthSquared() > 10.0f ? 1.0f : (float)Math.Abs(Math.Sin(WalkPos)));
                }
                else
                {
                    //mainLimb.PullJointWorldAnchorB = Collider.SimPosition;
                    mainLimb.PullJointWorldAnchorB = Vector2.Lerp(
                        mainLimb.PullJointWorldAnchorB,
                        Collider.SimPosition,
                        mainLimbDiff.LengthSquared() > 10.0f ? 1.0f : 0.5f);
                }
            }

            foreach (var limb in Limbs)
            {
                if (limb.IsSevered) { continue; }
                if (Math.Abs(limb.Params.ConstantTorque) > 0)
                {
                    float movementFactor = Math.Max(character.AnimController.Collider.LinearVelocity.Length() * 0.5f, 1);
                    limb.body.SmoothRotate(MainLimb.Rotation + MathHelper.ToRadians(limb.Params.ConstantAngle) * Dir, limb.Mass * limb.Params.ConstantTorque * movementFactor, wrapAngle: true);
                }
                if (limb.Params.BlinkFrequency > 0)
                {
                    limb.UpdateBlink(deltaTime, MainLimb.Rotation);
                }
            }

            floorY = Limbs[0].SimPosition.Y;
        }
            
        void UpdateWalkAnim(float deltaTime)
        {
            movement = MathUtils.SmoothStep(movement, TargetMovement, 0.2f);

            Collider.LinearVelocity = new Vector2(
                movement.X,
                Collider.LinearVelocity.Y > 0.0f ? Collider.LinearVelocity.Y * 0.5f : Collider.LinearVelocity.Y);

            //limbs are disabled when simple physics is enabled, no need to move them
            if (SimplePhysicsEnabled) { return; }

            Vector2 colliderBottom = GetColliderBottom();

            float movementAngle = 0.0f;
            var mainLimb = MainLimb;
            float mainLimbAngle = (mainLimb.type == LimbType.Torso ? TorsoAngle ?? 0 : HeadAngle ?? 0) * Dir;
            while (mainLimb.Rotation - (movementAngle + mainLimbAngle) > MathHelper.Pi)
            {
                movementAngle += MathHelper.TwoPi;
            }
            while (mainLimb.Rotation - (movementAngle + mainLimbAngle) < -MathHelper.Pi)
            {
                movementAngle -= MathHelper.TwoPi;
            }

            float offset = MathHelper.Pi * CurrentGroundedParams.StepLiftOffset;
            if (CurrentGroundedParams.MultiplyByDir)
            {
                offset *= Dir;
            }
            float stepLift = TargetMovement.X == 0.0f ? 0 :
                (float)Math.Sin(WalkPos * Dir * CurrentGroundedParams.StepLiftFrequency + offset) * (CurrentGroundedParams.StepLiftAmount / 100);

            float limpAmount = character.GetLegPenalty();
            if (limpAmount > 0)
            {
                float walkPosX = (float)Math.Cos(WalkPos);
                //make the footpos oscillate when limping
                limpAmount = Math.Max(Math.Abs(walkPosX) * limpAmount, 0.0f) * Math.Min(Math.Abs(TargetMovement.X), 0.3f) * Dir;
            }

            Limb torso = GetLimb(LimbType.Torso);
            if (torso != null)
            {
                if (TorsoAngle.HasValue)
                {
                    SmoothRotateWithoutWrapping(torso, movementAngle + TorsoAngle.Value * Dir, mainLimb, TorsoTorque);
                }
                if (TorsoPosition.HasValue && TorsoMoveForce > 0.0f)
                {
                    Vector2 pos = colliderBottom + new Vector2(limpAmount, TorsoPosition.Value + stepLift);

                    if (torso != mainLimb)
                    {
                        pos.X = torso.SimPosition.X;
                    }

                    torso.MoveToPos(pos, TorsoMoveForce);
                    torso.PullJointEnabled = true;
                    torso.PullJointWorldAnchorB = pos;
                }
            }

            Limb head = GetLimb(LimbType.Head);
            if (head != null)
            {
                bool headFacingBackwards = false;
                if (HeadAngle.HasValue && head != mainLimb)
                {
                    SmoothRotateWithoutWrapping(head, movementAngle + HeadAngle.Value * Dir, mainLimb, HeadTorque);
                    if (Math.Sign(head.SimPosition.X - mainLimb.SimPosition.X) != Math.Sign(Dir))
                    {
                        headFacingBackwards = true;
                    }
                }
                if (HeadPosition.HasValue && HeadMoveForce > 0.0f && !headFacingBackwards)
                {
                    Vector2 pos = colliderBottom + new Vector2(limpAmount, HeadPosition.Value + stepLift * CurrentGroundedParams.StepLiftHeadMultiplier);

                    if (head != mainLimb)
                    {
                        pos.X = head.SimPosition.X;
                    }

                    head.MoveToPos(pos, HeadMoveForce);
                    head.PullJointEnabled = true;
                    head.PullJointWorldAnchorB = pos;
                }
            }

            if (TailAngle.HasValue)
            {
                bool isAngleApplied = false;
                foreach (var limb in Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    if (limb.type != LimbType.Tail) { continue; }
                    if (!limb.Params.ApplyTailAngle) { continue; }
                    RotateTail(limb);
                    isAngleApplied = true;
                }
                if (!isAngleApplied)
                {
                    RotateTail(GetLimb(LimbType.Tail));
                }

                void RotateTail(Limb tail)
                {
                    if (tail != null)
                    {
                        SmoothRotateWithoutWrapping(tail, movementAngle + TailAngle.Value * Dir, mainLimb, TailTorque);
                    }
                }
            }

            float prevWalkPos = WalkPos;
            WalkPos -= mainLimb.LinearVelocity.X * (CurrentAnimationParams.CycleSpeed / RagdollParams.JointScale / 100.0f);

            Vector2 transformedStepSize = Vector2.Zero;
            if (Math.Abs(TargetMovement.X) > 0.01f)
            {
                transformedStepSize = new Vector2(
                    (float)Math.Cos(WalkPos) * StepSize.Value.X * 3.0f,
                    (float)Math.Sin(WalkPos) * StepSize.Value.Y * 2.0f);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) { continue; }
                if (Math.Abs(limb.Params.ConstantTorque) > 0)
                {
                    limb.body.SmoothRotate(MainLimb.Rotation + MathHelper.ToRadians(limb.Params.ConstantAngle) * Dir, limb.Mass * limb.Params.ConstantTorque, wrapAngle: true);
                }
                if (limb.Params.BlinkFrequency > 0 && !limb.Params.OnlyBlinkInWater)
                {
                    limb.UpdateBlink(deltaTime, MainLimb.Rotation);
                }
                switch (limb.type)
                {
                    case LimbType.LeftFoot:
                    case LimbType.RightFoot:
                        Vector2 footPos = new Vector2(limb.SimPosition.X, colliderBottom.Y);

                        if (limb.RefJointIndex > -1)
                        {
                            if (LimbJoints.Length <= limb.RefJointIndex)
                            {
                                DebugConsole.ThrowError($"Reference joint index {limb.RefJointIndex} is out of array. This is probably due to a missing joint. If you just deleted a joint, don't do that without first removing the reference joint indices from the limbs. If this is not the case, please ensure that you have defined the index to the right joint.");
                            }
                            else
                            {
                                footPos.X = LimbJoints[limb.RefJointIndex].WorldAnchorA.X;
                            }
                        }
                        footPos.X += limb.StepOffset.X * Dir;
                        footPos.Y += limb.StepOffset.Y;

                        bool playFootstepSound = false;
                        if (limb.type == LimbType.LeftFoot)
                        {
                            if (Math.Sign(Math.Sin(prevWalkPos)) > 0 && Math.Sign(transformedStepSize.Y) < 0)
                            {
                                playFootstepSound = true;
                            }

                            limb.DebugRefPos = footPos + Vector2.UnitX * movement.X * 0.1f;
                            limb.DebugTargetPos = footPos + new Vector2(
                                transformedStepSize.X + movement.X * 0.1f,
                                (transformedStepSize.Y > 0.0f) ? transformedStepSize.Y : 0.0f);
                            limb.MoveToPos(limb.DebugTargetPos, FootMoveForce);
                        }
                        else if (limb.type == LimbType.RightFoot)
                        {
                            if (Math.Sign(Math.Sin(prevWalkPos)) < 0 && Math.Sign(transformedStepSize.Y) > 0)
                            {
                                playFootstepSound = true;
                            }

                            limb.DebugRefPos = footPos + Vector2.UnitX * movement.X * 0.1f;
                            limb.DebugTargetPos = footPos + new Vector2(
                                -transformedStepSize.X + movement.X * 0.1f,
                                (-transformedStepSize.Y > 0.0f) ? -transformedStepSize.Y : 0.0f);
                            limb.MoveToPos(limb.DebugTargetPos, FootMoveForce);
                        }

                        if (playFootstepSound) 
                        { 
#if CLIENT
                            PlayImpactSound(limb); 
#endif
                        }

                        if (CurrentGroundedParams.FootAnglesInRadians.ContainsKey(limb.Params.ID))
                        {
                            SmoothRotateWithoutWrapping(limb,
                                movementAngle + CurrentGroundedParams.FootAnglesInRadians[limb.Params.ID] * Dir,
                                mainLimb, FootTorque);
                        }
                        break;
                    case LimbType.LeftLeg:
                    case LimbType.RightLeg:
                        if (Math.Abs(CurrentGroundedParams.LegTorque) > 0)
                        {
                            limb.body.ApplyTorque(limb.Mass * CurrentGroundedParams.LegTorque * Dir);
                        }
                        break;
                }
            }
        }

        void UpdateDying(float deltaTime)
        {
            if (deathAnimDuration <= 0.0f) { return; }

            float noise = (PerlinNoise.GetPerlin(WalkPos * 0.002f, WalkPos * 0.003f) - 0.5f) * 5.0f;
            float animStrength = (1.0f - deathAnimTimer / deathAnimDuration);

            Limb baseLimb = GetLimb(LimbType.Head);
            //if head is the main limb, it technically can't be severed - the rest of the limbs are considered severed if the head gets cut off
            if (baseLimb == MainLimb)
            {
                int connectedToHeadCount = GetConnectedLimbs(baseLimb).Count;
                //if there's nothing connected to the head, don't make it wiggle by itself
                if (connectedToHeadCount == 1) { baseLimb = null; }
                Limb torso = GetLimb(LimbType.Torso, excludeSevered: false);
                if (torso != null)
                {
                    //if there are more limbs connected to the torso than to the head, make the torso wiggle instead
                    int connectedToTorsoCount = GetConnectedLimbs(torso).Count;
                    if (connectedToTorsoCount > connectedToHeadCount)
                    {
                        baseLimb = torso;
                    }
                }
            }
            else if (baseLimb == null)
            {
                baseLimb = GetLimb(LimbType.Torso, excludeSevered: true);
                if (baseLimb == null) { return; }
            }

            var connectedToBaseLimb = GetConnectedLimbs(baseLimb);

            Limb tail = GetLimb(LimbType.Tail);
            if (baseLimb != null) { baseLimb.body.ApplyTorque((float)(Math.Sqrt(baseLimb.Mass) * Dir * (Math.Sin(WalkPos) + noise)) * 30.0f * animStrength); }
            if (tail != null && connectedToBaseLimb.Contains(tail)) { tail.body.ApplyTorque((float)(Math.Sqrt(tail.Mass) * -Dir * (Math.Sin(WalkPos) + noise)) * 30.0f * animStrength); }

            WalkPos += deltaTime * 10.0f * animStrength;

            Vector2 centerOfMass = GetCenterOfMass();

            foreach (Limb limb in Limbs)
            {
                if (!connectedToBaseLimb.Contains(limb)) { continue; }
#if CLIENT
                if (limb.LightSource != null)
                {
                    limb.LightSource.Color = Color.Lerp(limb.InitialLightSourceColor, Color.TransparentBlack, deathAnimTimer / deathAnimDuration);
                    if (limb.InitialLightSpriteAlpha.HasValue)
                    {
                        limb.LightSource.OverrideLightSpriteAlpha = MathHelper.Lerp(limb.InitialLightSpriteAlpha.Value, 0.0f, deathAnimTimer / deathAnimDuration);
                    }
                }
#endif
                if (limb.type == LimbType.Head || limb.type == LimbType.Tail || limb.IsSevered || !limb.body.Enabled) continue;
                if (limb.Mass <= 0.0f)
                {
                    string errorMsg = "Creature death animation error: invalid limb mass on character \"" + character.SpeciesName + "\" (type: " + limb.type + ", mass: " + limb.Mass + ")";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("FishAnimController.UpdateDying:InvalidMass" + character.ID, GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    deathAnimTimer = deathAnimDuration;
                    return;
                }

                Vector2 diff = (centerOfMass - limb.SimPosition);
                if (!MathUtils.IsValid(diff))
                {
                    string errorMsg = "Creature death animation error: invalid diff (center of mass: " + centerOfMass + ", limb position: " + limb.SimPosition + ")";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("FishAnimController.UpdateDying:InvalidDiff" + character.ID, GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    deathAnimTimer = deathAnimDuration;
                    return;
                }

                limb.body.ApplyForce(diff * (float)(Math.Sin(WalkPos) * Math.Sqrt(limb.Mass)) * 30.0f * animStrength, maxVelocity: 10.0f);
            }
        }

        private void SmoothRotateWithoutWrapping(Limb limb, float angle, Limb referenceLimb, float torque)
        {
            //make sure the angle "has the same number of revolutions" as the reference limb
            //(e.g. we don't want to rotate the legs to 0 if the torso is at 360, because that'd blow up the hip joints) 
            while (referenceLimb.Rotation - angle > MathHelper.TwoPi)
            {
                angle += MathHelper.TwoPi;
            }
            while (referenceLimb.Rotation - angle < -MathHelper.TwoPi)
            {
                angle -= MathHelper.TwoPi;
            }

            limb?.body.SmoothRotate(angle, torque, wrapAngle: false);
        }

        public override void Flip()
        {
            base.Flip();
            foreach (Limb l in Limbs)
            {
                if (l.IsSevered) { continue; }
                if (!l.DoesFlip) { continue; }         
                if (RagdollParams.IsSpritesheetOrientationHorizontal)
                {
                    //horizontally aligned limbs need to be flipped 180 degrees
                    l.body.SetTransform(l.SimPosition, l.body.Rotation + MathHelper.Pi * Dir);
				}
                //no need to do anything when flipping vertically oriented limbs
                //the sprite gets flipped horizontally, which does the job
            }
        }

        public void Mirror(bool lerp = true)
        {
            Vector2 centerOfMass = GetCenterOfMass();

            foreach (Limb l in Limbs)
            {
                if (l.IsSevered) { continue; }

                TrySetLimbPosition(l,
                    centerOfMass,
                    new Vector2(centerOfMass.X - (l.SimPosition.X - centerOfMass.X), l.SimPosition.Y),
                    lerp);

                l.body.PositionSmoothingFactor = 0.8f;

                if (!l.DoesFlip) { continue; }
                if (RagdollParams.IsSpritesheetOrientationHorizontal)
				{
                    //horizontally oriented sprites can be mirrored by rotating 180 deg and inverting the angle
                    l.body.SetTransform(l.SimPosition, -(l.body.Rotation + MathHelper.Pi));
				}    
                else
				{
                    //vertically oriented limbs can be mirrored by inverting the angle (neutral angle is straight upwards)
                    l.body.SetTransform(l.SimPosition, -l.body.Rotation);
				}        
            }
            if (character.SelectedCharacter != null && CanDrag(character.SelectedCharacter))
            {
                float diff = character.SelectedCharacter.SimPosition.X - centerOfMass.X;
                if (diff < 100.0f)
                {
                    character.SelectedCharacter.AnimController.SetPosition(
                        new Vector2(centerOfMass.X - diff, character.SelectedCharacter.SimPosition.Y), lerp);
                }
            }
        }  
    }
}
