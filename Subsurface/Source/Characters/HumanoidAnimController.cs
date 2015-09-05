using System;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Subsurface.Items.Components;

namespace Subsurface
{
    class HumanoidAnimController : AnimController
    {
        private bool aiming;

        public HumanoidAnimController(Character character, XElement element)
            : base(character, element)
        {
        }

        public override void UpdateAnim(float deltaTime)
        {
            if (character.IsDead)
            {
                UpdateStruggling();
                return;
            }

            Vector2 colliderPos = GetLimb(LimbType.Torso).SimPosition;

            if (inWater) stairs = null;

            Vector2 rayStart = colliderPos; // at the bottom of the player sprite
            Vector2 rayEnd = rayStart - new Vector2(0.0f, TorsoPosition);
            if (stairs != null) rayEnd.Y -= 0.5f;
            if (Anim != Animation.UsingConstruction) ResetPullJoints();

            //do a raytrace straight down from the torso to figure 
            //out whether the  ragdoll is standing on ground
            float closestFraction = 1;
            Structure closestStructure = null;
            Game1.World.RayCast((fixture, point, normal, fraction) =>
            {
                switch (fixture.CollisionCategories)
                {
                    case Physics.CollisionStairs:
                        if (inWater) return -1;
                        Structure structure = fixture.Body.UserData as Structure;
                        if (stairs == null && structure!=null)
                        {
                            if (LowestLimb.SimPosition.Y < structure.SimPosition.Y)
                            {
                                return -1;
                            }
                            else
                            {
                                stairs = structure;
                            }
                        }
                        break;
                    case Physics.CollisionPlatform:
                         Structure platform = fixture.Body.UserData as Structure;
                        if (IgnorePlatforms || LowestLimb.Position.Y < platform.Rect.Y) return -1;
                        break;
                    case Physics.CollisionWall:
                        break;
                    default:
                        return -1;
                }

                onGround = true;
                if (fraction < closestFraction)
                {
                    closestFraction = fraction;

                    Structure structure = fixture.Body.UserData as Structure;
                    if (structure != null) closestStructure = structure;
                }
                onFloorTimer = 0.05f;
                return closestFraction;
            }
            , rayStart, rayEnd);

            if (closestStructure != null && closestStructure.StairDirection != Direction.None)
            {
                stairs = closestStructure;
            }
            else
            {
                stairs = null;
            }

            //the ragdoll "stays on ground" for 50 millisecs after separation
            if (onFloorTimer <= 0.0f)
            {
                onGround = false;
                if (GetLimb(LimbType.Torso).inWater) inWater = true;
                //TODO: joku järkevämpi systeemi
                //if (!inWater && lastTimeOnFloor + 200 < gameTime.TotalGameTime.Milliseconds)
                //    stunTimer = Math.Max(stunTimer, (float)gameTime.TotalGameTime.TotalMilliseconds + 100.0f);
            }
            else
            {
                onFloorTimer -= deltaTime;
            }

            if (closestFraction == 1) //raycast didn't hit anything
            {
                floorY = (currentHull == null) ? -1000.0f : ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height);
            }                
            else
            {
                floorY = rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;
            }
                          

            IgnorePlatforms = (TargetMovement.Y < 0.0f);

            //stun (= disable the animations) if the ragdoll receives a large enough impact
            if (strongestImpact > 0.0f)
            {
                character.Stun();
                stunTimer = MathHelper.Clamp(strongestImpact * 0.5f, stunTimer, 5.0f);
            }
            strongestImpact = 0.0f;

            if (stunTimer > 0)
            {
                UpdateStruggling();
                stunTimer -= deltaTime;
                return;
            }

            switch (Anim)
            {
                case Animation.Climbing:
                    UpdateClimbing();
                    break;
                case Animation.UsingConstruction:
                    break;
                default:
                    if (inWater)
                        UpdateSwimming();
                    else if (IsStanding)
                        UpdateStanding();

                    break;
            }

            if (TargetDir != dir) Flip();

            foreach (Limb limb in limbs)
            {
                limb.Disabled = false;
            }

            aiming = false;
        }

        void UpdateStanding()
        {
            Vector2 handPos;

            Limb leftFoot   = GetLimb(LimbType.LeftFoot);
            Limb rightFoot  = GetLimb(LimbType.RightFoot);
            Limb head       = GetLimb(LimbType.Head);
            Limb torso      = GetLimb(LimbType.Torso);

            Limb leftHand   = GetLimb(LimbType.LeftHand);
            Limb rightHand  = GetLimb(LimbType.RightHand);

            Limb leftLeg    = GetLimb(LimbType.LeftLeg);
            Limb rightLeg   = GetLimb(LimbType.RightLeg);

            float getUpSpeed = 0.3f;
            float walkCycleSpeed = head.LinearVelocity.X * 0.08f;
            if (stairs != null)
            {
                TargetMovement = new Vector2(MathHelper.Clamp(TargetMovement.X, -2.0f, 2.0f), TargetMovement.Y) ;

                if ((TargetMovement.X>0.0f && stairs.StairDirection == Direction.Right) ||
                    TargetMovement.X < 0.0f && stairs.StairDirection == Direction.Left)
                {
                    TargetMovement *= 1.35f;
                }
                else                    
                {

                    TargetMovement /= 1.2f;
                }

                walkCycleSpeed *= 1.5f;
            }

            Vector2 colliderPos = new Vector2(torso.SimPosition.X, floorY);

            float walkPosX = (float)Math.Cos(walkPos);
            float walkPosY = (float)Math.Sin(walkPos);
            float runningModifier = (float)Math.Max(Math.Abs(movement.X) / 1.5f, 1.0);

            Vector2 stepSize = new Vector2(
                this.stepSize.X * walkPosX * runningModifier,
                this.stepSize.Y * walkPosY * runningModifier * runningModifier);

            float footMid = (leftFoot.SimPosition.X + rightFoot.SimPosition.X) / 2.0f;

            movement = MathUtils.SmoothStep(movement, TargetMovement, 0.5f);
            movement.Y = 0.0f;

            //place the anchors of the head and the torso to make the ragdoll stand
            if (onGround && LowestLimb != null && (LowestLimb.SimPosition.Y-floorY < 0.5f || stairs != null) && head !=null)
            {
                getUpSpeed = Math.Max(getUpSpeed * (head.SimPosition.Y - colliderPos.Y), 0.25f);

                if (stairs != null)
                {
                    if (LowestLimb.SimPosition.Y < stairs.SimPosition.Y) IgnorePlatforms = true;
                    
                    torso.pullJoint.Enabled = true;
                    torso.pullJoint.WorldAnchorB = new Vector2(
                        MathHelper.SmoothStep(torso.SimPosition.X, footMid + movement.X * 0.35f, getUpSpeed * 0.8f),
                        MathHelper.SmoothStep(torso.SimPosition.Y, colliderPos.Y + TorsoPosition - Math.Abs(walkPosX * 0.05f), getUpSpeed * 3.0f));


                    head.pullJoint.Enabled = true;
                    head.pullJoint.WorldAnchorB = new Vector2(
                        MathHelper.SmoothStep(head.SimPosition.X, footMid + movement.X * 0.4f, getUpSpeed * 0.8f),
                        MathHelper.SmoothStep(head.SimPosition.Y, colliderPos.Y + HeadPosition - Math.Abs(walkPosX * 0.05f), getUpSpeed * 3.0f));
                }
                else
                {
                    torso.pullJoint.Enabled = true;
                    torso.pullJoint.WorldAnchorB =
                        MathUtils.SmoothStep(torso.SimPosition,
                        new Vector2(footMid + movement.X * 0.35f, colliderPos.Y + TorsoPosition), getUpSpeed);

                    head.pullJoint.Enabled = true;
                    head.pullJoint.WorldAnchorB =
                        MathUtils.SmoothStep(head.SimPosition,
                        new Vector2(footMid + movement.X * 0.4f, colliderPos.Y + HeadPosition), getUpSpeed);
                }


                //moving horizontally
                if (TargetMovement.X != 0.0f)
                {
                    //progress the walking animation
                    walkPos -= (walkCycleSpeed / runningModifier)*0.8f;

                    MoveLimb(leftFoot,
                        colliderPos + new Vector2(
                            stepSize.X,
                            (stepSize.Y > 0.0f) ? stepSize.Y : -0.15f),
                        15.0f, true);

                    MoveLimb(rightFoot,
                        colliderPos + new Vector2(
                            -stepSize.X,
                            (-stepSize.Y > 0.0f) ? -stepSize.Y : -0.15f),
                        15.0f, true);

                    if (Math.Sign(stepSize.X) == Math.Sign(Dir))
                    {
                        leftFoot.body.SmoothRotate(leftLeg.body.Rotation + MathHelper.PiOver2 * Dir * 1.6f, 20.0f * runningModifier);
                    }
                    else if (Math.Sign(-stepSize.X) == Math.Sign(Dir))
                    {
                        rightFoot.body.SmoothRotate(rightLeg.body.Rotation + MathHelper.PiOver2 * Dir * 1.6f, 20 * runningModifier);
                    }

                    if (walkPosY > 0.0f)
                    {
                        GetLimb(LimbType.LeftThigh).body.ApplyTorque(-walkPosY * Dir * Math.Abs(movement.X) * -5.0f);
                    }
                    else
                    {
                        GetLimb(LimbType.RightThigh).body.ApplyTorque(walkPosY * Dir * Math.Abs(movement.X) * -5.0f);
                    }

                    //calculate the positions of hands
                    handPos = torso.SimPosition;
                    handPos.X = -walkPosX * 0.1f * runningModifier;

                    float lowerY = -0.6f + runningModifier/3.5f;
                    
                    handPos.Y = lowerY + (float)(Math.Abs(Math.Sin(walkPos - Math.PI * 1.5f) * 0.1)) / runningModifier;

                    Vector2 posAdditon = new Vector2(movement.X*0.07f, 0.0f);
                    if (stairs!=null)
                    {
                        if ((stairs.StairDirection == Direction.Right && movement.X < 0.0f) ||
                        (stairs.StairDirection == Direction.Left && movement.X > 0.0f))
                        {
                            posAdditon.Y -= 0.1f;
                        }
                        else
                        {
                           posAdditon.Y += 0.1f;
                        }
                    }


                    if (!rightHand.Disabled)
                    {
                        rightHand.body.ApplyTorque(walkPosY * runningModifier * Dir);
                        MoveLimb(rightHand, torso.SimPosition + posAdditon +
                            new Vector2(
                                -handPos.X,
                                (Math.Sign(walkPosX) == Math.Sign(Dir)) ? handPos.Y : lowerY),
                            15.0f, true);
                    }

                    if (!leftHand.Disabled)
                    {
                        leftHand.body.ApplyTorque(-walkPosY * runningModifier * Dir);
                        MoveLimb(leftHand, torso.SimPosition + posAdditon +
                            new Vector2(
                                handPos.X,
                                (Math.Sign(walkPosX) == Math.Sign(-Dir)) ? handPos.Y : lowerY),
                            15.0f, true);
                    }

                }
                else
                {
                    //add torque to the head to do a subtle "breathing" effect
                    //head.body.ApplyTorque((float)Math.Sin(gameTime.TotalGameTime.TotalMilliseconds / 300) * 0.2f);

                    //standing still -> "attach" the feet to the ground

                    float movementFactor = (movement.X / 4.0f) * movement.X * Math.Sign(movement.X);

                    Vector2 footPos = new Vector2(
                        colliderPos.X + movementFactor - Dir * 0.05f,
                        colliderPos.Y - 0.2f - Math.Abs(movementFactor));

                    MoveLimb(leftFoot, footPos, 2.5f);
                    MoveLimb(rightFoot, footPos, 2.5f);

                    leftFoot.body.SmoothRotate(Dir * MathHelper.PiOver2, 5.0f);
                    rightFoot.body.SmoothRotate(Dir * MathHelper.PiOver2, 5.0f);

                    
                    

                    //handPos = torso.SimPosition;
                    //handPos.X += movement.X;
                    //handPos.Y -= 0.4f;
                    if (!rightHand.Disabled)
                    {
                        // MoveLimb(rightHand, handPos, 0.05f, true);
                        //rightHand.body.ApplyLinearImpulse((handPos - rightHand.Position));
                        rightHand.body.SmoothRotate(0.0f, 5.0f);

                        var rightArm = GetLimb(LimbType.RightArm);
                        rightArm.body.SmoothRotate(0.0f, 20.0f);
                    }

                    if (!leftHand.Disabled)
                    {
                        //MoveLimb(leftHand, handPos, 0.05f, true);
                        //leftHand.body.ApplyLinearImpulse((handPos - leftHand.Position));
                        leftHand.body.SmoothRotate(0.0f, 5.0f);

                        var leftArm = GetLimb(LimbType.LeftArm);
                        leftArm.body.SmoothRotate(0.0f, 20.0f);
                    }
                }
            }

            for (int i = 0; i < 2; i++)
            {
                Limb leg = (i == 0) ? rightFoot : leftFoot;

                if (leg.SimPosition.Y < torso.SimPosition.Y) continue;

                leg.body.ApplyTorque(-Dir * leg.Mass * 20.0f);
            }

        }

        void UpdateSwimming()
        {
            IgnorePlatforms = true;

            Vector2 footPos, handPos;

            float surfaceLimiter = 1.0f;

            Limb head = GetLimb(LimbType.Head);

            if (currentHull != null && (currentHull.Rect.Y - currentHull.Surface > 50.0f) && !head.inWater)
            {
                surfaceLimiter = (ConvertUnits.ToDisplayUnits(head.SimPosition.Y)-surfaceY);
                surfaceLimiter = Math.Max(1.0f, surfaceLimiter);
                if (surfaceLimiter > 20.0f) return;
            }

            Limb torso      = GetLimb(LimbType.Torso);
            Limb leftHand   = GetLimb(LimbType.LeftHand);
            Limb rightHand  = GetLimb(LimbType.RightHand);

            Limb leftFoot   = GetLimb(LimbType.LeftFoot);
            Limb rightFoot  = GetLimb(LimbType.RightFoot);
            Limb leftLeg    = GetLimb(LimbType.LeftLeg);
            Limb rightLeg   = GetLimb(LimbType.RightLeg);
            
            float rotation = MathHelper.WrapAngle(torso.Rotation);
            rotation = MathHelper.ToDegrees(rotation);
            if (rotation < 0.0f) rotation += 360;

            if (!character.IsNetworkPlayer && !aiming)
            {
                if (rotation > 20 && rotation < 170)
                    TargetDir = Direction.Left;
                else if (rotation > 190 && rotation < 340)
                    TargetDir = Direction.Right;
            }

            if (TargetMovement == Vector2.Zero) return;

            float targetSpeed = TargetMovement.Length();
            if (targetSpeed > 0.0f) TargetMovement /= targetSpeed;

            //if trying to head to the opposite direction, apply torque
            //to the torso to flip the ragdoll around
            //if (Math.Sign(TargetMovement.X) != Dir && TargetMovement.X != 0.0f)
            //{
            //    float torque = torso.Mass * 10.0f;
            //    torque *= (rotation > 90 && rotation < 270) ? -Dir : Dir;

            //    torso.body.ApplyTorque(torque);
            //}

            if (targetSpeed > 0.1f && !aiming)
            {
                torso.body.SmoothRotate(MathUtils.VectorToAngle(TargetMovement)-MathHelper.PiOver2);
            }

            movement = MathUtils.SmoothStep(movement, TargetMovement, 0.3f);

            //dont try to move upwards if head is already out of water
            if (surfaceLimiter > 1.0f && TargetMovement.Y > 0.0f)
            {
                if (TargetMovement.X == 0.0f)
                {
                    head.body.ApplyForce(head.Mass * new Vector2(-Dir * 5.1f, -5.0f));
                    torso.body.ApplyForce(torso.Mass * new Vector2(-Dir * 5.1f, -15.0f));
                    leftFoot.body.ApplyForce(leftFoot.Mass * new Vector2(0.0f, -80.0f));
                    rightFoot.body.ApplyForce(rightFoot.Mass * new Vector2(0.0f, -80.0f));
                }
                else
                {
                    TargetMovement = new Vector2(
                        (float)Math.Sqrt(targetSpeed * targetSpeed - TargetMovement.Y * TargetMovement.Y)
                        * Math.Sign(TargetMovement.X),
                        Math.Max(TargetMovement.Y, TargetMovement.Y * 0.2f));

                    head.body.ApplyTorque(Dir * 0.1f);
                }

                movement.Y = movement.Y - (surfaceLimiter - 1.0f) * 0.01f;
            }

            head.body.ApplyForce((new Vector2(movement.X,
                    movement.Y / surfaceLimiter + 0.2f) - head.body.LinearVelocity * 0.2f) * 
                    20.0f * head.body.Mass);

            torso.body.ApplyForce((new Vector2(movement.X,
                    movement.Y / surfaceLimiter + 0.2f) - torso.body.LinearVelocity * 0.2f) * 10.0f * torso.body.Mass);

            walkPos += movement.Length() * 0.15f;
            footPos = (leftFoot.SimPosition + rightFoot.SimPosition) / 2.0f;

            Vector2 transformedFootPos = new Vector2((float)Math.Sin(walkPos) * 0.3f, 0.0f);
            transformedFootPos = Vector2.Transform(
                transformedFootPos,
                Matrix.CreateRotationZ(torso.body.Rotation));

            MoveLimb(leftFoot, footPos + transformedFootPos, 2.5f);
            MoveLimb(rightFoot, footPos - transformedFootPos, 2.5f);

            //float legCorrection = MathUtils.GetShortestAngle(leftLeg.Rotation, torso.body.Rotation);

            //leftLeg.body.ApplyTorque(legCorrection);

            //legCorrection = MathUtils.GetShortestAngle(rightLeg.Rotation, torso.body.Rotation);

            //rightLeg.body.ApplyTorque(legCorrection);
            Vector2 feetExtendForce = new Vector2(
                (float)-Math.Sin(torso.body.Rotation),
                (float)Math.Cos(torso.body.Rotation));

            leftFoot.body.ApplyForce(feetExtendForce);
            rightFoot.body.ApplyForce(feetExtendForce);
            
            leftFoot.body.ApplyTorque(leftFoot.body.Mass * -Dir);
            rightFoot.body.ApplyTorque(rightFoot.body.Mass * -Dir);
            
            handPos = (torso.SimPosition + head.SimPosition) / 2.0f;

            //if (!rightHand.Disabled) rightHand.body.ApplyTorque(leftHand.body.Mass * Dir);
            //if (!leftHand.Disabled) leftHand.body.ApplyTorque(leftHand.body.Mass * Dir);
            
            //at the surface, not moving sideways -> hands just float around
            if (!headInWater && TargetMovement.X == 0.0f && TargetMovement.Y>0)
            {
                handPos.X = handPos.X + Dir * 0.6f;

                float wobbleAmount = 0.05f;

                if (!rightHand.Disabled)
                {
                    MoveLimb(rightHand, new Vector2(
                        handPos.X + (float)Math.Sin(walkPos / 1.5f) * wobbleAmount,
                        handPos.Y + (float)Math.Sin(walkPos / 3.5f) * wobbleAmount - 0.0f), 1.5f);
                }

                if (!leftHand.Disabled)
                {
                    MoveLimb(leftHand, new Vector2(
                        handPos.X + (float)Math.Sin(walkPos / 2.0f) * wobbleAmount,
                        handPos.Y + (float)Math.Sin(walkPos / 3.0f) * wobbleAmount - 0.0f), 1.5f);
                }

                return;
            }

            handPos += head.LinearVelocity * 0.1f;

            float handCyclePos = walkPos / 2.0f;
            float handPosX = (float)Math.Cos(handCyclePos * Dir) * 0.4f;
            float handPosY = (float)Math.Sin(handCyclePos * Dir) * 0.7f;
            handPosY = MathHelper.Clamp(handPosY, -0.6f, 0.6f);

            Matrix rotationMatrix = Matrix.CreateRotationZ(torso.Rotation);

            if (!rightHand.Disabled)
            {
                Vector2 rightHandPos = new Vector2(-handPosX, -handPosY);
                rightHandPos.X = (Dir == 1.0f) ? Math.Max(0.2f, rightHandPos.X) : Math.Min(-0.2f, rightHandPos.X);
                rightHandPos = Vector2.Transform(rightHandPos, rotationMatrix);

                MoveLimb(rightHand, handPos + rightHandPos, 3.5f);
            }

            if (!leftHand.Disabled)
            {
                Vector2 leftHandPos = new Vector2(handPosX, handPosY);
                leftHandPos.X = (Dir == 1.0f) ? Math.Max(0.2f, leftHandPos.X) : Math.Min(-0.2f, leftHandPos.X);
                leftHandPos = Vector2.Transform(leftHandPos, rotationMatrix);

                MoveLimb(leftHand, handPos + leftHandPos, 3.5f);
            }            
        }

        void UpdateClimbing()
        {
            if (character.SelectedConstruction == null || character.SelectedConstruction.GetComponent<Ladder>()==null)
            {
                Anim = Animation.None;
                return;
            }

            onGround = false;
            IgnorePlatforms = true;

            movement = MathUtils.SmoothStep(movement, TargetMovement, 0.3f);

            Vector2 footPos, handPos;

            Limb leftFoot   = GetLimb(LimbType.LeftFoot);
            Limb rightFoot  = GetLimb(LimbType.RightFoot);
            Limb head       = GetLimb(LimbType.Head);
            Limb torso      = GetLimb(LimbType.Torso);

            Limb waist      = GetLimb(LimbType.Waist);

            Limb leftHand   = GetLimb(LimbType.LeftHand);
            Limb rightHand  = GetLimb(LimbType.RightHand);

            Vector2 ladderSimPos = ConvertUnits.ToSimUnits(
                character.SelectedConstruction.Rect.X + character.SelectedConstruction.Rect.Width / 2.0f,
                character.SelectedConstruction.Rect.Y);

            MoveLimb(head, new Vector2(ladderSimPos.X - 0.27f * Dir, head.SimPosition.Y + 0.05f), 10.5f);
            MoveLimb(torso, new Vector2(ladderSimPos.X - 0.27f * Dir, torso.SimPosition.Y), 10.5f);
            MoveLimb(waist, new Vector2(ladderSimPos.X - 0.35f * Dir, waist.SimPosition.Y), 10.5f);

            float stepHeight = ConvertUnits.ToSimUnits(30.0f);

            handPos = new Vector2(
                ladderSimPos.X,
                head.SimPosition.Y + 0.0f + movement.Y * 0.1f - ladderSimPos.Y);

            MoveLimb(leftHand,
                new Vector2(handPos.X,
                MathUtils.Round(handPos.Y - stepHeight, stepHeight * 2.0f) + stepHeight + ladderSimPos.Y),
                5.2f);

            MoveLimb(rightHand,
                new Vector2(handPos.X,
                MathUtils.Round(handPos.Y, stepHeight * 2.0f) + ladderSimPos.Y),
                5.2f);

            leftHand.body.ApplyTorque(Dir * 2.0f);
            rightHand.body.ApplyTorque(Dir * 2.0f);

            footPos = new Vector2(
                handPos.X - Dir*0.05f,
                head.SimPosition.Y - stepHeight * 2.7f - ladderSimPos.Y - 0.7f);

            //if (movement.Y < 0) footPos.Y += 0.05f;

            MoveLimb(leftFoot,
                new Vector2(footPos.X,
                MathUtils.Round(footPos.Y + stepHeight, stepHeight * 2.0f) - stepHeight + ladderSimPos.Y),
                15.5f, true);

            MoveLimb(rightFoot,
                new Vector2(footPos.X,
                MathUtils.Round(footPos.Y, stepHeight * 2.0f) + ladderSimPos.Y),
                15.5f, true);

            //apply torque to the legs to make the knees bend
            Limb leftLeg = GetLimb(LimbType.LeftLeg);
            Limb rightLeg = GetLimb(LimbType.RightLeg);

            leftLeg.body.ApplyTorque(Dir * -8.0f);
            rightLeg.body.ApplyTorque(Dir * -8.0f);

            //apply forces to the head and the torso to move the character up/down
            float movementFactor = (handPos.Y / stepHeight) * (float)Math.PI;
            movementFactor = 0.8f + (float)Math.Abs(Math.Sin(movementFactor));

            Vector2 climbForce = new Vector2(0.0f, movement.Y + 0.4f) * movementFactor;
            torso.body.ApplyForce(climbForce * 40.0f * torso.Mass);
            head.body.SmoothRotate(0.0f);

            Rectangle trigger = character.SelectedConstruction.Prefab.Triggers.FirstOrDefault();
            if (trigger == null)
            {
                character.SelectedConstruction = null;
                return;
            }
            trigger = character.SelectedConstruction.TransformTrigger(trigger);

            //stop climbing if:
            //   - going too fast (can't grab a ladder while falling)
            //   - moving sideways
            //   - reached the top or bottom of the ladder
            if (Math.Abs(torso.LinearVelocity.Y) > 5.0f ||
                TargetMovement.X != 0.0f ||
                (TargetMovement.Y < 0.0f && ConvertUnits.ToSimUnits(trigger.Height) + handPos.Y < HeadPosition*1.5f) ||
                (TargetMovement.Y > 0.0f && handPos.Y > 0.3f))
            {
                Anim = Animation.None;
                character.SelectedConstruction = null;
                IgnorePlatforms = false;
            }

        }

        void UpdateStruggling()
        {
            Limb leftLeg    = GetLimb(LimbType.LeftFoot);
            Limb rightLeg   = GetLimb(LimbType.RightFoot);
            Limb torso      = GetLimb(LimbType.Torso);

            //walkPos += 0.2f;

            if (inWater) return;

            HandIK(GetLimb(LimbType.RightHand), GetLimb(LimbType.Head).SimPosition,0.1f);
            HandIK(GetLimb(LimbType.LeftHand), GetLimb(LimbType.Head).SimPosition,0.1f);
            
            //Vector2 footPos = torso.body.Position+ new Vector2(TorsoPosition*Dir,0.0f);

            //MoveLimb(leftLeg, footPos, 0.7f);
            //MoveLimb(rightLeg, footPos, 0.7f);
        }

        public override void HoldItem(float deltaTime, Camera cam, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, float holdAngle)
        {
            //calculate the handle positions
            Matrix itemTransfrom = Matrix.CreateRotationZ(item.body.Rotation);
            Vector2[] transformedHandlePos = new Vector2[2];
            transformedHandlePos[0] = Vector2.Transform(handlePos[0], itemTransfrom);
            transformedHandlePos[1] = Vector2.Transform(handlePos[1], itemTransfrom);

            Limb head       = GetLimb(LimbType.Head);
            Limb torso      = GetLimb(LimbType.Torso);
            Limb leftHand   = GetLimb(LimbType.LeftHand);
            Limb leftArm    = GetLimb(LimbType.LeftArm);
            Limb rightHand  = GetLimb(LimbType.RightHand);
            Limb rightArm   = GetLimb(LimbType.RightArm);

            Vector2 itemPos = character.GetInputState(InputType.SecondaryHeld) ? aimPos : holdPos;       

            float itemAngle;
            if (stunTimer <= 0.0f && character.GetInputState(InputType.SecondaryHeld) && itemPos != Vector2.Zero)
            {
                Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);

                Vector2 diff = (mousePos - torso.SimPosition) * Dir;

                holdAngle = MathUtils.VectorToAngle(new Vector2(diff.X, diff.Y * Dir)) - torso.body.Rotation * Dir;
                holdAngle = MathHelper.Clamp(MathUtils.WrapAnglePi(holdAngle), -1.3f, 1.0f);

                itemAngle = (torso.body.Rotation + holdAngle * Dir);

                head.body.SmoothRotate(itemAngle);

                if (TargetMovement == Vector2.Zero && inWater)
                {
                    torso.body.AngularVelocity -= torso.body.AngularVelocity * 0.1f;
                    torso.body.ApplyForce(torso.body.LinearVelocity * -0.5f);
                }

                aiming = true;
            }
            else
            {
                itemAngle = (torso.body.Rotation + holdAngle * Dir);
            }
            
            Vector2 shoulderPos = limbJoints[2].WorldAnchorA;
            Vector2 transformedHoldPos = shoulderPos;

            if (itemPos == Vector2.Zero)
            {
                if (character.SelectedItems[0] == item)
                {
                    transformedHoldPos = rightHand.pullJoint.WorldAnchorA - transformedHandlePos[0];
                    itemAngle = (rightHand.Rotation + (holdAngle - MathHelper.PiOver2) * Dir);
                    //rightHand.Disabled = true;
                }
                if (character.SelectedItems[1] == item)
                {
                    transformedHoldPos = leftHand.pullJoint.WorldAnchorA - transformedHandlePos[1];
                    itemAngle = (leftHand.Rotation + (holdAngle - MathHelper.PiOver2) * Dir);
                    //leftHand.Disabled = true;
                }
            }
            else
            {
                if (character.SelectedItems[0] == item)
                {
                    rightHand.Disabled = true;
                }
                if (character.SelectedItems[1] == item)
                {
                    leftHand.Disabled = true;
                }


                itemPos.X = itemPos.X * Dir;

                Matrix torsoTransform = Matrix.CreateRotationZ(itemAngle);

                transformedHoldPos += Vector2.Transform(itemPos, torsoTransform);
            }


            Vector2 bodyVelocity = torso.body.LinearVelocity / 60.0f;
            
            item.body.ResetDynamics();
            item.body.SetTransform(MathUtils.SmoothStep(item.body.Position, transformedHoldPos + bodyVelocity, 0.5f), itemAngle);

            //item.body.SmoothRotate(itemAngle, 50.0f);

            for (int i = 0; i < 2; i++)
            {
                if (character.SelectedItems[i] != item) continue;
                if (itemPos == Vector2.Zero) continue;

                Limb hand = (i == 0) ? rightHand : leftHand;

                HandIK(hand, transformedHoldPos + transformedHandlePos[i]);

                //Limb arm = (i == 0) ? rightArm : leftArm;

                ////hand length
                //float a = 37.0f;

                ////arm length
                //float b = 28.0f;

                ////distance from shoulder to holdpos
                //float c = ConvertUnits.ToDisplayUnits(Vector2.Distance(transformedHoldPos + transformedHandlePos[i], shoulderPos));
                //c = MathHelper.Clamp(a + b - 1, b-a, c);

                //float ang2 = MathUtils.VectorToAngle((transformedHoldPos + transformedHandlePos[i]) - shoulderPos)+MathHelper.PiOver2;

                //float armAngle = MathUtils.SolveTriangleSSS(a, b, c);
                //float handAngle = MathUtils.SolveTriangleSSS(b, a, c);

                //arm.body.SmoothRotate((ang2 - armAngle * Dir), 20.0f);
                //hand.body.SmoothRotate((ang2 + handAngle * Dir), 100.0f);
            }            
        }

        private void HandIK(Limb hand, Vector2 pos, float force = 1.0f)
        {
            Vector2 shoulderPos = limbJoints[2].WorldAnchorA;

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

            arm.body.SmoothRotate((ang2 - armAngle * Dir), 20.0f*force);
            hand.body.SmoothRotate((ang2 + handAngle * Dir), 100.0f*force);
        }

        public override void Flip()
        {
            base.Flip();

            Limb torso = GetLimb(LimbType.Torso);

            Vector2 difference;

            Matrix torsoTransform = Matrix.CreateRotationZ(torso.Rotation);
            
            for (int i = 0; i < character.SelectedItems.Length; i++)
            {
                if (character.SelectedItems[i] != null)
                {
                    difference = character.SelectedItems[i].body.Position - torso.SimPosition;
                    difference = Vector2.Transform(difference, torsoTransform);
                    difference.Y = -difference.Y;

                    character.SelectedItems[i].body.SetTransform(
                        torso.SimPosition + Vector2.Transform(difference, -torsoTransform),
                        MathUtils.WrapAngleTwoPi(-character.SelectedItems[i].body.Rotation));
                }
            }

            foreach (Limb l in limbs)
            {
                switch (l.type)
                {
                    case LimbType.LeftHand:
                    case LimbType.LeftArm:
                    case LimbType.RightHand:
                    case LimbType.RightArm:
                        difference = l.body.Position - torso.SimPosition;
                        difference = Vector2.Transform(difference, torsoTransform);
                        difference.Y = -difference.Y;

                        l.body.SetTransform(torso.SimPosition + Vector2.Transform(difference, -torsoTransform), -l.body.Rotation);
                        break;
                    default:
                        if (!inWater) l.body.SetTransform(l.body.Position, 
                            MathUtils.WrapAnglePi(l.body.Rotation * (l.DoesFlip ? -1.0f : 1.0f)));
                        break;
                }
            }

        }

    }
}
