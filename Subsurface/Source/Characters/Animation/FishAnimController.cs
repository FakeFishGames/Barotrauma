using System;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    class FishAnimController : AnimController
    {
        //amplitude and wave length of the "sine wave" swimming animation
        //if amplitude = 0, sine wave animation isn't used
        private float waveAmplitude;
        private float waveLength;

        private bool rotateTowardsMovement;

        private bool mirror, flip;

        private float flipTimer;

        private float? footRotation;

        public FishAnimController(Character character, XElement element)
            : base(character, element)
        {
            waveAmplitude = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "waveamplitude", 0.0f));
            waveLength = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "wavelength", 0.0f));
            
            flip = ToolBox.GetAttributeBool(element, "flip", true);
            mirror = ToolBox.GetAttributeBool(element, "mirror", false);
            
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
                stunTimer -= deltaTime;
                return;
            }
            else
            {
                if (inWater)// || RefLimb.inWater)
                {
                    UpdateSineAnim(deltaTime);
                }
                else if (currentHull != null && CanEnterSubmarine)
                {
                    UpdateWalkAnim(deltaTime);
                }
            }

            if (mirror || !inWater)
            {
                if (!character.IsNetworkPlayer)
                {
                    //targetDir = (movement.X > 0.0f) ? Direction.Right : Direction.Left;
                    if (targetMovement.X > 0.1f && targetMovement.X > Math.Abs(targetMovement.Y) * 0.5f)
                    {
                        TargetDir = Direction.Right;
                    }
                    else if (targetMovement.X < -0.1f && targetMovement.X < -Math.Abs(targetMovement.Y) * 0.5f)
                    {
                        TargetDir = Direction.Left;
                    }
                }

            }
            else
            {
                Limb head = GetLimb(LimbType.Head);
                if (head == null) head = GetLimb(LimbType.Torso);

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
            
            if (!flip) return;

            flipTimer += deltaTime;
            
            if (TargetDir != dir) 
            {   
                if (flipTimer>1.0f || character.IsNetworkPlayer)
                {
                    Flip();
                    if (mirror || !inWater) Mirror();
                    flipTimer = 0.0f;
                }              
            }
        }

        void UpdateSineAnim(float deltaTime)
        {
            movement = TargetMovement*swimSpeed;

            Limb torso = GetLimb(LimbType.Torso);
            Limb head = GetLimb(LimbType.Head);

            Limb mainLimb = torso == null ? head : torso;

            mainLimb.pullJoint.Enabled = true;
            mainLimb.pullJoint.WorldAnchorB = collider.SimPosition;

            if (movement.LengthSquared() < 0.00001f) return;

            float movementAngle = MathUtils.VectorToAngle(movement) - MathHelper.PiOver2;

            float angle = (rotateTowardsMovement) ?
                mainLimb.body.Rotation + MathUtils.GetShortestAngle(mainLimb.body.Rotation, movementAngle) :
                HeadAngle * Dir;
            
            collider.SmoothRotate(angle, 25.0f);
            mainLimb.body.SmoothRotate(angle, 25.0f);

            Limb tail = GetLimb(LimbType.Tail);
            if (tail != null && waveAmplitude > 0.0f)
            {
                walkPos -= movement.Length();

                float waveRotation = (float)Math.Sin(walkPos / waveLength);

                tail.body.ApplyTorque(waveRotation * tail.Mass * 100.0f * waveAmplitude);
            }


            for (int i = 0; i < Limbs.Count(); i++)
            {
                Vector2 pullPos = Limbs[i].pullJoint == null ? Limbs[i].SimPosition : Limbs[i].pullJoint.WorldAnchorA;
                Limbs[i].body.ApplyForce(movement * Limbs[i].SteerForce * Limbs[i].Mass, pullPos);

                

                if (Limbs[i] == mainLimb) continue;

                float dist = (mainLimb.SimPosition - Limbs[i].SimPosition).Length();

                Vector2 limbPos = mainLimb.SimPosition - Vector2.Normalize(movement) * dist;

                Limbs[i].body.ApplyForce(((limbPos - Limbs[i].SimPosition) * 3.0f - Limbs[i].LinearVelocity * 3.0f) * Limbs[i].Mass);
            }
            
            collider.LinearVelocity = Vector2.Lerp(collider.LinearVelocity, movement, 0.5f);
                
            floorY = Limbs[0].SimPosition.Y;            
        }
            
        void UpdateWalkAnim(float deltaTime)
        {
            movement = MathUtils.SmoothStep(movement, TargetMovement * walkSpeed, 0.2f);
            if (movement == Vector2.Zero) return;

            IgnorePlatforms = (TargetMovement.Y < -Math.Abs(TargetMovement.X));

            Limb mainLimb;
            float mainLimbHeight, mainLimbAngle;

            Limb torso  = GetLimb(LimbType.Torso);
            Limb head   = GetLimb(LimbType.Head);

            if (torso != null)
            {
                mainLimb = torso;
                mainLimbHeight = TorsoPosition;
                mainLimbAngle = torsoAngle;
            }
            else
            {
                mainLimb = head;
                mainLimbHeight = HeadPosition;
                mainLimbAngle = headAngle;
            }

            //collider.SmoothRotate(TorsoAngle * Dir, 10.0f);
            mainLimb.body.SmoothRotate(mainLimbAngle * Dir, 50.0f);
            
            collider.LinearVelocity = new Vector2(
                movement.X,
                collider.LinearVelocity.Y > 0.0f ? collider.LinearVelocity.Y * 0.5f : collider.LinearVelocity.Y);

            mainLimb.MoveToPos(GetColliderBottom() + Vector2.UnitY * mainLimbHeight, 10.0f);
            
            mainLimb.pullJoint.Enabled = true;
            mainLimb.pullJoint.WorldAnchorB = GetColliderBottom() + Vector2.UnitY * mainLimbHeight;

            walkPos -= mainLimb.LinearVelocity.X * 0.05f;

            Vector2 transformedStepSize = new Vector2(
                (float)Math.Cos(walkPos) * stepSize.X * 3.0f,
                (float)Math.Sin(walkPos) * stepSize.Y * 2.0f);

            foreach (Limb limb in Limbs)
            {
                switch (limb.type)
                {
                    case LimbType.LeftFoot:
                    case LimbType.RightFoot:
                        Vector2 footPos = new Vector2(limb.SimPosition.X, mainLimb.SimPosition.Y - mainLimbHeight);

                        if (limb.RefJointIndex>-1)
                        {
                            RevoluteJoint refJoint = limbJoints[limb.RefJointIndex];
                            footPos.X = refJoint.WorldAnchorA.X;
                        }
                        footPos.X += limb.StepOffset.X * Dir;
                        footPos.Y += limb.StepOffset.Y;

                        if (limb.type == LimbType.LeftFoot)
                        {
                            limb.MoveToPos(footPos +new Vector2(
                                transformedStepSize.X + movement.X * 0.1f,
                                (transformedStepSize.Y > 0.0f) ? transformedStepSize.Y : 0.0f),
                            8.0f);
                        }
                        else if (limb.type == LimbType.RightFoot)
                        {
                            limb.MoveToPos(footPos + new Vector2(
                                -transformedStepSize.X + movement.X * 0.1f,
                                (-transformedStepSize.Y > 0.0f) ? -transformedStepSize.Y : 0.0f),
                            8.0f);
                        }

                        if (footRotation != null) limb.body.SmoothRotate((float)footRotation * Dir, 50.0f);

                        break;
                    case LimbType.LeftLeg:
                    case LimbType.RightLeg:
                        if (legTorque != 0.0f) limb.body.ApplyTorque(limb.Mass * legTorque * Dir);
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

        public override Vector2 EstimateCurrPosition(Vector2 prevPosition, float timePassed)
        {
            timePassed = MathHelper.Clamp(timePassed, 0.0f, 1.0f);

            Vector2 currPosition = prevPosition + targetMovement * timePassed;

            return currPosition;
        }

        private void Mirror()
        {
            Vector2 centerOfMass = GetCenterOfMass();

            foreach (Limb l in Limbs)
            {
                TrySetLimbPosition(l,
                    centerOfMass,
                    new Vector2(centerOfMass.X - (l.SimPosition.X - centerOfMass.X), l.SimPosition.Y),
                    true);
            }
        }
  
    }
}
