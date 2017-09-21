using FarseerPhysics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class FishAnimController : AnimController
    {
        //amplitude and wave length of the "sine wave" swimming animation
        //if amplitude = 0, sine wave animation isn't used
        private float waveAmplitude;
        private float waveLength;

        private float steerTorque;

        private bool rotateTowardsMovement;

        private bool mirror, flip;

        private float flipTimer;

        private float? footRotation;

        private float deathAnimTimer, deathAnimDuration = 5.0f;

        public FishAnimController(Character character, XElement element)
            : base(character, element)
        {
            waveAmplitude   = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "waveamplitude", 0.0f));
            waveLength      = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "wavelength", 0.0f));

            steerTorque     = ToolBox.GetAttributeFloat(element, "steertorque", 25.0f);
            
            flip            = ToolBox.GetAttributeBool(element, "flip", true);
            mirror          = ToolBox.GetAttributeBool(element, "mirror", false);
            
            float footRot = ToolBox.GetAttributeFloat(element, "footrotation", float.NaN);
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
            if (Frozen) return;

            if (character.IsDead || character.IsUnconscious || character.Stun > 0.0f)
            {
                Collider.FarseerBody.FixedRotation = false;

                if (character.IsRemotePlayer)
                {
                    MainLimb.pullJoint.WorldAnchorB = Collider.SimPosition;
                    MainLimb.pullJoint.Enabled = true;
                }
                else
                {
                    Collider.LinearVelocity = (MainLimb.SimPosition - Collider.SimPosition) * 60.0f;
                    Collider.SmoothRotate(MainLimb.Rotation);
                }

                if (character.IsDead && deathAnimTimer < deathAnimDuration)
                {
                    deathAnimTimer += deltaTime;
                    UpdateDying(deltaTime);
                }
                
                return;
            }

            //re-enable collider
            if (!Collider.FarseerBody.Enabled)
            {
                var lowestLimb = FindLowestLimb();

                Collider.SetTransform(new Vector2(
                    Collider.SimPosition.X,
                    Math.Max(lowestLimb.SimPosition.Y + (Collider.radius + Collider.height / 2), Collider.SimPosition.Y)),
                    0.0f);

                Collider.FarseerBody.Enabled = true;
            }

            ResetPullJoints();

            if (strongestImpact > 0.0f)
            {
                character.Stun = MathHelper.Clamp(strongestImpact * 0.5f, character.Stun, 5.0f);
                strongestImpact = 0.0f;
            }


            if (inWater)
            {
                Collider.FarseerBody.FixedRotation = false;
                UpdateSineAnim(deltaTime);
            }
            else if (currentHull != null && CanEnterSubmarine)
            {
                if (Math.Abs(MathUtils.GetShortestAngle(Collider.Rotation, 0.0f)) > 0.001f)
                {
                    //rotate collider back upright
                    Collider.AngularVelocity = MathUtils.GetShortestAngle(Collider.Rotation, 0.0f) * 60.0f;
                    Collider.FarseerBody.FixedRotation = false;
                }
                else
                {
                    Collider.FarseerBody.FixedRotation = true;
                }

                UpdateWalkAnim(deltaTime);
            }
            
            if (!character.IsRemotePlayer)
            {
                if (mirror || !inWater)
                {
                    if (targetMovement.X > 0.1f && targetMovement.X > Math.Abs(targetMovement.Y) * 0.5f)
                    {
                        TargetDir = Direction.Right;
                    }
                    else if (targetMovement.X < -0.1f && targetMovement.X < -Math.Abs(targetMovement.Y) * 0.5f)
                    {
                        TargetDir = Direction.Left;
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
            }

            if (!flip) return;

            flipTimer += deltaTime;

            if (TargetDir != Direction.None && TargetDir != dir) 
            {
                if (flipTimer > 1.0f || character.IsRemotePlayer)
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
            
            MainLimb.pullJoint.Enabled = true;
            MainLimb.pullJoint.WorldAnchorB = Collider.SimPosition;

            if (movement.LengthSquared() < 0.00001f) return;

            float movementAngle = MathUtils.VectorToAngle(movement) - MathHelper.PiOver2;
            
            if (rotateTowardsMovement)
            {
                Collider.SmoothRotate(movementAngle, 25.0f);
                MainLimb.body.SmoothRotate(movementAngle, steerTorque);
            }
            else
            {
                Collider.SmoothRotate(HeadAngle * Dir, 25.0f);
                MainLimb.body.SmoothRotate(HeadAngle * Dir, steerTorque);
            }

            Limb tail = GetLimb(LimbType.Tail);
            if (tail != null && waveAmplitude > 0.0f)
            {
                walkPos -= movement.Length();

                float waveRotation = (float)Math.Sin(walkPos / waveLength);

                tail.body.ApplyTorque(waveRotation * tail.Mass * 100.0f * waveAmplitude);
            }


            for (int i = 0; i < Limbs.Length; i++)
            {
                if (Limbs[i].SteerForce <= 0.0f) continue;

                Vector2 pullPos = Limbs[i].pullJoint == null ? Limbs[i].SimPosition : Limbs[i].pullJoint.WorldAnchorA;
                Limbs[i].body.ApplyForce(movement * Limbs[i].SteerForce * Limbs[i].Mass, pullPos);
            }
            
            Collider.LinearVelocity = Vector2.Lerp(Collider.LinearVelocity, movement, 0.5f);
                
            floorY = Limbs[0].SimPosition.Y;            
        }
            
        void UpdateWalkAnim(float deltaTime)
        {
            movement = MathUtils.SmoothStep(movement, TargetMovement * walkSpeed, 0.2f);
            
            float mainLimbHeight, mainLimbAngle;
            if (MainLimb.type == LimbType.Torso)
            {
                mainLimbHeight = TorsoPosition;
                mainLimbAngle = torsoAngle;
            }
            else
            {
                mainLimbHeight = HeadPosition;
                mainLimbAngle = headAngle;
            }

            MainLimb.body.SmoothRotate(mainLimbAngle * Dir, 50.0f);
            
            Collider.LinearVelocity = new Vector2(
                movement.X,
                Collider.LinearVelocity.Y > 0.0f ? Collider.LinearVelocity.Y * 0.5f : Collider.LinearVelocity.Y);

            MainLimb.MoveToPos(GetColliderBottom() + Vector2.UnitY * mainLimbHeight, 10.0f);
            
            MainLimb.pullJoint.Enabled = true;
            MainLimb.pullJoint.WorldAnchorB = GetColliderBottom() + Vector2.UnitY * mainLimbHeight;

            walkPos -= MainLimb.LinearVelocity.X * 0.05f;

            Vector2 transformedStepSize = new Vector2(
                (float)Math.Cos(walkPos) * stepSize.X * 3.0f,
                (float)Math.Sin(walkPos) * stepSize.Y * 2.0f);

            foreach (Limb limb in Limbs)
            {
                switch (limb.type)
                {
                    case LimbType.LeftFoot:
                    case LimbType.RightFoot:
                        Vector2 footPos = new Vector2(limb.SimPosition.X, MainLimb.SimPosition.Y - mainLimbHeight);

                        if (limb.RefJointIndex>-1)
                        {
                            RevoluteJoint refJoint = LimbJoints[limb.RefJointIndex];
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

            if (head != null && !head.IsSevered) head.body.ApplyTorque((float)(Math.Sqrt(head.Mass) * Dir * Math.Sin(walkPos)) * 10.0f);
            if (tail != null && !tail.IsSevered) tail.body.ApplyTorque((float)(Math.Sqrt(tail.Mass) * -Dir * (float)Math.Sin(walkPos)) * 10.0f);

            walkPos += deltaTime * 5.0f;

            Vector2 centerOfMass = GetCenterOfMass();

            foreach (Limb limb in Limbs)
            {
                if (limb.type == LimbType.Head || limb.type == LimbType.Tail || limb.IsSevered) continue;

                limb.body.ApplyForce((centerOfMass - limb.SimPosition) * (float)(Math.Sin(walkPos) * Math.Sqrt(limb.Mass)) * 10.0f);
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
