/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using Microsoft.Xna.Framework;
using FarseerPhysics.Common.PhysicsLogic;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Controllers
{
    public abstract class AbstractForceController : Controller
    {
        #region DecayModes enum

        /// <summary>
        /// Modes for Decay. Actual Decay must be implemented in inheriting 
        /// classes
        /// </summary>
        public enum DecayModes
        {
            None,
            Step,
            Linear,
            InverseSquare,
            Curve
        }

        #endregion

        #region ForceTypes enum

        /// <summary>
        /// Forcetypes are used in the decay math to properly get the distance.
        /// They are also used to draw a representation in DebugView
        /// </summary>
        public enum ForceTypes
        {
            Point,
            Line,
            Area
        }

        #endregion

        #region TimingModes enum

        /// <summary>
        /// Timing Modes
        /// Switched: Standard on/off mode using the baseclass enabled property
        /// Triggered: When the Trigger() method is called the force is active 
        /// for a specified Impulse Length
        /// Curve: Still to be defined. The basic idea is having a Trigger 
        /// combined with a curve for the strength
        /// </summary>
        public enum TimingModes
        {
            Switched,
            Triggered,
            Curve
        }

        #endregion

        /// <summary>
        /// Curve to be used for Decay in Curve mode
        /// </summary>
        public Curve DecayCurve;

        /// <summary>
        /// The Forcetype of the instance
        /// </summary>
        public ForceTypes ForceType;

        /// <summary>
        /// Provided for reuse to provide Variation functionality in 
        /// inheriting classes
        /// </summary>
        protected Random Randomize;

        /// <summary>
        /// Curve used by Curve Mode as an animated multiplier for the force 
        /// strength.
        /// Only positions between 0 and 1 are considered as that range is 
        /// stretched to have ImpulseLength.
        /// </summary>
        public Curve StrengthCurve;

        /// <summary>
        /// Constructor
        /// </summary>
        public AbstractForceController()
        {
            Strength = 1.0f;
            Position = new Vector2(0, 0);
            MaximumSpeed = 100.0f;
            TimingMode = TimingModes.Switched;
            ImpulseTime = 0.0f;
            ImpulseLength = 1.0f;
            Triggered = false;
            StrengthCurve = new Curve();
            Variation = 0.0f;
            Randomize = new Random(1234);
            DecayMode = DecayModes.None;
            DecayCurve = new Curve();
            DecayStart = 0.0f;
            DecayEnd = 0.0f;

            StrengthCurve.Keys.Add(new CurveKey(0, 5));
            StrengthCurve.Keys.Add(new CurveKey(0.1f, 5));
            StrengthCurve.Keys.Add(new CurveKey(0.2f, -4));
            StrengthCurve.Keys.Add(new CurveKey(1f, 0));
        }

        /// <summary>
        /// Overloaded Contstructor with supplying Timing Mode
        /// </summary>
        /// <param name="mode"></param>
        public AbstractForceController(TimingModes mode)
        {
            TimingMode = mode;
            switch (mode)
            {
                case TimingModes.Switched:
                    Enabled = true;
                    break;
                case TimingModes.Triggered:
                    Enabled = false;
                    break;
                case TimingModes.Curve:
                    Enabled = false;
                    break;
            }
        }

        /// <summary>
        /// Global Strength of the force to be applied
        /// </summary>
        public float Strength { get; set; }

        /// <summary>
        /// Position of the Force. Can be ignored (left at (0,0) for forces
        /// that are not position-dependent
        /// </summary>
        public Vector2 Position { get; set; }

        /// <summary>
        /// Maximum speed of the bodies. Bodies that are travelling faster are
        /// supposed to be ignored
        /// </summary>
        public float MaximumSpeed { get; set; }

        /// <summary>
        /// Maximum Force to be applied. As opposed to Maximum Speed this is 
        /// independent of the velocity of
        /// the affected body
        /// </summary>
        public float MaximumForce { get; set; }

        /// <summary>
        /// Timing Mode of the force instance
        /// </summary>
        public TimingModes TimingMode { get; set; }

        /// <summary>
        /// Time of the current impulse. Incremented in update till 
        /// ImpulseLength is reached
        /// </summary>
        public float ImpulseTime { get; private set; }

        /// <summary>
        /// Length of a triggered impulse. Used in both Triggered and Curve Mode
        /// </summary>
        public float ImpulseLength { get; set; }

        /// <summary>
        /// Indicating if we are currently during an Impulse 
        /// (Triggered and Curve Mode)
        /// </summary>
        public bool Triggered { get; private set; }

        /// <summary>
        /// Variation of the force applied to each body affected
        /// !! Must be used in inheriting classes properly !!
        /// </summary>
        public float Variation { get; set; }

        /// <summary>
        /// See DecayModes
        /// </summary>
        public DecayModes DecayMode { get; set; }

        /// <summary>
        /// Start of the distance based Decay. To set a non decaying area
        /// </summary>
        public float DecayStart { get; set; }

        /// <summary>
        /// Maximum distance a force should be applied
        /// </summary>
        public float DecayEnd { get; set; }

        /// <summary>
        /// Calculate the Decay for a given body. Meant to ease force 
        /// development and stick to the DRY principle and provide unified and 
        /// predictable decay math.
        /// </summary>
        /// <param name="body">The body to calculate decay for</param>
        /// <returns>A multiplier to multiply the force with to add decay 
        /// support in inheriting classes</returns>
        protected float GetDecayMultiplier(Body body)
        {
            //TODO: Consider ForceType in distance calculation!
            float distance = (body.Position - Position).Length();
            switch (DecayMode)
            {
                case DecayModes.None:
                    {
                        return 1.0f;
                    }
                case DecayModes.Step:
                    {
                        if (distance < DecayEnd)
                            return 1.0f;
                        else
                            return 0.0f;
                    }
                case DecayModes.Linear:
                    {
                        if (distance < DecayStart)
                            return 1.0f;
                        if (distance > DecayEnd)
                            return 0.0f;
                        return (DecayEnd - DecayStart / distance - DecayStart);
                    }
                case DecayModes.InverseSquare:
                    {
                        if (distance < DecayStart)
                            return 1.0f;
                        else
                            return 1.0f / ((distance - DecayStart) * (distance - DecayStart));
                    }
                case DecayModes.Curve:
                    {
                        if (distance < DecayStart)
                            return 1.0f;
                        else
                            return DecayCurve.Evaluate(distance - DecayStart);
                    }
                default:
                    return 1.0f;
            }
        }

        /// <summary>
        /// Triggers the trigger modes (Trigger and Curve)
        /// </summary>
        public void Trigger()
        {
            Triggered = true;
            ImpulseTime = 0;
        }

        /// <summary>
        /// Inherited from Controller
        /// Depending on the TimingMode perform timing logic and call ApplyForce()
        /// </summary>
        /// <param name="dt"></param>
        public override void Update(float dt)
        {
            switch (TimingMode)
            {
                case TimingModes.Switched:
                    {
                        if (Enabled)
                        {
                            ApplyForce(dt, Strength);
                        }
                        break;
                    }
                case TimingModes.Triggered:
                    {
                        if (Enabled && Triggered)
                        {
                            if (ImpulseTime < ImpulseLength)
                            {
                                ApplyForce(dt, Strength);
                                ImpulseTime += dt;
                            }
                            else
                            {
                                Triggered = false;
                            }
                        }
                        break;
                    }
                case TimingModes.Curve:
                    {
                        if (Enabled && Triggered)
                        {
                            if (ImpulseTime < ImpulseLength)
                            {
                                ApplyForce(dt, Strength * StrengthCurve.Evaluate(ImpulseTime));
                                ImpulseTime += dt;
                            }
                            else
                            {
                                Triggered = false;
                            }
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// Apply the force supplying strength (wich is modified in Update() 
        /// according to the TimingMode
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="strength">The strength</param>
        public abstract void ApplyForce(float dt, float strength);
    }
}