/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Dynamics.Joints
{
    /// <summary>
    /// An easy to use factory for using joints.
    /// </summary>
    public static class JointFactory
    {
        public static MotorJoint CreateMotorJoint(World world, Body bodyA, Body bodyB, bool useWorldCoordinates = false)
        {
            MotorJoint joint = new MotorJoint(bodyA, bodyB, useWorldCoordinates);
            world.Add(joint);
            return joint;
        }

        public static RevoluteJoint CreateRevoluteJoint(World world, Body bodyA, Body bodyB, Vector2 anchorA, Vector2 anchorB, bool useWorldCoordinates = false)
        {
            RevoluteJoint joint = new RevoluteJoint(bodyA, bodyB, anchorA, anchorB, useWorldCoordinates);
            world.Add(joint);
            return joint;
        }

        public static RevoluteJoint CreateRevoluteJoint(World world, Body bodyA, Body bodyB, Vector2 anchor)
        {
            Vector2 localanchorA = bodyA.GetLocalPoint(bodyB.GetWorldPoint(anchor));
            RevoluteJoint joint = new RevoluteJoint(bodyA, bodyB, localanchorA, anchor);
            world.Add(joint);
            return joint;
        }

        public static RopeJoint CreateRopeJoint(World world, Body bodyA, Body bodyB, Vector2 anchorA, Vector2 anchorB, bool useWorldCoordinates = false)
        {
            RopeJoint ropeJoint = new RopeJoint(bodyA, bodyB, anchorA, anchorB, useWorldCoordinates);
            world.Add(ropeJoint);
            return ropeJoint;
        }

        public static WeldJoint CreateWeldJoint(World world, Body bodyA, Body bodyB, Vector2 anchorA, Vector2 anchorB, bool useWorldCoordinates = false)
        {
            WeldJoint weldJoint = new WeldJoint(bodyA, bodyB, anchorA, anchorB, useWorldCoordinates);
            world.Add(weldJoint);
            return weldJoint;
        }

        public static PrismaticJoint CreatePrismaticJoint(World world, Body bodyA, Body bodyB, Vector2 anchor, Vector2 axis, bool useWorldCoordinates = false)
        {
            PrismaticJoint joint = new PrismaticJoint(bodyA, bodyB, anchor, axis, useWorldCoordinates);
            world.Add(joint);
            return joint;
        }

        public static WheelJoint CreateWheelJoint(World world, Body bodyA, Body bodyB, Vector2 anchor, Vector2 axis, bool useWorldCoordinates = false)
        {
            WheelJoint joint = new WheelJoint(bodyA, bodyB, anchor, axis, useWorldCoordinates);
            world.Add(joint);
            return joint;
        }

        public static WheelJoint CreateWheelJoint(World world, Body bodyA, Body bodyB, Vector2 axis)
        {
            return CreateWheelJoint(world, bodyA, bodyB, Vector2.Zero, axis);
        }

        public static AngleJoint CreateAngleJoint(World world, Body bodyA, Body bodyB)
        {
            AngleJoint angleJoint = new AngleJoint(bodyA, bodyB);
            world.Add(angleJoint);
            return angleJoint;
        }

        public static DistanceJoint CreateDistanceJoint(World world, Body bodyA, Body bodyB, Vector2 anchorA, Vector2 anchorB, bool useWorldCoordinates = false)
        {
            DistanceJoint distanceJoint = new DistanceJoint(bodyA, bodyB, anchorA, anchorB, useWorldCoordinates);
            world.Add(distanceJoint);
            return distanceJoint;
        }

        public static DistanceJoint CreateDistanceJoint(World world, Body bodyA, Body bodyB)
        {
            return CreateDistanceJoint(world, bodyA, bodyB, Vector2.Zero, Vector2.Zero);
        }

        public static FrictionJoint CreateFrictionJoint(World world, Body bodyA, Body bodyB, Vector2 anchor, bool useWorldCoordinates = false)
        {
            FrictionJoint frictionJoint = new FrictionJoint(bodyA, bodyB, anchor, useWorldCoordinates);
            world.Add(frictionJoint);
            return frictionJoint;
        }

        public static FrictionJoint CreateFrictionJoint(World world, Body bodyA, Body bodyB)
        {
            return CreateFrictionJoint(world, bodyA, bodyB, Vector2.Zero);
        }

        public static GearJoint CreateGearJoint(World world, Body bodyA, Body bodyB, Joint jointA, Joint jointB, float ratio)
        {
            GearJoint gearJoint = new GearJoint(bodyA, bodyB, jointA, jointB, ratio);
            world.Add(gearJoint);
            return gearJoint;
        }

        public static PulleyJoint CreatePulleyJoint(World world, Body bodyA, Body bodyB, Vector2 anchorA, Vector2 anchorB, Vector2 worldAnchorA, Vector2 worldAnchorB, float ratio, bool useWorldCoordinates = false)
        {
            PulleyJoint pulleyJoint = new PulleyJoint(bodyA, bodyB, anchorA, anchorB, worldAnchorA, worldAnchorB, ratio, useWorldCoordinates);
            world.Add(pulleyJoint);
            return pulleyJoint;
        }

        public static FixedMouseJoint CreateFixedMouseJoint(World world, Body body, Vector2 worldAnchor)
        {
            FixedMouseJoint joint = new FixedMouseJoint(body, worldAnchor);
            world.Add(joint);
            return joint;
        }
    }
}