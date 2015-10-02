using System;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{
    class FishAnimController : AnimController
    {
        //amplitude and wave length of the "sine wave" swimming animation
        //if amplitude = 0, sine wave animation isn't used
        private float waveAmplitude;
        private float waveLength;

        private bool rotateTowardsMovement;

        private bool flip;

        private float flipTimer;

        private float? footRotation;

        public FishAnimController(Character character, XElement element)
            : base(character, element)
        {
            waveAmplitude = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "waveamplitude", 0.0f));
            waveLength = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "wavelength", 0.0f));
            
            flip = ToolBox.GetAttributeBool(element, "flip", false);
            
            walkSpeed = ToolBox.GetAttributeFloat(element, "walkspeed", 1.0f);
            swimSpeed = ToolBox.GetAttributeFloat(element, "swimspeed", 1.0f);

            float footRot = ToolBox.GetAttributeFloat(element,"footrotation", float.NaN);
            if (float.IsNaN(footRot))
            {
                footRotation = null;
            }
            else
            {
                footRotation = MathHelper.ToRadians(footRot);
            }

            rotateTowardsMovement = ToolBox.GetAttributeBool(element, "rotatetowardsmovement", true);
        }

        public override void UpdateAnim(float deltaTime)
        {
            if (character.IsDead)
            {
                UpdateDying(deltaTime);
                return;
            }

            ResetPullJoints();

            if (strongestImpact > 0.0f)
            {
                stunTimer = MathHelper.Clamp(strongestImpact * 0.5f, stunTimer, 5.0f);
                strongestImpact = 0.0f;
            }

            if (stunTimer>0.0f)
            {
                //UpdateStruggling(deltaTime);
                stunTimer -= deltaTime;
                return;
            }
            else if (SimplePhysicsEnabled)
            {
                UpdateSimpleAnim();
            }
            else
            {
                if (inWater)
                {
                    UpdateSineAnim(deltaTime);
                }
                else
                {
                    UpdateWalkAnim(deltaTime);
                }
            }

            if (flip)
            {
                //targetDir = (movement.X > 0.0f) ? Direction.Right : Direction.Left;
                if (movement.X > 0.1f && movement.X > Math.Abs(movement.Y)*0.5f)
                {
                    TargetDir = Direction.Right;
                }
                else if (movement.X < -0.1f && movement.X < -Math.Abs(movement.Y)*0.5f)
                {
                    TargetDir = Direction.Left;
                }
            }
            else
            {
                Limb head = GetLimb(LimbType.Head);
                float rotation = MathUtils.WrapAngleTwoPi(head.Rotation);
                rotation = MathHelper.ToDegrees(rotation);

                if (rotation < 0.0f) rotation += 360;

                if (rotation > 20 && rotation < 160)
                {
                    TargetDir = Direction.Left;
                }
                else if (rotation > 200 && rotation < 340)
                {
                    TargetDir = Direction.Right;
                }
            }
            
            //if (stunTimer > gameTime.TotalGameTime.TotalMilliseconds) return;
            flipTimer += deltaTime;
            
            if (TargetDir != dir) 
            {   
                if (flipTimer>1.0f)
                {
                    Flip();
                    if (flip) Mirror();
                    flipTimer = 0.0f;
                }              
            }
        }

        void UpdateSineAnim(float deltaTime)
        {
            movement = MathUtils.SmoothStep(movement, TargetMovement*swimSpeed, 1.0f);
            if (movement == Vector2.Zero) return;

            if (!inWater) movement.Y = Math.Min(0.0f, movement.Y);

            float movementAngle = MathUtils.VectorToAngle(movement) - MathHelper.PiOver2;

            Limb tail = GetLimb(LimbType.Tail);
            if (tail != null && waveAmplitude > 0.0f)
            {
                walkPos -= movement.Length();

                float waveRotation = (float)Math.Sin(walkPos / waveLength) * waveAmplitude;
                
                float angle = MathUtils.GetShortestAngle(tail.body.Rotation, movementAngle + waveRotation);

                tail.body.ApplyTorque(angle * tail.Mass);
                
                //limbs[tailIndex].body.ApplyTorque((Math.Sign(angle) + Math.Max(Math.Min(angle * 10.0f, 10.0f), -10.0f)) * limbs[tailIndex].body.Mass);
                //limbs[tailIndex].body.ApplyTorque(-limbs[tailIndex].body.AngularVelocity * 0.5f * limbs[tailIndex].body.Mass);
            }

            Vector2 steerForce = Vector2.Zero;

            Limb head = GetLimb(LimbType.Head);
            if (head != null)
            {
                float angle = (rotateTowardsMovement) ?
                    head.body.Rotation+ MathUtils.GetShortestAngle(head.body.Rotation, movementAngle) :
                    HeadAngle*Dir;                    


                head.body.SmoothRotate(angle, 25.0f);
                //rotate head towards the angle of movement
                //float torque = (Math.Sign(angle)*10.0f + MathHelper.Clamp(angle * 10.0f, -10.0f, 10.0f));
                //angular drag
                //torque -= head.body.AngularVelocity * 0.5f;
                //head.body.ApplyTorque(torque * head.body.Mass);
                

                //the movement vector if going to the direction of the head
                //Vector2 headMovement = new Vector2(
                //    (float)Math.Cos(head.body.Rotation - MathHelper.PiOver2),
                //    (float)Math.Sin(head.body.Rotation - MathHelper.PiOver2));
                //headMovement *= movement.Length();

                //the movement angle is between direction of the head and the direction 
                //where the character is actually trying to go
                
                //current * (float)alpha + previous * (1.0f - (float)alpha);


                steerForce = (movement * 50.0f - head.LinearVelocity * 30.0f);
               // force += (headMovement - movement) * Math.Min(head.LinearVelocity.Length()/movement.Length(), 1.0f);

                if (!inWater) steerForce.Y = 0.0f;
            }

            for (int i = 0; i < Limbs.Count(); i++)
            {
                if (steerForce!=Vector2.Zero)
                    Limbs[i].body.ApplyForce(steerForce * Limbs[i].SteerForce * Limbs[i].Mass);

                if (Limbs[i].type != LimbType.Torso) continue;

                float dist = (Limbs[0].SimPosition - Limbs[i].SimPosition).Length();

                Vector2 limbPos = Limbs[0].SimPosition - Vector2.Normalize(movement) * dist;

                Limbs[i].body.ApplyForce(((limbPos - Limbs[i].SimPosition) * 3.0f - Limbs[i].LinearVelocity * 3.0f) * Limbs[i].Mass);
            }

            if (!inWater)
            {
                UpdateWalkAnim(deltaTime);
            }
            else
            {
                floorY = Limbs[0].SimPosition.Y;
            }
        }

        void UpdateSimpleAnim()
        {
            movement = MathUtils.SmoothStep(movement, TargetMovement*swimSpeed, 1.0f);
            if (movement == Vector2.Zero) return;

            float movementAngle = MathUtils.VectorToAngle(movement) - MathHelper.PiOver2;
            
            RefLimb.body.SmoothRotate(
                (rotateTowardsMovement) ?
                RefLimb.body.Rotation + MathUtils.GetShortestAngle(RefLimb.body.Rotation, movementAngle) :
                HeadAngle*Dir);

            RefLimb.pullJoint.Enabled = true;
            RefLimb.pullJoint.WorldAnchorB =
                RefLimb.SimPosition + movement * 0.1f;

            RefLimb.body.SmoothRotate(0.0f);

            foreach (Limb l in Limbs)
            {
                if (l == RefLimb) continue;
                l.body.SetTransform(RefLimb.SimPosition, RefLimb.Rotation);
            }
        }
    
        void UpdateWalkAnim(float deltaTime)
        {
            movement = MathUtils.SmoothStep(movement, TargetMovement * walkSpeed, 0.2f);
            if (movement == Vector2.Zero) return;

            IgnorePlatforms = (TargetMovement.Y < -Math.Abs(TargetMovement.X));

            Limb colliderLimb;
            float colliderHeight;

            Limb torso  = GetLimb(LimbType.Torso);
            Limb head   = GetLimb(LimbType.Head);

            if (torso != null)
            {
                colliderLimb = torso;
                colliderHeight = TorsoPosition;

                colliderLimb.body.SmoothRotate(TorsoAngle * Dir, 10.0f);
            }
            else
            {
                colliderLimb = head;
                colliderHeight = HeadPosition;

                if (onGround) colliderLimb.body.SmoothRotate(HeadAngle * Dir, 100.0f);
            }
            
            Vector2 colliderPos = colliderLimb.SimPosition;

            Vector2 rayStart = colliderPos;
            Vector2 rayEnd = rayStart - new Vector2(0.0f, colliderHeight);
            if (stairs != null) rayEnd.Y -= 0.5f;

            //do a raytrace straight down from the torso to figure 
            //out whether the  ragdoll is standing on ground
            float closestFraction = 1;
            //Structure closestStructure = null;
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                //other limbs and bodies with no collision detection are ignored
                if (fixture == null ||
                    fixture.CollisionCategories == Physics.CollisionCharacter ||
                    fixture.CollisionCategories == Physics.CollisionNone ||
                    fixture.CollisionCategories == Physics.CollisionMisc) return -1;

                Structure structure = fixture.Body.UserData as Structure;
                if (structure != null)
                {
                    if (structure.StairDirection != Direction.None && (stairs == null)) return -1;
                    if (structure.IsPlatform && (IgnorePlatforms || stairs != null)) return -1;
                }

                onGround = true;
                onFloorTimer = 0.05f;

                if (fraction < closestFraction) closestFraction = fraction;
                return 1;
            }
            , rayStart, rayEnd);

            //the ragdoll "stays on ground" for 50 millisecs after separation
            if (onFloorTimer <= 0.0f)
            {
                onGround = false;
            }
            else
            {
                onFloorTimer -= deltaTime;
            }

            if (!onGround) return;

            if (closestFraction == 1) //raycast didn't hit anything
                floorY = (currentHull == null) ? -1000.0f : ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height);
            else
                floorY = rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;


            if (Math.Abs(colliderPos.Y - floorY) < colliderHeight * 1.2f)
            {
                colliderLimb.Move(new Vector2(colliderPos.X + movement.X * 0.2f, floorY + colliderHeight), 5.0f);
            }

            float walkCycleSpeed = head.LinearVelocity.X * 0.05f;

            walkPos -= walkCycleSpeed;

            Vector2 transformedStepSize = new Vector2(
                (float)Math.Cos(walkPos) * stepSize.X * 3.0f,
                (float)Math.Sin(walkPos) * stepSize.Y * 2.0f);

            foreach (Limb limb in Limbs)
            {
                switch (limb.type)
                {
                    case LimbType.LeftFoot:
                    case LimbType.RightFoot:
                        Vector2 footPos = new Vector2(limb.SimPosition.X, colliderPos.Y - colliderHeight);

                        if (limb.RefJointIndex>-1)
                        {
                            RevoluteJoint refJoint = limbJoints[limb.RefJointIndex];
                            footPos.X = refJoint.WorldAnchorA.X;
                        }
                        footPos.X += limb.StepOffset.X * Dir;
                        footPos.Y += limb.StepOffset.Y;

                        if (limb.type == LimbType.LeftFoot)
                        {
                            limb.Move(footPos +new Vector2(
                                transformedStepSize.X + movement.X * 0.1f,
                                (transformedStepSize.Y > 0.0f) ? transformedStepSize.Y : 0.0f),
                            8.0f);
                        }
                        else if (limb.type == LimbType.RightFoot)
                        {
                            limb.Move(footPos +new Vector2(
                                -transformedStepSize.X + movement.X * 0.1f,
                                (-transformedStepSize.Y > 0.0f) ? -transformedStepSize.Y : 0.0f),
                            8.0f);
                        }

                        if (footRotation!=null) limb.body.SmoothRotate((float)footRotation*Dir, 50.0f);

                        break;
                    case LimbType.LeftLeg:
                    case LimbType.RightLeg:
                        if (legTorque!=0.0f) limb.body.ApplyTorque(limb.Mass*legTorque*Dir);                        
                        break;
                }
            }
        }

        void UpdateDying(float deltaTime)
        {
            Limb head = GetLimb(LimbType.Head);
            Limb tail = GetLimb(LimbType.Tail);

            if (head != null) head.body.ApplyTorque(head.Mass * Dir * (float)Math.Sin(walkPos) * 5.0f);
            if (tail != null) tail.body.ApplyTorque(tail.Mass * -Dir * (float)Math.Sin(walkPos) * 5.0f);

            walkPos += deltaTime * 5.0f;

            Vector2 centerOfMass = GetCenterOfMass();

            foreach (Limb limb in Limbs)
            {
                if (limb.type == LimbType.Head || limb.type == LimbType.Tail) continue;

                limb.body.ApplyForce((centerOfMass - limb.SimPosition) * (float)Math.Sin(walkPos) * limb.Mass * 10.0f);
            }
        }

        public override void Flip()
        {
            base.Flip();

            foreach (Limb l in Limbs)
            {
                if (!l.DoesFlip) continue;
                
                l.body.SetTransform(l.SimPosition,
                    -l.body.Rotation);                
            }
        }

        private void Mirror()
        {
            float leftX = Limbs[0].SimPosition.X, rightX = Limbs[0].SimPosition.X;
            for (int i = 1; i < Limbs.Count(); i++ )
            {
                if (Limbs[i].SimPosition.X < leftX)
                {
                    leftX = Limbs[i].SimPosition.X;
                }
                else if (Limbs[i].SimPosition.X > rightX)
                {
                    rightX = Limbs[i].SimPosition.X;
                }
            }

            float midX = GetCenterOfMass().X;

            foreach (Limb l in Limbs)
            {
                Vector2 newPos = new Vector2(midX - (l.SimPosition.X - midX), l.SimPosition.Y);

                if (Submarine.CheckVisibility(l.SimPosition, newPos)!=null)
                {
                    Vector2 diff = newPos - l.SimPosition;

                    l.body.SetTransform(
                        l.SimPosition + Submarine.LastPickedFraction * diff * 0.8f, l.body.Rotation);
                }
                else
                {
                    l.body.SetTransform(newPos, l.body.Rotation);
                }

                
            }
        }
  
    }
}
