/*
* Farseer Physics Engine:
* Copyright (c) 2012 Ian Qvist
* 
* Original source Box2D:
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org 
* 
* This software is provided 'as-is', without any express or implied 
* warranty.  In no event will the authors be held liable for any damages 
* arising from the use of this software. 
* Permission is granted to anyone to use this software for any purpose, 
* including commercial applications, and to alter it and redistribute it 
* freely, subject to the following restrictions: 
* 1. The origin of this software must not be misrepresented; you must not 
* claim that you wrote the original software. If you use this software 
* in a product, an acknowledgment in the product documentation would be 
* appreciated but is not required. 
* 2. Altered source versions must be plainly marked as such, and must not be 
* misrepresented as being the original software. 
* 3. This notice may not be removed or altered from any source distribution. 
*/

using System;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics
{
    public static class Settings
    {
        public const float MaxFloat = 3.402823466e+38f;
        public const float Epsilon = 1.192092896e-07f;
        public const float Pi = 3.14159265359f;

        // Common

        /// <summary>
        /// If true, all collision callbacks have to return the same value, and agree
        /// if there was a collision or not. Swtich this to false to revert to the 
        /// collision agreement used in FPE 3.3.x
        /// </summary>
        public const bool AllCollisionCallbacksAgree = true;

        /// <summary>
        /// Enabling diagnistics causes the engine to gather timing information.
        /// You can see how much time it took to solve the contacts, solve CCD
        /// and update the controllers.
        /// NOTE: If you are using a debug view that shows performance counters,
        /// you might want to enable this.
        /// </summary>
        public const bool EnableDiagnostics = true;

        /// <summary>
        /// Set this to true to skip sanity checks in the engine. This will speed up the
        /// tools by removing the overhead of the checks, but you will need to handle checks
        /// yourself where it is needed.
        /// </summary>
        public const bool SkipSanityChecks = false;

        /// <summary>
        /// The number of velocity iterations used in the solver.
        /// </summary>
        public static int VelocityIterations = 8;

        /// <summary>
        /// The number of position iterations used in the solver.
        /// </summary>
        public static int PositionIterations = 3;

        /// <summary>
        /// Enable/Disable Continuous Collision Detection (CCD)
        /// </summary>
        public static bool ContinuousPhysics = true;

        /// <summary>
        /// If true, it will run a GiftWrap convex hull on all polygon inputs.
        /// This makes for a more stable engine when given random input,
        /// but if speed of the creation of polygons are more important,
        /// you might want to set this to false.
        /// </summary>
        public static bool UseConvexHullPolygons = true;

        /// <summary>
        /// The number of velocity iterations in the TOI solver
        /// </summary>
        public static int TOIVelocityIterations = VelocityIterations;

        /// <summary>
        /// The number of position iterations in the TOI solver
        /// </summary>
        public static int TOIPositionIterations = 20;

        /// <summary>
        /// Maximum number of sub-steps per contact in continuous physics simulation.
        /// </summary>
        public const int MaxSubSteps = 8;

        /// <summary>
        /// Enable/Disable warmstarting
        /// </summary>
        public const bool EnableWarmstarting = true;

        /// <summary>
        /// Enable/Disable sleeping
        /// </summary>
        public static bool AllowSleep = true;

        /// <summary>
        /// The maximum number of vertices on a convex polygon.
        /// </summary>
        public static int MaxPolygonVertices = 8;

        /// <summary>
        /// Farseer Physics Engine has a different way of filtering fixtures than Box2d.
        /// We have both FPE and Box2D filtering in the engine. If you are upgrading
        /// from earlier versions of FPE, set this to true and DefaultFixtureCollisionCategories
        /// to Category.All.
        /// </summary>
        public static bool UseFPECollisionCategories;

        /// <summary>
        /// This is used by the Fixture constructor as the default value 
        /// for Fixture.CollisionCategories member. Note that you may need to change this depending
        /// on the setting of UseFPECollisionCategories, above.
        /// </summary>
        public static Category DefaultFixtureCollisionCategories = Category.Cat1;

        /// <summary>
        /// This is used by the Fixture constructor as the default value 
        /// for Fixture.CollidesWith member.
        /// </summary>
        public static Category DefaultFixtureCollidesWith = Category.All;


        /// <summary>
        /// This is used by the Fixture constructor as the default value 
        /// for Fixture.IgnoreCCDWith member.
        /// </summary>
        public static Category DefaultFixtureIgnoreCCDWith = Category.None;

        /// <summary>
        /// The maximum number of contact points between two convex shapes.
        /// DO NOT CHANGE THIS VALUE!
        /// </summary>
        public const int MaxManifoldPoints = 2;

        /// <summary>
        /// This is used to fatten AABBs in the dynamic tree. This allows proxies
        /// to move by a small amount without triggering a tree adjustment.
        /// This is in meters.
        /// </summary>
        public const float AABBExtension = 0.1f;

        /// <summary>
        /// This is used to fatten AABBs in the dynamic tree. This is used to predict
        /// the future position based on the current displacement.
        /// This is a dimensionless multiplier.
        /// </summary>
        public const float AABBMultiplier = 2.0f;

        /// <summary>
        /// A small length used as a collision and constraint tolerance. Usually it is
        /// chosen to be numerically significant, but visually insignificant.
        /// </summary>
        public const float LinearSlop = 0.005f;

        /// <summary>
        /// A small angle used as a collision and constraint tolerance. Usually it is
        /// chosen to be numerically significant, but visually insignificant.
        /// </summary>
        public const float AngularSlop = (2.0f / 180.0f * Pi);

        /// <summary>
        /// The radius of the polygon/edge shape skin. This should not be modified. Making
        /// this smaller means polygons will have an insufficient buffer for continuous collision.
        /// Making it larger may create artifacts for vertex collision.
        /// </summary>
        public const float PolygonRadius = (2.0f * LinearSlop);

        // Dynamics

        /// <summary>
        /// Maximum number of contacts to be handled to solve a TOI impact.
        /// </summary>
        public const int MaxTOIContacts = 32;

        /// <summary>
        /// A velocity threshold for elastic collisions. Any collision with a relative linear
        /// velocity below this threshold will be treated as inelastic.
        /// </summary>
        public const float VelocityThreshold = 1.0f;

        /// <summary>
        /// The maximum linear position correction used when solving constraints. This helps to
        /// prevent overshoot.
        /// </summary>
        public const float MaxLinearCorrection = 0.2f;

        /// <summary>
        /// The maximum angular position correction used when solving constraints. This helps to
        /// prevent overshoot.
        /// </summary>
        public const float MaxAngularCorrection = (8.0f / 180.0f * Pi);

        /// <summary>
        /// This scale factor controls how fast overlap is resolved. Ideally this would be 1 so
        /// that overlap is removed in one time step. However using values close to 1 often lead
        /// to overshoot.
        /// </summary>
        public const float Baumgarte = 0.2f;

        // Sleep
        /// <summary>
        /// The time that a body must be still before it will go to sleep.
        /// </summary>
        public const float TimeToSleep = 0.5f;

        /// <summary>
        /// A body cannot sleep if its linear velocity is above this tolerance.
        /// </summary>
        public const float LinearSleepTolerance = 0.01f;

        /// <summary>
        /// A body cannot sleep if its angular velocity is above this tolerance.
        /// </summary>
        public const float AngularSleepTolerance = (2.0f / 180.0f * Pi);

        /// <summary>
        /// The maximum linear velocity of a body. This limit is very large and is used
        /// to prevent numerical problems. You shouldn't need to adjust this.
        /// </summary>
        public const float MaxTranslation = 2.0f;

        public const float MaxTranslationSquared = (MaxTranslation * MaxTranslation);

        /// <summary>
        /// The maximum angular velocity of a body. This limit is very large and is used
        /// to prevent numerical problems. You shouldn't need to adjust this.
        /// </summary>
        public const float MaxRotation = (0.5f * Pi);

        public const float MaxRotationSquared = (MaxRotation * MaxRotation);

        /// <summary>
        /// Defines the maximum number of iterations made by the GJK algorithm.
        /// </summary>
        public const int MaxGJKIterations = 20;

        /// <summary>
        /// This is only for debugging the solver
        /// </summary>
        public const bool EnableSubStepping = false;

        /// <summary>
        /// By default, forces are cleared automatically after each call to Step.
        /// The default behavior is modified with this setting.
        /// The purpose of this setting is to support sub-stepping. Sub-stepping is often used to maintain
        /// a fixed sized time step under a variable frame-rate.
        /// When you perform sub-stepping you should disable auto clearing of forces and instead call
        /// ClearForces after all sub-steps are complete in one pass of your game loop.
        /// </summary>
        public const bool AutoClearForces = true;

        /// <summary>
        /// Friction mixing law. Feel free to customize this.
        /// </summary>
        /// <param name="friction1">The friction1.</param>
        /// <param name="friction2">The friction2.</param>
        /// <returns></returns>
        public static float MixFriction(float friction1, float friction2)
        {
            return (float)Math.Sqrt(friction1 * friction2);
        }

        /// <summary>
        /// Restitution mixing law. Feel free to customize this.
        /// </summary>
        /// <param name="restitution1">The restitution1.</param>
        /// <param name="restitution2">The restitution2.</param>
        /// <returns></returns>
        public static float MixRestitution(float restitution1, float restitution2)
        {
            return restitution1 > restitution2 ? restitution1 : restitution2;
        }
    }
}