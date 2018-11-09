using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class HumanoidAnimController : AnimController
    {
        public bool Crouching;

        private bool aiming;

        private float walkAnimSpeed;

        private float movementLerp;

        private float thighTorque;

        private float cprAnimState;
        private float cprPump;

        private float inWaterTimer;
        private bool swimming;

        private float useItemTimer;
        
        protected override float TorsoPosition
        {
            get
            {
                return Crouching ? base.TorsoPosition - base.HeadPosition * 0.3f : base.TorsoPosition;
            }
        }

        protected override float TorsoAngle
        {
            get
            {
                return Crouching ? base.TorsoAngle + 0.5f : base.TorsoAngle;
            }
        }

        public override Vector2 AimSourceSimPos
        {
            get
            {
                float shoulderHeight = Collider.height / 2.0f - 0.1f;
                if (inWater)
                {
                    shoulderHeight += 0.4f;
                }
                else if (Crouching)
                {
                    shoulderHeight -= 0.15f;
                }

                return Collider.SimPosition + new Vector2(
                    (float)Math.Sin(-Collider.Rotation),
                    (float)Math.Cos(-Collider.Rotation)) * shoulderHeight;
            }
        }

        public HumanoidAnimController(Character character, XElement element)
            : base(character, element)
        {
            walkAnimSpeed = element.GetAttributeFloat("walkanimspeed", 4.0f);
            walkAnimSpeed = MathHelper.ToRadians(walkAnimSpeed);

            movementLerp = element.GetAttributeFloat("movementlerp", 0.4f);

            thighTorque = element.GetAttributeFloat("thightorque", -5.0f);
        }

        public override void UpdateAnim(float deltaTime)
        {
            if (Frozen) return;

            levitatingCollider = true;
            ColliderIndex = Crouching ? 1 : 0;
            if (!Crouching && ColliderIndex == 1) Crouching = true;

            //stun (= disable the animations) if the ragdoll receives a large enough impact
            if (strongestImpact > 0.0f)
            {
                character.SetStun(MathHelper.Min(strongestImpact * 0.5f, 5.0f));
                strongestImpact = 0.0f;
                return;
            }

            if (!character.AllowInput)
            {
                levitatingCollider = false;
                Collider.FarseerBody.FixedRotation = false;

                if (Math.Abs(Collider.Rotation - GetLimb(LimbType.Torso).Rotation) > Math.PI * 0.6f)
                {
                    Collider.SetTransform(Collider.SimPosition, MathHelper.WrapAngle(Collider.Rotation + (float)Math.PI));
                }

                Vector2 diff = GetLimb(LimbType.Waist).SimPosition - Collider.SimPosition;
                if (diff.LengthSquared() > 10.0f * 10.0f)
                {
                    Collider.SetTransform(GetLimb(LimbType.Waist).SimPosition, GetLimb(LimbType.Torso).Rotation);
                }
                else
                {
                    Collider.LinearVelocity = diff * 20.0f;
                    Collider.SmoothRotate(GetLimb(LimbType.Torso).Rotation);
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

            if (character.LockHands)
            {
                var leftHand = GetLimb(LimbType.LeftHand);
                var rightHand = GetLimb(LimbType.RightHand);

                var waist = GetLimb(LimbType.Waist);

                rightHand.Disabled = true;
                leftHand.Disabled = true;

                Vector2 midPos = waist.SimPosition;
                Matrix torsoTransform = Matrix.CreateRotationZ(waist.Rotation);

                midPos += Vector2.Transform(new Vector2(-0.3f * Dir, -0.2f), torsoTransform);

                if (rightHand.PullJointEnabled) midPos = (midPos + rightHand.PullJointWorldAnchorB) / 2.0f;

                HandIK(rightHand, midPos);
                HandIK(leftHand, midPos);
            }
            else
            {
                if (Anim != Animation.UsingConstruction) ResetPullJoints();
            }

            if (SimplePhysicsEnabled)
            {
                UpdateStandingSimple();
                return;
            }

            if (character.SelectedCharacter != null)
            {
                DragCharacter(character.SelectedCharacter);
            }

            switch (Anim)
            {
                case Animation.Climbing:
                    levitatingCollider = false;
                    UpdateClimbing();
                    break;
                case Animation.CPR:
                    UpdateCPR(deltaTime);
                    break;
                case Animation.UsingConstruction:
                default:
                    if (Anim == Animation.UsingConstruction)
                    {
                        useItemTimer -= deltaTime;
                        if (useItemTimer <= 0.0f) Anim = Animation.None;
                    }
                    
                    //0.5 second delay for switching between swimming and walking
                    //prevents rapid switches between swimming/walking if the water level is fluctuating around the minimum swimming depth
                    if (inWater)
                    {
                        inWaterTimer = Math.Max(inWaterTimer + deltaTime, 0.5f);
                        if (inWaterTimer >= 1.0f) swimming = true;
                    }
                    else
                    {
                        inWaterTimer = Math.Min(inWaterTimer - deltaTime, 0.5f);
                        if (inWaterTimer <= 0.0f) swimming = false;
                    }

                    if (swimming)
                    {
                        UpdateSwimming();
                    }
                    else
                    {
                        UpdateStanding();
                    }

                    break;
            }

            if (TargetDir != dir) Flip();

            foreach (Limb limb in Limbs)
            {
                limb.Disabled = false;
            }

            aiming = false;
            if (character.IsRemotePlayer && GameMain.Server == null) Collider.LinearVelocity = Vector2.Zero;
        }



        void UpdateStanding()
        {
            Vector2 handPos;

            //if you're allergic to magic numbers, stop reading now

            Limb leftFoot = GetLimb(LimbType.LeftFoot);
            Limb rightFoot = GetLimb(LimbType.RightFoot);
            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);

            Limb waist = GetLimb(LimbType.Waist);

            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Limb leftLeg = GetLimb(LimbType.LeftLeg);
            Limb rightLeg = GetLimb(LimbType.RightLeg);
            
            float getUpSpeed = 0.3f;
            float walkCycleSpeed = movement.X * walkAnimSpeed;
            if (Stairs != null)
            {
                TargetMovement = new Vector2(MathHelper.Clamp(TargetMovement.X, -1.5f, 1.5f), TargetMovement.Y);

                /*if ((TargetMovement.X > 0.0f && stairs.StairDirection == Direction.Right) ||
                    TargetMovement.X < 0.0f && stairs.StairDirection == Direction.Left)
                {
                    TargetMovement *= 1.7f;
                    //walkCycleSpeed *= 1.0f;
                }*/
            }

            Vector2 colliderPos = GetColliderBottom();
            if (Math.Abs(TargetMovement.X) > 1.0f)
            {
                float slowdownAmount = 0.0f;
                if (currentHull != null)
                {
                    //full slowdown (1.5f) when water is up to the torso
                    surfaceY = ConvertUnits.ToSimUnits(currentHull.Surface);
                    slowdownAmount = MathHelper.Clamp((surfaceY - colliderPos.Y) / torsoPosition, 0.0f, 1.0f) * 1.5f;
                }

                float maxSpeed = Math.Max(TargetMovement.Length() - slowdownAmount, 1.0f);
                TargetMovement = Vector2.Normalize(TargetMovement) * maxSpeed;
            }

            float walkPosX = (float)Math.Cos(walkPos);
            float walkPosY = (float)Math.Sin(walkPos);
            float runningModifier = (float)Math.Max(Math.Min(Math.Abs(TargetMovement.X), 3.0f) / 1.5f, 1.0);
            
            Vector2 stepSize = new Vector2(
                this.stepSize.X * walkPosX * runningModifier,
                this.stepSize.Y * walkPosY * runningModifier * runningModifier);

            if (Crouching) stepSize *= 0.5f;

            float footMid = colliderPos.X;// (leftFoot.SimPosition.X + rightFoot.SimPosition.X) / 2.0f;

            movement = overrideTargetMovement == Vector2.Zero ?
                MathUtils.SmoothStep(movement, TargetMovement * walkSpeed, movementLerp) :
                overrideTargetMovement;

            if (Math.Abs(movement.X) < 0.005f)
            {
                movement.X = 0.0f;
            }

            movement.Y = 0.0f;

            for (int i = 0; i < 2; i++)
            {
                Limb leg = GetLimb((i == 0) ? LimbType.LeftThigh : LimbType.RightThigh);// : leftLeg;

                float shortestAngle = leg.Rotation - torso.Rotation;

                if (Math.Abs(shortestAngle) < 2.5f) continue;

                if (Math.Abs(shortestAngle) > 5.0f)
                {
                    TargetDir = TargetDir == Direction.Right ? Direction.Left : Direction.Right;
                }
                else
                {

                    leg.body.ApplyTorque(shortestAngle * 10.0f);

                    leg = GetLimb((i == 0) ? LimbType.LeftLeg : LimbType.RightLeg);
                    leg.body.ApplyTorque(-shortestAngle * 10.0f);
                }
            }

            if (onGround && (!character.IsRemotePlayer || GameMain.Server != null))
            {
                //move slower if collider isn't upright
                float rotationFactor = (float)Math.Abs(Math.Cos(Collider.Rotation));

                Collider.LinearVelocity = new Vector2(
                        movement.X * rotationFactor,
                        Collider.LinearVelocity.Y > 0.0f ? Collider.LinearVelocity.Y * 0.5f : Collider.LinearVelocity.Y);
            }

            getUpSpeed = getUpSpeed * Math.Max(head.SimPosition.Y - colliderPos.Y, 0.5f);

            torso.PullJointEnabled = true;
            head.PullJointEnabled = true;
            waist.PullJointEnabled = true;
            
            float floorPos = GetFloorY(colliderPos + new Vector2(Math.Sign(movement.X) * 0.5f, 1.0f));
            bool onSlope = floorPos > GetColliderBottom().Y + 0.05f;

            if (Stairs != null || onSlope)
            {
                torso.PullJointWorldAnchorB = new Vector2(
                    MathHelper.SmoothStep(torso.SimPosition.X, footMid + movement.X * 0.25f, getUpSpeed * 0.8f),
                    MathHelper.SmoothStep(torso.SimPosition.Y, colliderPos.Y + TorsoPosition - Math.Abs(walkPosX * 0.05f), getUpSpeed * 2.0f));


                head.PullJointWorldAnchorB = new Vector2(
                    MathHelper.SmoothStep(head.SimPosition.X, footMid + movement.X * (Crouching ? 0.6f : 0.25f), getUpSpeed * 0.8f),
                    MathHelper.SmoothStep(head.SimPosition.Y, colliderPos.Y + HeadPosition - Math.Abs(walkPosX * 0.05f), getUpSpeed * 2.0f));

                waist.PullJointWorldAnchorB = waist.SimPosition;
            }
            else
            {
                if (!onGround) movement = Vector2.Zero;

                torso.PullJointWorldAnchorB =
                    MathUtils.SmoothStep(torso.SimPosition,
                    new Vector2(footMid + movement.X * 0.2f, colliderPos.Y + TorsoPosition), getUpSpeed);

                head.PullJointWorldAnchorB = 
                    MathUtils.SmoothStep(head.SimPosition,
                    new Vector2(footMid + movement.X * (Crouching && Math.Sign(movement.X) == Math.Sign(Dir) ? 0.6f : 0.2f), colliderPos.Y + HeadPosition), getUpSpeed * 1.2f);

                waist.PullJointWorldAnchorB = waist.SimPosition + movement * 0.06f;
            }

            if (!onGround)
            {
                Vector2 move =  torso.PullJointWorldAnchorB - torso.SimPosition;

                foreach (Limb limb in Limbs)
                {
                    MoveLimb(limb, limb.SimPosition+move, 15.0f, true);
                }

                return;
            }

            //moving horizontally
            if (TargetMovement.X != 0.0f)
            {
                //progress the walking animation
                walkPos -= (walkCycleSpeed / runningModifier) * 0.8f;

                for (int i = -1; i < 2; i += 2)
                {
                    Limb foot = i == -1 ? leftFoot : rightFoot;
                    Limb leg = i == -1 ? leftLeg : rightLeg;

                    Vector2 footPos = stepSize * -i;
                    if (stepSize.Y < 0.0f) stepSize.Y = -0.15f;

                    if (onSlope && Stairs == null)
                    {
                        footPos.Y *= 2.0f;
                    }
                    footPos.Y = Math.Min(waist.SimPosition.Y - colliderPos.Y - 0.4f, footPos.Y);

                    MoveLimb(foot, footPos + colliderPos, 15.0f, true);
                    foot.body.SmoothRotate(leg.body.Rotation + MathHelper.PiOver2 * Dir * 1.6f, 20.0f * runningModifier);
                }

                if (runningModifier > 1.0f)
                {
                    if (walkPosY > 0.0f)
                    {
                        GetLimb(LimbType.LeftThigh).body.ApplyTorque(-walkPosY * Dir * Math.Abs(movement.X) * thighTorque);
                    }
                    else
                    {
                        GetLimb(LimbType.RightThigh).body.ApplyTorque(walkPosY * Dir * Math.Abs(movement.X) * thighTorque);
                    }
                }

                if (legTorque > 0.0f)
                {
                    if (Math.Sign(walkPosX) != Math.Sign(movement.X))
                    {
                        GetLimb(LimbType.LeftLeg).body.ApplyTorque(-walkPosY * Dir * Math.Abs(movement.X) * legTorque / runningModifier);
                    }
                    else
                    {
                        GetLimb(LimbType.RightLeg).body.ApplyTorque(walkPosY * Dir * Math.Abs(movement.X) * legTorque / runningModifier);
                    }
                }

                //calculate the positions of hands
                handPos = torso.SimPosition;
                handPos.X = -walkPosX * 0.4f;

                float lowerY = -1.0f + (runningModifier - 1.0f) * 0.8f;

                handPos.Y = lowerY + (float)(Math.Abs(Math.Sin(walkPos - Math.PI * 1.5f) * 0.15 * runningModifier));

                Vector2 posAddition = new Vector2(-movement.X * 0.015f * runningModifier, 0.0f);

                if (!rightHand.Disabled)
                {
                    HandIK(rightHand, torso.SimPosition + posAddition +
                        new Vector2(
                            -handPos.X,
                            (Math.Sign(walkPosX) == Math.Sign(Dir)) ? handPos.Y : lowerY), 0.7f * runningModifier);
                }

                if (!leftHand.Disabled)
                {
                    HandIK(leftHand, torso.SimPosition + posAddition +
                        new Vector2(
                            handPos.X,
                            (Math.Sign(walkPosX) == Math.Sign(-Dir)) ? handPos.Y : lowerY), 0.7f * runningModifier);
                }

            }
            else
            {
                //float movementFactor = (movement.X / 4.0f) * movement.X * Math.Sign(movement.X);

                for (int i = -1; i < 2; i += 2)
                {
                    Vector2 footPos = colliderPos;
                    
                    if (Crouching)
                    {
                        footPos = new Vector2(
                            waist.SimPosition.X + Math.Sign(stepSize.X * i) * Dir * 0.3f,
                            colliderPos.Y - 0.1f);
                    }
                    else
                    {
                        footPos = new Vector2(GetCenterOfMass().X + stepSize.X * i * 0.2f, colliderPos.Y - 0.1f);
                    }

                    if (Stairs == null)
                    {
                        footPos.Y = Math.Max(Math.Min(floorPos, footPos.Y + 0.5f), footPos.Y);
                    }

                    var foot = i == -1 ? rightFoot : leftFoot;

                    MoveLimb(foot, footPos, Math.Abs(foot.SimPosition.X - footPos.X) * 100.0f, true);
                }

                leftFoot.body.SmoothRotate(Dir * MathHelper.PiOver2, 50.0f);
                rightFoot.body.SmoothRotate(Dir * MathHelper.PiOver2, 50.0f);

                if (!rightHand.Disabled)
                {
                    rightHand.body.SmoothRotate(0.0f, 5.0f);

                    var rightArm = GetLimb(LimbType.RightArm);
                    rightArm.body.SmoothRotate(0.0f, 20.0f);
                }

                if (!leftHand.Disabled)
                {
                    leftHand.body.SmoothRotate(0.0f, 5.0f);

                    var leftArm = GetLimb(LimbType.LeftArm);
                    leftArm.body.SmoothRotate(0.0f, 20.0f);
                }
            }
        }

        void UpdateStandingSimple()
        {
            if (Math.Abs(movement.X) < 0.005f)
            {
                movement.X = 0.0f;
            }

            if (InWater)
            {
                if (inWater && movement.LengthSquared() > 0.00001f)
                {
                    movement = Vector2.Normalize(movement);
                }
                movement = MathUtils.SmoothStep(movement, TargetMovement, movementLerp);
                Collider.LinearVelocity = Vector2.Lerp(Collider.LinearVelocity, movement * swimSpeed, movementLerp);
            }
            else
            {
                movement = MathUtils.SmoothStep(movement, TargetMovement * walkSpeed, movementLerp);
                if (onGround && (!character.IsRemotePlayer || GameMain.Server != null))
                {
                    Collider.LinearVelocity = new Vector2(
                            movement.X,
                            Collider.LinearVelocity.Y > 0.0f ? Collider.LinearVelocity.Y * 0.5f : Collider.LinearVelocity.Y);
                }
            }
        }

        private void ClimbOverObstacles()
        {
            if (Collider.FarseerBody.ContactList == null || Math.Abs(movement.X) < 0.01f) return;

            //check if the collider is touching a suitable obstacle to climb over
            Vector2? handle = null;
            FarseerPhysics.Dynamics.Contacts.ContactEdge ce = Collider.FarseerBody.ContactList;
            while (ce != null && ce.Contact != null)
            {
                if (ce.Contact.Enabled && ce.Contact.IsTouching && ce.Contact.FixtureA.CollisionCategories.HasFlag(Physics.CollisionWall))
                {
                    Vector2 contactNormal;
                    FarseerPhysics.Common.FixedArray2<Vector2> contactPos;
                    ce.Contact.GetWorldManifold(out contactNormal, out contactPos);

                    //only climb if moving towards the obstacle
                    if (Math.Sign(contactPos[0].X - Collider.SimPosition.X) == Math.Sign(movement.X) &&
                        (handle == null || contactPos[0].Y > ((Vector2)handle).Y))
                    {
                        handle = contactPos[0];
                    }
                }

                ce = ce.Next;
            }
            
            if (handle == null) return;

            float colliderBottomY = GetColliderBottom().Y;

            //the contact point should be higher than the bottom of the collider
            if (((Vector2)handle).Y < colliderBottomY + 0.01f ||
                ((Vector2)handle).Y > Collider.SimPosition.Y) return;
            
            //find the height of the floor below the torso
            //(if moving towards towards an obstacle that's low enough to climb over, the torso should be above it)
            float obstacleY = GetFloorY(GetLimb(LimbType.Torso));

            if (obstacleY > colliderBottomY)
            {
                //higher vertical velocity for taller obstacles
                Collider.LinearVelocity += Vector2.UnitY * (((Vector2)handle).Y - colliderBottomY + 0.01f) * 50;
                onGround = true;
            }
        }

        void UpdateSwimming()
        {
            IgnorePlatforms = true;

            Vector2 footPos, handPos;

            float surfaceLimiter = 1.0f;

            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);
            
            if (currentHull != null && (currentHull.Rect.Y - currentHull.Surface > 50.0f))
            {
                surfaceLimiter = (ConvertUnits.ToDisplayUnits(Collider.SimPosition.Y + 0.4f) - surfaceY);
                surfaceLimiter = Math.Max(1.0f, surfaceLimiter);
                if (surfaceLimiter > 50.0f) return;
            }

            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Limb leftFoot = GetLimb(LimbType.LeftFoot);
            Limb rightFoot = GetLimb(LimbType.RightFoot);
            
            float rotation = MathHelper.WrapAngle(Collider.Rotation);
            rotation = MathHelper.ToDegrees(rotation);
            if (rotation < 0.0f) rotation += 360;

            if (!character.IsRemotePlayer && !aiming && Anim != Animation.UsingConstruction)
            {
                if (rotation > 20 && rotation < 170)
                    TargetDir = Direction.Left;
                else if (rotation > 190 && rotation < 340)
                    TargetDir = Direction.Right;
            }

            float targetSpeed = TargetMovement.Length();

            if (targetSpeed > 0.1f)
            {
                if (!aiming)
                {
                    float newRotation = MathUtils.VectorToAngle(TargetMovement) - MathHelper.PiOver2;
                    Collider.SmoothRotate(newRotation, 5.0f);
                    //torso.body.SmoothRotate(newRotation);
                    
                }
            }
            else
            {
                if (aiming)
                {
                    Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);
                    Vector2 diff = (mousePos - torso.SimPosition) * Dir;

                    TargetMovement = new Vector2(0.0f, -0.1f);

                    float newRotation = MathUtils.VectorToAngle(diff);
                    Collider.SmoothRotate(newRotation, 5.0f);
                }
            }

            torso.body.SmoothRotate(Collider.Rotation);
            torso.body.MoveToPos(Collider.SimPosition + new Vector2((float)Math.Sin(-Collider.Rotation), (float)Math.Cos(-Collider.Rotation))*0.4f, 5.0f);
            
            if (TargetMovement == Vector2.Zero) return;

            movement = MathUtils.SmoothStep(movement, TargetMovement, 0.3f);

            //dont try to move upwards if head is already out of water
            if (surfaceLimiter > 1.0f && TargetMovement.Y > 0.0f)
            {
                if (TargetMovement.X == 0.0f)
                {
                    //pull head above water
                    head.body.SmoothRotate(0.0f, 5.0f);

                    walkPos += 0.05f;
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

                movement.Y = movement.Y - (surfaceLimiter - 1.0f) * 0.01f;
            }

            if (!character.IsRemotePlayer || GameMain.Server != null)
            {
                Collider.LinearVelocity = Vector2.Lerp(Collider.LinearVelocity, movement * swimSpeed, movementLerp);
            }
                        
            walkPos += movement.Length() * 0.2f;
            footPos = Collider.SimPosition - new Vector2((float)Math.Sin(-Collider.Rotation), (float)Math.Cos(-Collider.Rotation)) * 0.4f;
            
            for (int i = -1; i<2; i+=2)
            {
                var thigh = i == -1 ? GetLimb(LimbType.LeftThigh) : GetLimb(LimbType.RightThigh);
                var leg = i == -1 ? GetLimb(LimbType.LeftLeg) : GetLimb(LimbType.RightLeg);
                
                float thighDiff = Math.Abs(MathUtils.GetShortestAngle(torso.Rotation, thigh.Rotation));
                if (thighDiff > MathHelper.PiOver2)
                {
                    //thigh bent too close to the torso -> force the leg to extend
                    float thighTorque = thighDiff * thigh.Mass * Math.Sign(torso.Rotation - thigh.Rotation) * 10.0f;
                    thigh.body.ApplyTorque(thighTorque);
                    leg.body.ApplyTorque(thighTorque);
                }
                else
                {
                    thigh.body.SmoothRotate(torso.Rotation + (float)Math.Sin(walkPos) * i * 0.3f, 2.0f);
                }
            }
            
            Vector2 transformedFootPos = new Vector2((float)Math.Sin(walkPos) * 0.5f, 0.0f);
            transformedFootPos = Vector2.Transform(
                transformedFootPos,
                Matrix.CreateRotationZ(Collider.Rotation));

            MoveLimb(rightFoot, footPos - transformedFootPos, 1.0f);
            MoveLimb(leftFoot, footPos + transformedFootPos, 1.0f);            

            handPos = (torso.SimPosition + head.SimPosition) / 2.0f;

            //at the surface, not moving sideways -> hands just float around
            if (!headInWater && TargetMovement.X == 0.0f && TargetMovement.Y > 0)
            {
                handPos.X = handPos.X + Dir * 0.6f;

                float wobbleAmount = 0.1f;

                if (!rightHand.Disabled)
                {
                    MoveLimb(rightHand, new Vector2(
                        handPos.X + (float)Math.Sin(walkPos / 1.5f) * wobbleAmount,
                        handPos.Y + (float)Math.Sin(walkPos / 3.5f) * wobbleAmount - 0.25f), 1.5f);
                }

                if (!leftHand.Disabled)
                {
                    MoveLimb(leftHand, new Vector2(
                        handPos.X + (float)Math.Sin(walkPos / 2.0f) * wobbleAmount,
                        handPos.Y + (float)Math.Sin(walkPos / 3.0f) * wobbleAmount - 0.25f), 1.5f);
                }

                return;
            }

            handPos += head.LinearVelocity * 0.1f;

            float handCyclePos = walkPos / 2.0f * -Dir;
            float handPosX = (float)Math.Cos(handCyclePos) * 0.4f;
            float handPosY = (float)Math.Sin(handCyclePos) * 1.0f;
            handPosY = MathHelper.Clamp(handPosY, -0.8f, 0.8f);

            Matrix rotationMatrix = Matrix.CreateRotationZ(torso.Rotation);

            if (!rightHand.Disabled)
            {
                Vector2 rightHandPos = new Vector2(-handPosX, -handPosY);
                rightHandPos.X = (Dir == 1.0f) ? Math.Max(0.3f, rightHandPos.X) : Math.Min(-0.3f, rightHandPos.X);
                rightHandPos = Vector2.Transform(rightHandPos, rotationMatrix);

                HandIK(rightHand, handPos + rightHandPos, 0.5f);
            }

            if (!leftHand.Disabled)
            {
                Vector2 leftHandPos = new Vector2(handPosX, handPosY);
                leftHandPos.X = (Dir == 1.0f) ? Math.Max(0.3f, leftHandPos.X) : Math.Min(-0.3f, leftHandPos.X);
                leftHandPos = Vector2.Transform(leftHandPos, rotationMatrix);

                HandIK(leftHand, handPos + leftHandPos, 0.5f);
            }
        }

        void UpdateClimbing()
        {
            if (character.SelectedConstruction == null || character.SelectedConstruction.GetComponent<Ladder>() == null)
            {
                Anim = Animation.None;
                return;
            }

            onGround = false;
            IgnorePlatforms = true;

            Vector2 tempTargetMovement = TargetMovement;

            tempTargetMovement.Y = Math.Min(tempTargetMovement.Y, 1.0f);

            movement = MathUtils.SmoothStep(movement, tempTargetMovement, 0.3f);

            Limb leftFoot = GetLimb(LimbType.LeftFoot);
            Limb rightFoot = GetLimb(LimbType.RightFoot);
            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);

            Limb waist = GetLimb(LimbType.Waist);

            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Vector2 ladderSimPos = ConvertUnits.ToSimUnits(
                character.SelectedConstruction.Rect.X + character.SelectedConstruction.Rect.Width / 2.0f,
                character.SelectedConstruction.Rect.Y);

            float stepHeight = ConvertUnits.ToSimUnits(30.0f);

            if (currentHull == null && character.SelectedConstruction.Submarine != null)
            {
                ladderSimPos += character.SelectedConstruction.Submarine.SimPosition;
            }
            else if (currentHull.Submarine != null && currentHull.Submarine != character.SelectedConstruction.Submarine)
            {
                ladderSimPos += character.SelectedConstruction.Submarine.SimPosition - currentHull.Submarine.SimPosition;
            }

            MoveLimb(head, new Vector2(ladderSimPos.X - 0.27f * Dir, Collider.SimPosition.Y + 0.9f - colliderHeightFromFloor), 10.5f);
            MoveLimb(torso, new Vector2(ladderSimPos.X - 0.27f * Dir, Collider.SimPosition.Y + 0.7f - colliderHeightFromFloor), 10.5f);
            MoveLimb(waist, new Vector2(ladderSimPos.X - 0.35f * Dir, Collider.SimPosition.Y + 0.6f - colliderHeightFromFloor), 10.5f);

            Collider.MoveToPos(new Vector2(ladderSimPos.X - 0.2f * Dir, Collider.SimPosition.Y), 10.5f);            

            bool slide = targetMovement.Y < -1.1f;

            Vector2 handPos = new Vector2(
                ladderSimPos.X,
                Collider.SimPosition.Y + 0.8f + movement.Y * 0.1f - ladderSimPos.Y);

            handPos.Y = Math.Min(-0.2f, handPos.Y) - colliderHeightFromFloor;

            MoveLimb(leftHand,
                new Vector2(handPos.X,
                (slide ? handPos.Y : MathUtils.Round(handPos.Y - stepHeight, stepHeight * 2.0f) + stepHeight) + ladderSimPos.Y),
                5.2f);

            MoveLimb(rightHand,
                new Vector2(handPos.X,
                (slide ? handPos.Y : MathUtils.Round(handPos.Y, stepHeight * 2.0f)) + ladderSimPos.Y),
                5.2f);

            leftHand.body.ApplyTorque(Dir * 2.0f);
            rightHand.body.ApplyTorque(Dir * 2.0f);

            Vector2 footPos = new Vector2(
                handPos.X - Dir * 0.05f,
                Collider.SimPosition.Y + 0.9f - colliderHeightFromFloor - stepHeight * 2.7f - ladderSimPos.Y - 0.7f);

            //if (movement.Y < 0) footPos.Y += 0.05f;

            MoveLimb(leftFoot,
                new Vector2(footPos.X,
                (slide ? footPos.Y : MathUtils.Round(footPos.Y + stepHeight, stepHeight * 2.0f) - stepHeight) + ladderSimPos.Y),
                15.5f, true);

            MoveLimb(rightFoot,
                new Vector2(footPos.X,
                (slide ? footPos.Y : MathUtils.Round(footPos.Y, stepHeight * 2.0f)) + ladderSimPos.Y),
                15.5f, true);

            //apply torque to the legs to make the knees bend
            Limb leftLeg = GetLimb(LimbType.LeftLeg);
            Limb rightLeg = GetLimb(LimbType.RightLeg);

            leftLeg.body.ApplyTorque(Dir * -8.0f);
            rightLeg.body.ApplyTorque(Dir * -8.0f);

            float movementFactor = (handPos.Y / stepHeight) * (float)Math.PI;
            movementFactor = 0.8f + (float)Math.Abs(Math.Sin(movementFactor));

            Vector2 subSpeed = currentHull != null || character.SelectedConstruction.Submarine == null
                ? Vector2.Zero : character.SelectedConstruction.Submarine.Velocity;

            Vector2 climbForce = new Vector2(0.0f, movement.Y + 0.3f) * movementFactor;
            //if (climbForce.Y > 0.5f) climbForce.Y = Math.Max(climbForce.Y, 1.3f);

            //apply forces to the collider to move the Character up/down
            Collider.ApplyForce((climbForce * 20.0f + subSpeed * 50.0f) * Collider.Mass);
            head.body.SmoothRotate(0.0f);
            
            if (!character.SelectedConstruction.Prefab.Triggers.Any())
            {
                character.SelectedConstruction = null;
                return;
            }

            Rectangle trigger = character.SelectedConstruction.Prefab.Triggers.FirstOrDefault();
            trigger = character.SelectedConstruction.TransformTrigger(trigger);

            bool notClimbing = false;
            if (character.IsRemotePlayer && GameMain.Server == null)
            {
                notClimbing = character.IsKeyDown(InputType.Left) || character.IsKeyDown(InputType.Right);
            }
            else
            {
                notClimbing = Math.Abs(targetMovement.X) > 0.05f ||
                (TargetMovement.Y < 0.0f && ConvertUnits.ToSimUnits(trigger.Height) + handPos.Y < HeadPosition) ||
                (TargetMovement.Y > 0.0f && handPos.Y > 0.1f);
            }

            if (notClimbing)
            {
                Anim = Animation.None;
                character.SelectedConstruction = null;
                IgnorePlatforms = false;
            }
        }

        private void UpdateCPR(float deltaTime)
        {
            if (character.SelectedCharacter == null)
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

            if (GameMain.Client == null) //Serverside code
            {
                if (target.Bleeding <= 0.5f && target.Oxygen <= 0.0f) //If they're bleeding too hard CPR will hurt them
                {
                    target.Oxygen += deltaTime * 0.5f; //Stabilize them
                }
            }

            int skill = character.GetSkillLevel("Medical");
            if (cprAnimState % 17 > 15.0f && targetHead != null && head != null)
            {
                float yPos = (float)Math.Sin(cprAnimState) * 0.2f;
                head.PullJointWorldAnchorB = new Vector2(targetHead.SimPosition.X, targetHead.SimPosition.Y + 0.3f + yPos);
                head.PullJointEnabled = true;
                torso.PullJointWorldAnchorB = new Vector2(torso.SimPosition.X, colliderPos.Y + (TorsoPosition - 0.2f));
                torso.PullJointEnabled = true;

                if (GameMain.Client == null) //Serverside code
                {
                    float cpr = skill / 2.0f; //Max possible oxygen addition is 20 per second
                    character.Oxygen -= (30.0f - cpr) * deltaTime; //Worse skill = more oxygen required
                    if (character.Oxygen > 0.0f) //we didn't suffocate yet did we
                        target.Oxygen += cpr * deltaTime;

                    //DebugConsole.NewMessage("CPR Us: " + character.Oxygen + " Them: " + target.Oxygen + " How good we are: restore " + cpr + " use " + (30.0f - cpr), Color.Aqua);
                }
            }
            else
            {
                if (targetHead != null && head != null)
                {
                    head.PullJointWorldAnchorB = new Vector2(targetHead.SimPosition.X, targetHead.SimPosition.Y + 0.8f);
                    head.PullJointEnabled = true;
                }
                torso.PullJointWorldAnchorB = new Vector2(torso.SimPosition.X, colliderPos.Y + (TorsoPosition - 0.1f));
                torso.PullJointEnabled = true;
                if (cprPump >= 1)
                {
                    torso.body.ApplyForce(new Vector2(0, -1000f));
                    targetTorso.body.ApplyForce(new Vector2(0, -1000f));
                    cprPump = 0;

                    if (target.Bleeding <= 0.5f && target.Health <= 0.0f && !target.IsDead) //Have a chance to revive them to 2 HP if they were damaged.
                    {
                        if (GameMain.Client == null) //Serverside code
                        {
                            float reviveChance = (cprAnimState % 17) * (skill / 50.0f); //~5% max chance for 10 skill, ~50% max chance for 100 skill
                            float rng = Rand.Int(100, Rand.RandSync.Server);

                            //DebugConsole.NewMessage("CPR Pump cprAnimState: " + (cprAnimState % 17) + " revive chance: " + reviveChance + " rng: " + rng, Color.Aqua);
                            if (rng <= reviveChance) //HOLY CRAP YOU SAVED HIM!!!
                            {
                                target.Oxygen = Math.Max(target.Oxygen, 10.0f);
                                target.Health = 2.0f;
                                Anim = Animation.None;
                                return;
                            }
                        }
                    }
                    else if (target.Bleeding > 0.5f || skill < 50) //We will hurt them if they're bleeding or we suck
                    {
                        //If not bleeding: 10% skill causes 0.8 damage per pump, 40% skill causes only 0.2
                        if (target.Bleeding <= 0.5f)
                            target.AddDamage(CauseOfDeath.Damage, (50 - skill) * 0.02f, character);
                        else //If bleeding: 2 HP damage per pump. Basically speeds up their death. Don't pump bleeding people!
                        {
                            target.AddDamage(CauseOfDeath.Bloodloss, 1.0f, character);
#if CLIENT
                            SoundPlayer.PlayDamageSound("LimbBlunt", 25.0f, targetTorso.body);

                            for (int i = 0; i < 4; i++)
                            {
                                var blood = GameMain.ParticleManager.CreateParticle(inWater ? "waterblood" : "blood", targetTorso.WorldPosition, Rand.Vector(10.0f), 0.0f, target.AnimController.CurrentHull);
                            }
#endif
                        }
                    }
                }
                cprPump += deltaTime;
            }

            cprAnimState += deltaTime;
        }

        public override void DragCharacter(Character target)
        {
            if (target == null) return;

            Limb torso = GetLimb(LimbType.Torso);

            Limb targetLeftHand = target.AnimController.GetLimb(LimbType.LeftHand);
            Limb targetRightHand = target.AnimController.GetLimb(LimbType.RightHand);

            target.AnimController.ResetPullJoints();

            if (Anim == Animation.Climbing)
            {
                Limb targetTorso = target.AnimController.GetLimb(LimbType.Torso);
                if (targetTorso == null) targetTorso = target.AnimController.MainLimb;

                if (target.AnimController.Dir != Dir)
                    target.AnimController.Flip();

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

                target.AnimController.IgnorePlatforms = true;
            }
            else
            {
                Limb leftHand = GetLimb(LimbType.LeftHand);
                Limb rightHand = GetLimb(LimbType.RightHand);

                //only grab with one hand when swimming
                leftHand.Disabled = true;
                if (!inWater) rightHand.Disabled = true;

                for (int i = 0; i < 2; i++)
                {
                    Limb targetLimb = target.AnimController.GetLimb(GrabLimb);

                    //grab hands if GrabLimb is not specified (or torso if the character has no hands)
                    if (GrabLimb == LimbType.None || targetLimb.IsSevered)
                    {
                        targetLimb = target.AnimController.GetLimb(LimbType.Torso);
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
                    }

                    Limb pullLimb = i == 0 ? leftHand : rightHand;

                    if (GameMain.Client == null)
                    {
                        //stop dragging if there's something between the pull limb and the target limb
                        Vector2 sourceSimPos = pullLimb.SimPosition;
                        Vector2 targetSimPos = targetLimb.SimPosition;
                        if (character.Submarine != null && target.Submarine == null)
                        {
                            targetSimPos -= character.Submarine.SimPosition;
                        }
                        else if (character.Submarine == null && target.Submarine != null)
                        {
                            sourceSimPos -= target.Submarine.SimPosition;
                        }

                        var body = Submarine.CheckVisibility(sourceSimPos, targetSimPos, ignoreSubs: true);
                        if (body != null)
                        {
                            character.DeselectCharacter();
                            return;
                        }
                    }

                    //only pull with one hand when swimming
                    if (i < 1 || !inWater)
                    {
                        Vector2 diff = ConvertUnits.ToSimUnits(targetLimb.WorldPosition - pullLimb.WorldPosition);

                        pullLimb.PullJointEnabled = true;
                        targetLimb.PullJointEnabled = true;
                        if (targetLimb.type == LimbType.Torso || targetLimb == target.AnimController.MainLimb)
                        {
                            pullLimb.PullJointWorldAnchorB = targetLimb.SimPosition;
                            pullLimb.PullJointMaxForce = 5000.0f;
                            targetMovement *= MathHelper.Clamp(Mass / target.Mass, 0.5f, 1.0f);

                            //hand length
                            float a = 37.0f;
                            //arm length
                            float b = 28.0f;

                            Vector2 shoulderPos = LimbJoints[2].WorldAnchorA;
                            Vector2 dragDir = inWater ? Vector2.Normalize(targetLimb.SimPosition - shoulderPos) : Vector2.UnitY;
                            targetLimb.PullJointWorldAnchorB = shoulderPos - dragDir * ConvertUnits.ToSimUnits(a + b);
                            targetLimb.PullJointMaxForce = 200.0f;

                            if (target.Submarine != character.Submarine)
                            {
                                if (character.Submarine == null)
                                {
                                    pullLimb.PullJointWorldAnchorB += target.Submarine.SimPosition;
                                    targetLimb.PullJointWorldAnchorB -= target.Submarine.SimPosition;
                                }
                                else if (target.Submarine == null)
                                {
                                    pullLimb.PullJointWorldAnchorB -= character.Submarine.SimPosition;
                                    targetLimb.PullJointWorldAnchorB += character.Submarine.SimPosition;
                                }
                                else
                                {
                                    pullLimb.PullJointWorldAnchorB -= target.Submarine.SimPosition;
                                    pullLimb.PullJointWorldAnchorB += character.Submarine.SimPosition;
                                    targetLimb.PullJointWorldAnchorB -= character.Submarine.SimPosition;
                                    targetLimb.PullJointWorldAnchorB += target.Submarine.SimPosition;
                                }
                            }
                        }
                        else
                        {
                            pullLimb.PullJointWorldAnchorB = pullLimb.SimPosition + diff;
                            pullLimb.PullJointMaxForce = 5000.0f;

                            targetLimb.PullJointWorldAnchorB = targetLimb.SimPosition - diff;
                            targetLimb.PullJointMaxForce = 5000.0f;
                        }

                        target.AnimController.movement = -diff;
                    }
                }

                float dist = Vector2.Distance(target.SimPosition, Collider.SimPosition);

                //limit movement if moving away from the target
                if (Vector2.Dot(target.SimPosition - Collider.SimPosition, targetMovement) < 0)
                {
                    targetMovement *= MathHelper.Clamp(2.0f - dist, 0.0f, 1.0f);
                }

                target.AnimController.IgnorePlatforms = IgnorePlatforms;

                if (!target.AllowInput)
                {
                    target.AnimController.TargetMovement = TargetMovement;
                }
                else if (target is AICharacter)
                {
                    target.AnimController.TargetMovement = Vector2.Lerp(
                        target.AnimController.TargetMovement,
                        (character.SimPosition + Vector2.UnitX * Dir) - target.SimPosition, 0.5f);
                }
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

        public override void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle)
        {
            if (character.IsUnconscious || character.Stun > 0.0f) aim = false;

            //calculate the handle positions
            Matrix itemTransfrom = Matrix.CreateRotationZ(item.body.Rotation);
            Vector2[] transformedHandlePos = new Vector2[2];
            transformedHandlePos[0] = Vector2.Transform(handlePos[0], itemTransfrom);
            transformedHandlePos[1] = Vector2.Transform(handlePos[1], itemTransfrom);

            Limb head = GetLimb(LimbType.Head);
            Limb torso = GetLimb(LimbType.Torso);
            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Vector2 itemPos = aim ? aimPos : holdPos;

            bool usingController = character.SelectedConstruction != null && character.SelectedConstruction.GetComponent<Controller>() != null;

            float itemAngle;
            if (Anim != Animation.Climbing && !usingController && character.Stun <= 0.0f && aim && itemPos != Vector2.Zero)
            {
                Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);

                Vector2 diff = (mousePos - AimSourceSimPos) * Dir;

                holdAngle = MathUtils.VectorToAngle(new Vector2(diff.X, diff.Y * Dir)) - torso.body.Rotation * Dir;

                itemAngle = (torso.body.Rotation + holdAngle * Dir);

                Holdable holdable = item.GetComponent<Holdable>();
                if (holdable.ControlPose)
                {
                    head.body.SmoothRotate(itemAngle);

                    if (TargetMovement == Vector2.Zero && inWater)
                    {
                        torso.body.AngularVelocity -= torso.body.AngularVelocity * 0.1f;
                        torso.body.ApplyForce(torso.body.LinearVelocity * -0.5f);
                    }

                    aiming = true;
                }

            }
            else
            {
                itemAngle = (torso.body.Rotation + holdAngle * Dir);
            }

            Vector2 shoulderPos = LimbJoints[2].WorldAnchorA;
            Vector2 transformedHoldPos = shoulderPos;

            if (itemPos == Vector2.Zero || Anim == Animation.Climbing || usingController)
            {
                if (character.SelectedItems[0] == item)
                {
                    if (rightHand.IsSevered) return;
                    transformedHoldPos = rightHand.PullJointWorldAnchorA - transformedHandlePos[0];
                    itemAngle = (rightHand.Rotation + (holdAngle - MathHelper.PiOver2) * Dir);
                }
                else if (character.SelectedItems[1] == item)
                {
                    if (leftHand.IsSevered) return;
                    transformedHoldPos = leftHand.PullJointWorldAnchorA - transformedHandlePos[1];
                    itemAngle = (leftHand.Rotation + (holdAngle - MathHelper.PiOver2) * Dir);
                }
            }
            else
            {
                if (character.SelectedItems[0] == item)
                {
                    if (rightHand.IsSevered) return;
                    rightHand.Disabled = true;
                }
                if (character.SelectedItems[1] == item)
                {
                    if (leftHand.IsSevered) return;
                    leftHand.Disabled = true;
                }

                itemPos.X = itemPos.X * Dir;                
                transformedHoldPos += Vector2.Transform(itemPos, Matrix.CreateRotationZ(itemAngle));
            }

            item.body.ResetDynamics();

            Vector2 currItemPos = (character.SelectedItems[0] == item) ?
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
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error, 
                    errorMsg);

                return;
            }

            item.SetTransform(currItemPos, itemAngle);

            //item.SetTransform(MathUtils.SmoothStep(item.body.SimPosition, transformedHoldPos + bodyVelocity, 0.5f), itemAngle);

            if (Anim == Animation.Climbing) return;

            for (int i = 0; i < 2; i++)
            {
                if (character.SelectedItems[i] != item) continue;
                if (itemPos == Vector2.Zero) continue;

                Limb hand = (i == 0) ? rightHand : leftHand;

                HandIK(hand, transformedHoldPos + transformedHandlePos[i]);
            }
        }

        private void HandIK(Limb hand, Vector2 pos, float force = 1.0f)
        {
            Vector2 shoulderPos = LimbJoints[2].WorldAnchorA;

            Limb arm = (hand.type == LimbType.LeftHand) ? GetLimb(LimbType.LeftArm) : GetLimb(LimbType.RightArm);

            //hand length
            float a = 37.0f;

            //arm length
            float b = 28.0f;

            //distance from shoulder to holdpos
            float c = ConvertUnits.ToDisplayUnits(Vector2.Distance(pos, shoulderPos));
            c = MathHelper.Clamp(a + b - 1, b - a, c);

            float ang2 = MathUtils.VectorToAngle(pos - shoulderPos) + MathHelper.PiOver2;

            float armAngle = MathUtils.SolveTriangleSSS(a, b, c);
            float handAngle = MathUtils.SolveTriangleSSS(b, a, c);

            arm.body.SmoothRotate((ang2 - armAngle * Dir), 20.0f * force);
            hand.body.SmoothRotate((ang2 + handAngle * Dir), 100.0f * force);
        }

        public override void UpdateUseItem(bool allowMovement, Vector2 handPos)
        {
            var leftHand = GetLimb(LimbType.LeftHand);
            var rightHand = GetLimb(LimbType.RightHand);

            useItemTimer = 0.5f;
            Anim = Animation.UsingConstruction;

            if (!allowMovement)
            {
                TargetMovement = Vector2.Zero;
                TargetDir = handPos.X > character.SimPosition.X ? Direction.Right : Direction.Left;
                if (Vector2.Distance(character.SimPosition, handPos) > 1.0f)
                {
                    TargetMovement = Vector2.Normalize(handPos - character.SimPosition);
                }
            }

            leftHand.Disabled = true;
            leftHand.PullJointEnabled = true;
            leftHand.PullJointWorldAnchorB = handPos;

            rightHand.Disabled = true;
            rightHand.PullJointEnabled = true;
            rightHand.PullJointWorldAnchorB = handPos;
        }

        public override void Flip()
        {
            base.Flip();

            walkPos = -walkPos;

            Limb torso = GetLimb(LimbType.Torso);

            Vector2 difference;

            Matrix torsoTransform = Matrix.CreateRotationZ(torso.Rotation);

            for (int i = 0; i < character.SelectedItems.Length; i++)
            {
                if (character.SelectedItems[i] != null && character.SelectedItems[i].body != null)
                {
                    difference = character.SelectedItems[i].body.SimPosition - torso.SimPosition;
                    difference = Vector2.Transform(difference, torsoTransform);
                    difference.Y = -difference.Y;

                    character.SelectedItems[i].body.SetTransform(
                        torso.SimPosition + Vector2.Transform(difference, -torsoTransform),
                        MathUtils.WrapAngleTwoPi(-character.SelectedItems[i].body.Rotation));
                }
            }

            foreach (Limb limb in Limbs)
            {
                bool mirror = false;
                bool flipAngle = false;
                bool wrapAngle = false;

                switch (limb.type)
                {
                    case LimbType.LeftHand:
                    case LimbType.LeftArm:
                    case LimbType.RightHand:
                    case LimbType.RightArm:
                        mirror = true;
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
                if (wrapAngle) angle = MathUtils.WrapAnglePi(angle);
                
                TrySetLimbPosition(limb, Collider.SimPosition, position);

                limb.body.SetTransform(limb.body.SimPosition, angle);
            }
        }

    }
}
