using System;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;

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

        protected override float HeadPosition
        {
            get
            {
                return Crouching ? base.HeadPosition : base.HeadPosition;
            }
        }

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
                return Crouching ? base.TorsoAngle+0.5f : base.TorsoAngle;
            }
        }

        public HumanoidAnimController(Character character, XElement element)
            : base(character, element)
        {
            walkAnimSpeed = ToolBox.GetAttributeFloat(element, "walkanimspeed", 4.0f);
            walkAnimSpeed = MathHelper.ToRadians(walkAnimSpeed);

            movementLerp = ToolBox.GetAttributeFloat(element, "movementlerp", 0.4f);

            thighTorque = ToolBox.GetAttributeFloat(element, "thightorque", -5.0f);
        }

        public override void UpdateAnim(float deltaTime)
        {
            if (character.IsDead) return;

            Vector2 colliderPos = GetLimb(LimbType.Torso).SimPosition;

            //if (inWater) stairs = null;

            if (onFloorTimer <= 0.0f && !SimplePhysicsEnabled)
            {
            Vector2 rayStart = colliderPos; // at the bottom of the player sprite
            Vector2 rayEnd = rayStart - new Vector2(0.0f, TorsoPosition);
            if (stairs != null) rayEnd.Y -= 0.5f;
                //do a raytrace straight down from the torso to figure 
                //out whether the  ragdoll is standing on ground
                float closestFraction = 1;
                Structure closestStructure = null;
                GameMain.World.RayCast((fixture, point, normal, fraction) =>
                {
                    switch (fixture.CollisionCategories)
                    {
                        case Physics.CollisionStairs:
                            if (inWater && TargetMovement.Y < 0.5f) return -1;
                            Structure structure = fixture.Body.UserData as Structure;
                            if (stairs == null && structure != null)
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

                if (closestFraction == 1) //raycast didn't hit anything
                {
                    floorY = (currentHull == null) ? -1000.0f : ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height);
                }
                else
                {
                    floorY = rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;
                }
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
            
            //stun (= disable the animations) if the ragdoll receives a large enough impact
            if (strongestImpact > 0.0f)
            {
                character.StartStun(MathHelper.Min(strongestImpact * 0.5f, 5.0f));
            }
            strongestImpact = 0.0f;

            
            if (stunTimer > 0)
            {
                stunTimer -= deltaTime;
                return;
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


                midPos += Vector2.Transform(new Vector2(-0.3f*Dir,-0.2f), torsoTransform);

                if (rightHand.pullJoint.Enabled) midPos = (midPos + rightHand.pullJoint.WorldAnchorB) / 2.0f;

                HandIK(rightHand, midPos);
                HandIK(leftHand, midPos);

                //rightHand.pullJoint.Enabled = true;
                //rightHand.pullJoint.WorldAnchorB = midPos;

                //rightHand.pullJoint.Enabled = true;
                //rightHand.pullJoint.WorldAnchorB = midPos;
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


            switch (Anim)
            {
                case Animation.Climbing:
                    UpdateClimbing();
                    break;
                case Animation.UsingConstruction:
                    UpdateStanding();
                    break;
                case Animation.CPR:
                    UpdateCPR(deltaTime);
                    break;
                default:

                    if (character.SelectedCharacter != null) DragCharacter(character.SelectedCharacter);

                    if (inWater)
                        UpdateSwimming();
                    else
                        UpdateStanding();

                    break;
            }

            if (TargetDir != dir) Flip();

            foreach (Limb limb in Limbs)
            {
                limb.Disabled = false;
            }

            aiming = false;
        }



        void UpdateStanding()
        {
            Vector2 handPos;

            //if you're allergic to magic numbers, stop reading now

            Limb leftFoot   = GetLimb(LimbType.LeftFoot);
            Limb rightFoot  = GetLimb(LimbType.RightFoot);
            Limb head       = GetLimb(LimbType.Head);
            Limb torso      = GetLimb(LimbType.Torso);

            Limb waist      = GetLimb(LimbType.Waist);

            Limb leftHand   = GetLimb(LimbType.LeftHand);
            Limb rightHand  = GetLimb(LimbType.RightHand);

            Limb leftLeg    = GetLimb(LimbType.LeftLeg);
            Limb rightLeg   = GetLimb(LimbType.RightLeg);
            
            float getUpSpeed = 0.3f;
            float walkCycleSpeed = head.LinearVelocity.X * walkAnimSpeed;
            if (stairs != null)
            {
                TargetMovement = new Vector2(MathHelper.Clamp(TargetMovement.X, -1.5f, 1.5f), TargetMovement.Y);

                if ((TargetMovement.X > 0.0f && stairs.StairDirection == Direction.Right) ||
                    TargetMovement.X < 0.0f && stairs.StairDirection == Direction.Left)
                {
                    TargetMovement *= 1.7f;
                    walkCycleSpeed *= 1.7f;
                }
                else
                {
                    TargetMovement /= 1.0f;
                    walkCycleSpeed *= 1.5f;
                }                
            }

            Vector2 colliderPos = new Vector2(torso.SimPosition.X, floorY);

            if (Math.Abs(TargetMovement.X) > 1.0f)
            {
                int limbsInWater = 0;
                foreach (Limb limb in Limbs)
                {
                    if (limb.inWater) limbsInWater++;
                }

                float slowdownFactor = (float)limbsInWater / (float)Limbs.Count();

                float maxSpeed = Math.Max(TargetMovement.Length() - slowdownFactor, 1.0f);
               // if (character.SelectedCharacter!=null) maxSpeed = Math.Min(maxSpeed, 1.0f);

                TargetMovement = Vector2.Normalize(TargetMovement) * maxSpeed;
            }

            float walkPosX = (float)Math.Cos(walkPos);
            float walkPosY = (float)Math.Sin(walkPos);
            float runningModifier = (float)Math.Max(Math.Min(Math.Abs(TargetMovement.X),3.0f) / 1.5f, 1.0);

            Vector2 stepSize = new Vector2(
                this.stepSize.X * walkPosX * runningModifier,
                this.stepSize.Y * walkPosY * runningModifier * runningModifier);

            if (Crouching) stepSize *= 0.5f;
            
            float footMid = waist.SimPosition.X;// (leftFoot.SimPosition.X + rightFoot.SimPosition.X) / 2.0f;
            
            movement = MathUtils.SmoothStep(movement, TargetMovement*walkSpeed, movementLerp);
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

            if (LowestLimb == null) return;

            if (!onGround || (LowestLimb.SimPosition.Y - floorY > 0.5f && stairs == null)) return;

            float? ceilingY = null;
            if (Submarine.PickBody(head.SimPosition, head.SimPosition + Vector2.UnitY, null, Physics.CollisionWall)!=null)
            {
                ceilingY = Submarine.LastPickedPosition.Y;

                if (ceilingY - floorY < HeadPosition) Crouching = true;
            }

            getUpSpeed = getUpSpeed * Math.Max(head.SimPosition.Y - colliderPos.Y, 0.5f);

            torso.pullJoint.Enabled = true;
            head.pullJoint.Enabled = true;
            waist.pullJoint.Enabled = true;

            if (stairs != null)
            {
                if (LowestLimb.SimPosition.Y < stairs.SimPosition.Y) IgnorePlatforms = true;

                torso.pullJoint.WorldAnchorB = new Vector2(
                    MathHelper.SmoothStep(torso.SimPosition.X, footMid + movement.X * 0.35f, getUpSpeed * 0.8f),
                    MathHelper.SmoothStep(torso.SimPosition.Y, colliderPos.Y + TorsoPosition - Math.Abs(walkPosX * 0.05f), getUpSpeed * 2.0f));


                head.pullJoint.WorldAnchorB = new Vector2(
                    MathHelper.SmoothStep(head.SimPosition.X, footMid + movement.X * (Crouching ? 1.0f : 0.4f), getUpSpeed * 0.8f),
                    MathHelper.SmoothStep(head.SimPosition.Y, colliderPos.Y + HeadPosition - Math.Abs(walkPosX * 0.05f), getUpSpeed * 2.0f));

                waist.pullJoint.WorldAnchorB = waist.SimPosition;// +movement * 0.3f;
            }
            else
            {
                torso.pullJoint.WorldAnchorB =
                    MathUtils.SmoothStep(torso.SimPosition,
                    new Vector2(footMid + movement.X * 0.3f, colliderPos.Y + TorsoPosition), getUpSpeed);

                head.pullJoint.WorldAnchorB =
                    MathUtils.SmoothStep(head.SimPosition,
                    new Vector2(footMid + movement.X * (Crouching && Math.Sign(movement.X)==Math.Sign(Dir) ? 1.0f : 0.3f), colliderPos.Y + HeadPosition), getUpSpeed*1.2f);

                waist.pullJoint.WorldAnchorB = waist.SimPosition + movement * 0.1f;
            }


            //moving horizontally
            if (TargetMovement.X != 0.0f)
            {
                //progress the walking animation
                walkPos -= (walkCycleSpeed / runningModifier) * 0.8f;

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

                leftFoot.body.SmoothRotate(leftLeg.body.Rotation + MathHelper.PiOver2 * Dir * 1.6f, 20.0f * runningModifier);
                rightFoot.body.SmoothRotate(rightLeg.body.Rotation + MathHelper.PiOver2 * Dir * 1.6f, 20.0f * runningModifier);

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
                            (Math.Sign(walkPosX) == Math.Sign(Dir)) ? handPos.Y : lowerY), 0.7f*runningModifier);
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
                float movementFactor = (movement.X / 4.0f) * movement.X * Math.Sign(movement.X);
                


                //MoveLimb(leftFoot, footPos, 2.5f);

                for (int i = -1; i < 2; i+=2 )
                {
                Vector2 footPos = new Vector2(
                    Crouching ?  waist.SimPosition.X + Math.Sign(stepSize.X*i)*Dir*0.3f : waist.SimPosition.X,
                    colliderPos.Y - 0.2f);

                    var foot = i == -1 ? rightFoot : leftFoot;

                    MoveLimb(foot, footPos, Math.Abs(foot.SimPosition.X - footPos.X)*50.0f);
                }

                leftFoot.body.SmoothRotate(Dir * MathHelper.PiOver2, 5.0f);
                rightFoot.body.SmoothRotate(Dir * MathHelper.PiOver2, 5.0f);

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
            movement = MathUtils.SmoothStep(movement, TargetMovement, movementLerp);

            if (inWater && movement.LengthSquared() > 0.00001f)
            {
                movement = Vector2.Normalize(movement);
            }

            RefLimb.pullJoint.Enabled = true;
            RefLimb.pullJoint.WorldAnchorB =
                RefLimb.SimPosition + movement * 0.15f;

            RefLimb.body.SmoothRotate(0.0f);

            foreach (Limb l in Limbs)
            {
                if (l == RefLimb) continue;
                l.body.SetTransform(RefLimb.SimPosition, RefLimb.Rotation);
            }
            //new Vector2(movement.X, floorY + HeadPosition), 0.5f);
        }

        void UpdateSwimming()
        {
            IgnorePlatforms = true;

            Vector2 footPos, handPos;

            float surfaceLimiter = 1.0f;

            Limb head = GetLimb(LimbType.Head);

            if (currentHull != null && (currentHull.Rect.Y - currentHull.Surface > 50.0f) && !head.inWater)
            {
                surfaceLimiter = (ConvertUnits.ToDisplayUnits(head.SimPosition.Y) - surfaceY);
                surfaceLimiter = Math.Max(1.0f, surfaceLimiter);
                if (surfaceLimiter > 20.0f) return;
            }

            Limb torso = GetLimb(LimbType.Torso);
            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            Limb leftFoot = GetLimb(LimbType.LeftFoot);
            Limb rightFoot = GetLimb(LimbType.RightFoot);

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

            float targetSpeed = TargetMovement.Length();
            if (targetSpeed > 0.0f) TargetMovement /= targetSpeed;

            if (targetSpeed > 0.1f)
            {
                if (!aiming)
                {
                    torso.body.SmoothRotate(MathUtils.VectorToAngle(TargetMovement) - MathHelper.PiOver2);
                }
            }
            else
            {
                if (aiming)
                {
                    Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);
                    Vector2 diff = (mousePos - torso.SimPosition) * Dir;

                    TargetMovement = new Vector2(0.0f, -0.1f);

                    torso.body.SmoothRotate(MathUtils.VectorToAngle(diff));
                }
            }

            if (TargetMovement == Vector2.Zero) return;

            movement = MathUtils.SmoothStep(movement, TargetMovement*swimSpeed, 0.3f);

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

            movement.Y -= 0.05f;

            head.body.ApplyForce((new Vector2(movement.X,
                    movement.Y / surfaceLimiter + 0.2f) - head.body.LinearVelocity * 0.2f) *
                    30.0f * head.body.Mass);

            torso.body.ApplyForce((new Vector2(movement.X,
                    movement.Y / surfaceLimiter + 0.2f) - torso.body.LinearVelocity * 0.2f) * 10.0f * torso.body.Mass);

            walkPos += movement.Length() * 0.15f;
            footPos = (leftFoot.SimPosition + rightFoot.SimPosition) / 2.0f;

            var rightThigh = GetLimb(LimbType.RightThigh);
            var leftThigh = GetLimb(LimbType.LeftThigh);

            rightThigh.body.SmoothRotate(torso.Rotation + (float)Math.Sin(walkPos) * 0.3f, 2.0f);
            leftThigh.body.SmoothRotate(torso.Rotation - (float)Math.Sin(walkPos) * 0.3f, 2.0f);

            Vector2 transformedFootPos = new Vector2((float)Math.Sin(walkPos) * 0.5f, 0.0f);
            transformedFootPos = Vector2.Transform(
                transformedFootPos,
                Matrix.CreateRotationZ(torso.body.Rotation));

            if (Math.Abs(MathUtils.GetShortestAngle(torso.Rotation, rightThigh.Rotation)) < 0.3f)
            {
                MoveLimb(rightFoot, footPos - transformedFootPos, 1.0f);
            }
            if (Math.Abs(MathUtils.GetShortestAngle(torso.Rotation, leftThigh.Rotation)) < 0.3f)
            {
                MoveLimb(leftFoot, footPos + transformedFootPos, 1.0f);
            }

            handPos = (torso.SimPosition + head.SimPosition) / 2.0f;

            //if (!rightHand.Disabled) rightHand.body.ApplyTorque(leftHand.body.Mass * Dir);
            //if (!leftHand.Disabled) leftHand.body.ApplyTorque(leftHand.body.Mass * Dir);

            //at the surface, not moving sideways -> hands just float around
            if (!headInWater && TargetMovement.X == 0.0f && TargetMovement.Y > 0)
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
            float handPosY = (float)Math.Sin(handCyclePos) * 1.0f;
            handPosY = MathHelper.Clamp(handPosY, -0.8f, 0.8f);

            Matrix rotationMatrix = Matrix.CreateRotationZ(torso.Rotation);

            if (!rightHand.Disabled)
            {
                Vector2 rightHandPos = new Vector2(-handPosX, -handPosY);
                rightHandPos.X = (Dir == 1.0f) ? Math.Max(0.2f, rightHandPos.X) : Math.Min(-0.2f, rightHandPos.X);
                rightHandPos = Vector2.Transform(rightHandPos, rotationMatrix);

                //MoveLimb(rightHand, handPos + rightHandPos, 1.5f);

                HandIK(rightHand, handPos + rightHandPos, 0.5f);
            }

            if (!leftHand.Disabled)
            {
                Vector2 leftHandPos = new Vector2(handPosX, handPosY);
                leftHandPos.X = (Dir == 1.0f) ? Math.Max(0.2f, leftHandPos.X) : Math.Min(-0.2f, leftHandPos.X);
                leftHandPos = Vector2.Transform(leftHandPos, rotationMatrix);

                //MoveLimb(leftHand, handPos + leftHandPos,1.5f);
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
            //if (TargetMovement.Y != 0.0f)
            //{
            //    tempTargetMovement.Y = Math.Max(Math.Abs(TargetMovement.Y), 0.6f) * Math.Sign(TargetMovement.Y);
            //}
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

            if (currentHull==null && character.SelectedConstruction.Submarine!=null)
            {
                ladderSimPos += character.SelectedConstruction.Submarine.SimPosition;
            }

            MoveLimb(head, new Vector2(ladderSimPos.X - 0.27f * Dir, head.SimPosition.Y + 0.05f), 10.5f);
            MoveLimb(torso, new Vector2(ladderSimPos.X - 0.27f * Dir, torso.SimPosition.Y), 10.5f);
            MoveLimb(waist, new Vector2(ladderSimPos.X - 0.35f * Dir, waist.SimPosition.Y), 10.5f);
            

            Vector2 handPos = new Vector2(
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

            Vector2 footPos = new Vector2(
                handPos.X - Dir * 0.05f,
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

            //apply forces to the head and the torso to move the Character up/down
            float movementFactor = (handPos.Y / stepHeight) * (float)Math.PI;
            movementFactor = 0.8f + (float)Math.Abs(Math.Sin(movementFactor));

            Vector2 subSpeed = currentHull != null || character.SelectedConstruction.Submarine == null 
                ? Vector2.Zero : character.SelectedConstruction.Submarine.Velocity;

            Vector2 climbForce = new Vector2(0.0f, movement.Y + (inWater ? -0.05f : 0.6f)) * movementFactor;
            if (climbForce.Y > 0.5f) climbForce.Y = Math.Max(climbForce.Y, 1.3f);
            torso.body.ApplyForce((climbForce * 40.0f + subSpeed*50.0f) * torso.Mass);
            head.body.SmoothRotate(0.0f);

            if (!character.SelectedConstruction.Prefab.Triggers.Any())
            {
                character.SelectedConstruction = null;
                return;
            }

            Rectangle trigger = character.SelectedConstruction.Prefab.Triggers.FirstOrDefault();
            trigger = character.SelectedConstruction.TransformTrigger(trigger);

            bool notClimbing = false;
            if (character.IsNetworkPlayer)
            {
                notClimbing = character.IsKeyDown(InputType.Left) || character.IsKeyDown(InputType.Right);
            }
            else
            {
                notClimbing = targetMovement.X != 0.0f;
            }

            //stop climbing if:
            //   - moving sideways
            //   - reached the top or bottom of the ladder
            if (notClimbing ||
                (TargetMovement.Y < 0.0f && ConvertUnits.ToSimUnits(trigger.Height) + handPos.Y < HeadPosition) ||
                (TargetMovement.Y > 0.0f && handPos.Y > 0.1f))
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

            Crouching = true;

            Vector2 diff = character.SelectedCharacter.SimPosition - character.SimPosition;
            var targetHead = character.SelectedCharacter.AnimController.GetLimb(LimbType.Head);

            Vector2 headDiff = targetHead == null ? diff : targetHead.SimPosition - character.SimPosition;

            targetMovement = new Vector2(diff.X, 0.0f);
            TargetDir = headDiff.X > 0.0f ? Direction.Right : Direction.Left;
            
            UpdateStanding();

            Vector2 handPos = character.SelectedCharacter.AnimController.GetLimb(LimbType.Torso).SimPosition + Vector2.UnitY*0.2f;

            Grab(handPos, handPos);

            float yPos = (float)Math.Sin(cprAnimState) * 0.1f;
            cprAnimState += deltaTime*8.0f;

            var head = GetLimb(LimbType.Head);
            head.pullJoint.WorldAnchorB = new Vector2(targetHead.SimPosition.X, targetHead.SimPosition.Y + 0.6f + yPos);
            head.pullJoint.Enabled = true;


            //RefLimb.pullJoint.WorldAnchorB = new Vector2(targetHead.SimPosition.X - Math.Sign(headDiff.X) * 0.5f, targetHead.SimPosition.Y + 0.4f + yPos);
            //head.pullJoint.Enabled = true;

            

            //DragCharacter(character.SelectedCharacter, LimbType.Torso, LimbType.Head);
        }

        //float punchTimer;
        //bool punching;

        //public void Punch()
        //{
        //    if (punchTimer < 0.01f) punching = true;

        //    Limb rightHand = GetLimb(LimbType.RightHand);
        //    Limb head = GetLimb(LimbType.Head);

        //    Vector2 diff = Vector2.Normalize(Character.CursorPosition - RefLimb.Position);

        //    rightHand.body.ApplyLinearImpulse(diff * 20.0f);
        //    head.body.ApplyLinearImpulse(diff * 5.0f);
        //    head.body.ApplyTorque(Dir*100.0f);
        //}

        //public void Block(float deltaTime)
        //{
        //    Limb head = GetLimb(LimbType.Head);
        //    Limb torso = GetLimb(LimbType.Torso);
        //    Limb leftHand = GetLimb(LimbType.LeftHand);
        //    Limb leftFoot    = GetLimb(LimbType.LeftFoot);
        //    Limb rightHand = GetLimb(LimbType.RightHand);

        //    Vector2 pos = head.SimPosition;

        //    rightHand.Disabled = true;
        //    leftHand.Disabled = true;

        //    HandIK(leftHand, pos + new Vector2(0.25f*Dir, 0.0f));

        //    if (punching)
        //    {
        //        punchTimer += deltaTime*10.0f;
        //        if (punchTimer>2.0f)
        //        {
        //            punching = false;
        //        }
        //    }
        //    else
        //    {
        //        punchTimer = MathHelper.Lerp(punchTimer, 0.0f, 0.3f);
        //        HandIK(rightHand, pos + new Vector2((0.3f + punchTimer) * Dir, 0.1f));
        //    }            
        //}

        public override void DragCharacter(Character target, LimbType rightHandTarget = LimbType.RightHand, LimbType leftHandTarget = LimbType.LeftHand)
        {
            if (target == null) return;

            Limb leftHand = GetLimb(LimbType.LeftHand);
            Limb rightHand = GetLimb(LimbType.RightHand);

            //only grab with one hand when swimming
            leftHand.Disabled = true;
            if (!inWater) rightHand.Disabled = true;
            
            for (int i = 0; i < 2; i++ )
            {
                LimbType type = i == 0 ? leftHandTarget: rightHandTarget;
                Limb targetLimb = target.AnimController.GetLimb(type);
                
                Limb pullLimb = GetLimb(i == 0 ?LimbType.LeftHand: LimbType.RightHand);

                if (i==1 && inWater)
                {
                    targetLimb.pullJoint.Enabled = false;
                }
                else
                {
                    pullLimb.pullJoint.Enabled = true;
                    pullLimb.pullJoint.WorldAnchorB = targetLimb.SimPosition;
                    pullLimb.pullJoint.MaxForce = 10000.0f;

                    targetLimb.pullJoint.Enabled = true;
                    targetLimb.pullJoint.WorldAnchorB = pullLimb.SimPosition;
                    targetLimb.pullJoint.MaxForce = 10000.0f;
                }

            }


            target.AnimController.IgnorePlatforms = IgnorePlatforms;

            if (target.Stun > 0.0f || target.IsDead)
            {
                target.AnimController.TargetMovement = TargetMovement;
            }
            else if (target is AICharacter)
            {
                target.AnimController.TargetMovement = Vector2.Lerp(target.AnimController.TargetMovement, (character.SimPosition + Vector2.UnitX*Dir) - target.SimPosition, 0.5f);
            }
        }

        public void Grab(Vector2 rightHandPos, Vector2 leftHandPos)
        {
            for (int i = 0; i < 2; i++)
            {
                Limb pullLimb = (i == 0) ? GetLimb(LimbType.LeftHand) : GetLimb(LimbType.RightHand);

                pullLimb.Disabled = true;

                pullLimb.pullJoint.Enabled = true;
                pullLimb.pullJoint.WorldAnchorB = (i==0) ? rightHandPos : leftHandPos;
                pullLimb.pullJoint.MaxForce = 500.0f;
            }

        }

        public override void HoldItem(float deltaTime, Item item, Vector2[] handlePos, Vector2 holdPos, Vector2 aimPos, bool aim, float holdAngle)
        {
            Holdable holdable = item.GetComponent<Holdable>();

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
            if (Anim != Animation.Climbing && !usingController && stunTimer <= 0.0f && aim && itemPos != Vector2.Zero)
            {
                Vector2 mousePos = ConvertUnits.ToSimUnits(character.CursorPosition);

                Vector2 diff = (mousePos - torso.SimPosition) * Dir;

                holdAngle = MathUtils.VectorToAngle(new Vector2(diff.X, diff.Y * Dir)) - torso.body.Rotation * Dir;

                itemAngle = (torso.body.Rotation + holdAngle * Dir);

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

            Vector2 shoulderPos = limbJoints[2].WorldAnchorA;
            Vector2 transformedHoldPos = shoulderPos;

            if (itemPos == Vector2.Zero || Anim == Animation.Climbing || usingController)
            {
                if (character.SelectedItems[1] == item)
                {
                    transformedHoldPos = leftHand.pullJoint.WorldAnchorA - transformedHandlePos[1];
                    itemAngle = (leftHand.Rotation + (holdAngle - MathHelper.PiOver2) * Dir);
                }
                if (character.SelectedItems[0] == item)
                {
                    transformedHoldPos = rightHand.pullJoint.WorldAnchorA - transformedHandlePos[0];
                    itemAngle = (rightHand.Rotation + (holdAngle - MathHelper.PiOver2) * Dir);
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

            item.body.ResetDynamics();

            Vector2 currItemPos = (character.SelectedItems[0]==item) ?
                rightHand.pullJoint.WorldAnchorA - transformedHandlePos[0] :
                leftHand.pullJoint.WorldAnchorA - transformedHandlePos[1];
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

            arm.body.SmoothRotate((ang2 - armAngle * Dir), 20.0f * force);
            hand.body.SmoothRotate((ang2 + handAngle * Dir), 100.0f * force);
        }

        public override Vector2 EstimateCurrPosition(Vector2 prevPosition, float timePassed)
        {
            timePassed = MathHelper.Clamp(timePassed, 0.0f, 1.0f);

            Vector2 targetMovement = character.GetTargetMovement();

            Vector2 currPosition = prevPosition + targetMovement * timePassed/500.0f;

            return currPosition;
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
                if (character.SelectedItems[i] != null)
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

                if (!limb.pullJoint.Enabled && mirror)
                {
                    difference = limb.body.SimPosition - torso.SimPosition;
                    difference = Vector2.Transform(difference, torsoTransform);
                    difference.Y = -difference.Y;

                    position = torso.SimPosition + Vector2.Transform(difference, -torsoTransform);

                    //TrySetLimbPosition(limb, limb.SimPosition, );
                }

                float angle = flipAngle ? -limb.body.Rotation : limb.body.Rotation;
                if (wrapAngle) angle = MathUtils.WrapAnglePi(angle);

                TrySetLimbPosition(limb, RefLimb.SimPosition, position);

                limb.body.SetTransform(limb.body.SimPosition, angle);
            }
        }
        
    }
}
