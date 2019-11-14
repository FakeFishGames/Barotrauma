/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

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
using FarseerPhysics.Common;
using FarseerPhysics.Common.Maths;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Dynamics.Joints
{
    // Point-to-point constraint
    // C = p2 - p1
    // Cdot = v2 - v1
    //      = v2 + cross(w2, r2) - v1 - cross(w1, r1)
    // J = [-I -r1_skew I r2_skew ]
    // Identity used:
    // w k % (rx i + ry j) = w * (-ry i + rx j)

    // Angle constraint
    // C = angle2 - angle1 - referenceAngle
    // Cdot = w2 - w1
    // J = [0 0 -1 0 0 1]
    // K = invI1 + invI2

    /// <summary>
    /// A weld joint essentially glues two bodies together. A weld joint may
    /// distort somewhat because the island constraint solver is approximate.
    /// 
    /// The joint is soft constraint based, which means the two bodies will move
    /// relative to each other, when a force is applied. To combine two bodies
    /// in a rigid fashion, combine the fixtures to a single body instead.
    /// </summary>
    public class WeldJoint : Joint
    {
        // Solver shared
        private Vector3 _impulse;
        private float _gamma;
        private float _bias;

        // Solver temp
        private int _indexA;
        private int _indexB;
        private Vector2 _rA;
        private Vector2 _rB;
        private Vector2 _localCenterA;
        private Vector2 _localCenterB;
        private float _invMassA;
        private float _invMassB;
        private float _invIA;
        private float _invIB;
        private Mat33 _mass;

        internal WeldJoint()
        {
            JointType = JointType.Weld;
        }

        /// <summary>
        /// You need to specify an anchor point where they are attached.
        /// The position of the anchor point is important for computing the reaction torque.
        /// </summary>
        /// <param name="bodyA">The first body</param>
        /// <param name="bodyB">The second body</param>
        /// <param name="anchorA">The first body anchor.</param>
        /// <param name="anchorB">The second body anchor.</param>
        /// <param name="useWorldCoordinates">Set to true if you are using world coordinates as anchors.</param>
        public WeldJoint(Body bodyA, Body bodyB, Vector2 anchorA, Vector2 anchorB, bool useWorldCoordinates = false)
            : base(bodyA, bodyB)
        {
            JointType = JointType.Weld;

            if (useWorldCoordinates)
            {
                LocalAnchorA = bodyA.GetLocalPoint(anchorA);
                LocalAnchorB = bodyB.GetLocalPoint(anchorB);
            }
            else
            {
                LocalAnchorA = anchorA;
                LocalAnchorB = anchorB;
            }

            ReferenceAngle = BodyB.Rotation - BodyA.Rotation;
        }

        /// <summary>
        /// The local anchor point on BodyA
        /// </summary>
        public Vector2 LocalAnchorA { get; set; }

        /// <summary>
        /// The local anchor point on BodyB
        /// </summary>
        public Vector2 LocalAnchorB { get; set; }

        /// <summary>
        /// If true, body B is treated as if it was kinematic (i.e. as if it had infinite mass)
        /// </summary>
        public bool KinematicBodyB;

        public override Vector2 WorldAnchorA
        {
            get { return BodyA.GetWorldPoint(LocalAnchorA); }
            set { LocalAnchorA = BodyA.GetLocalPoint(value); }
        }

        public override Vector2 WorldAnchorB
        {
            get { return BodyB.GetWorldPoint(LocalAnchorB); }
            set { LocalAnchorB = BodyB.GetLocalPoint(value); }
        }

        /// <summary>
        /// The bodyB angle minus bodyA angle in the reference state (radians).
        /// </summary>
        public float ReferenceAngle { get; set; }

        /// <summary>
        /// The frequency of the joint. A higher frequency means a stiffer joint, but
        /// a too high value can cause the joint to oscillate.
        /// Default is 0, which means the joint does no spring calculations.
        /// </summary>
        public float FrequencyHz { get; set; }

        /// <summary>
        /// The damping on the joint. The damping is only used when
        /// the joint has a frequency (> 0). A higher value means more damping.
        /// </summary>
        public float DampingRatio { get; set; }

        public override Vector2 GetReactionForce(float invDt)
        {
            return invDt * new Vector2(_impulse.X, _impulse.Y);
        }

        public override float GetReactionTorque(float invDt)
        {
            return invDt * _impulse.Z;
        }

        internal override void InitVelocityConstraints(ref SolverData data)
        {
            _indexA = BodyA.IslandIndex;
            _indexB = BodyB.IslandIndex;
            _localCenterA = BodyA._sweep.LocalCenter;
            _localCenterB = BodyB._sweep.LocalCenter;
            _invMassA = BodyA._invMass;
            _invMassB = KinematicBodyB ? 0.0f : BodyB._invMass;
            _invIA = BodyA._invI;
            _invIB = BodyB._invI;
            _invIB = KinematicBodyB ? 0.0f : BodyB._invI;

            float aA = data.positions[_indexA].a;
            Vector2 vA = data.velocities[_indexA].v;
            float wA = data.velocities[_indexA].w;

            float aB = data.positions[_indexB].a;
            Vector2 vB = data.velocities[_indexB].v;
            float wB = data.velocities[_indexB].w;

            Complex qA = Complex.FromAngle(aA);
            Complex qB = Complex.FromAngle(aB);

            _rA = Complex.Multiply(LocalAnchorA - _localCenterA, ref qA);
            _rB = Complex.Multiply(LocalAnchorB - _localCenterB, ref qB);

            // J = [-I -r1_skew I r2_skew]
            //     [ 0       -1 0       1]
            // r_skew = [-ry; rx]

            // Matlab
            // K = [ mA+r1y^2*iA+mB+r2y^2*iB,  -r1y*iA*r1x-r2y*iB*r2x,          -r1y*iA-r2y*iB]
            //     [  -r1y*iA*r1x-r2y*iB*r2x, mA+r1x^2*iA+mB+r2x^2*iB,           r1x*iA+r2x*iB]
            //     [          -r1y*iA-r2y*iB,           r1x*iA+r2x*iB,                   iA+iB]

            float mA = _invMassA, mB = _invMassB;
            float iA = _invIA, iB = _invIB;

            Mat33 K = new Mat33();
            K.ex.X = mA + mB + _rA.Y * _rA.Y * iA + _rB.Y * _rB.Y * iB;
            K.ey.X = -_rA.Y * _rA.X * iA - _rB.Y * _rB.X * iB;
            K.ez.X = -_rA.Y * iA - _rB.Y * iB;
            K.ex.Y = K.ey.X;
            K.ey.Y = mA + mB + _rA.X * _rA.X * iA + _rB.X * _rB.X * iB;
            K.ez.Y = _rA.X * iA + _rB.X * iB;
            K.ex.Z = K.ez.X;
            K.ey.Z = K.ez.Y;
            K.ez.Z = iA + iB;

            if (FrequencyHz > 0.0f)
            {
                K.GetInverse22(ref _mass);

                float invM = iA + iB;
                float m = invM > 0.0f ? 1.0f / invM : 0.0f;

                float C = aB - aA - ReferenceAngle;

                // Frequency
                float omega = 2.0f * MathHelper.Pi * FrequencyHz;

                // Damping coefficient
                float d = 2.0f * m * DampingRatio * omega;

                // Spring stiffness
                float k = m * omega * omega;

                // magic formulas
                float h = data.step.dt;
                _gamma = h * (d + h * k);
                _gamma = _gamma != 0.0f ? 1.0f / _gamma : 0.0f;
                _bias = C * h * k * _gamma;

                invM += _gamma;
                _mass.ez.Z = invM != 0.0f ? 1.0f / invM : 0.0f;
            }
            else if (K.ez.Z == 0.0f)
            {
                K.GetInverse22(ref _mass);
                _gamma = 0.0f;
                _bias = 0.0f;
            }
            else
            {
                K.GetSymInverse33(ref _mass);
                _gamma = 0.0f;
                _bias = 0.0f;
            }

            if (data.step.warmStarting)
            {
                // Scale impulses to support a variable time step.
                _impulse *= data.step.dtRatio;

                Vector2 P = new Vector2(_impulse.X, _impulse.Y);

                vA -= mA * P;
                wA -= iA * (MathUtils.Cross(ref _rA, ref P) + _impulse.Z);

                vB += mB * P;
                wB += iB * (MathUtils.Cross(ref _rB, ref P) + _impulse.Z);
            }
            else
            {
                _impulse = Vector3.Zero;
            }

            data.velocities[_indexA].v = vA;
            data.velocities[_indexA].w = wA;
            data.velocities[_indexB].v = vB;
            data.velocities[_indexB].w = wB;
        }

        internal override void SolveVelocityConstraints(ref SolverData data)
        {
            Vector2 vA = data.velocities[_indexA].v;
            float wA = data.velocities[_indexA].w;
            Vector2 vB = data.velocities[_indexB].v;
            float wB = data.velocities[_indexB].w;

            float mA = _invMassA, mB = _invMassB;
            float iA = _invIA, iB = _invIB;

            if (FrequencyHz > 0.0f)
            {
                float Cdot2 = wB - wA;

                float impulse2 = -_mass.ez.Z * (Cdot2 + _bias + _gamma * _impulse.Z);
                _impulse.Z += impulse2;

                wA -= iA * impulse2;
                wB += iB * impulse2;

                Vector2 Cdot1 = vB + MathUtils.Cross(wB, ref _rB) - vA - MathUtils.Cross(wA, ref _rA);

                Vector2 impulse1 = -MathUtils.Mul22(_mass, Cdot1);
                _impulse.X += impulse1.X;
                _impulse.Y += impulse1.Y;

                Vector2 P = impulse1;

                vA -= mA * P;
                wA -= iA * MathUtils.Cross(ref _rA, ref P);

                vB += mB * P;
                wB += iB * MathUtils.Cross(ref _rB, ref P);
            }
            else
            {
                Vector2 Cdot1 = vB + MathUtils.Cross(wB, ref _rB) - vA - MathUtils.Cross(wA, ref _rA);
                float Cdot2 = wB - wA;
                Vector3 Cdot = new Vector3(Cdot1.X, Cdot1.Y, Cdot2);

                Vector3 impulse = -MathUtils.Mul(_mass, Cdot);
                _impulse += impulse;

                Vector2 P = new Vector2(impulse.X, impulse.Y);

                vA -= mA * P;
                wA -= iA * (MathUtils.Cross(ref _rA, ref P) + impulse.Z);

                vB += mB * P;
                wB += iB * (MathUtils.Cross(ref _rB, ref P) + impulse.Z);
            }

            data.velocities[_indexA].v = vA;
            data.velocities[_indexA].w = wA;
            data.velocities[_indexB].v = vB;
            data.velocities[_indexB].w = wB;
        }

        internal override bool SolvePositionConstraints(ref SolverData data)
        {
            Vector2 cA = data.positions[_indexA].c;
            float aA = data.positions[_indexA].a;
            Vector2 cB = data.positions[_indexB].c;
            float aB = data.positions[_indexB].a;

            Complex qA = Complex.FromAngle(aA);
            Complex qB = Complex.FromAngle(aB);

            float mA = _invMassA, mB = _invMassB;
            float iA = _invIA, iB = _invIB;

            Vector2 rA = Complex.Multiply(LocalAnchorA - _localCenterA, ref qA);
            Vector2 rB = Complex.Multiply(LocalAnchorB - _localCenterB, ref qB);

            float positionError, angularError;

            Mat33 K = new Mat33();
            K.ex.X = mA + mB + rA.Y * rA.Y * iA + rB.Y * rB.Y * iB;
            K.ey.X = -rA.Y * rA.X * iA - rB.Y * rB.X * iB;
            K.ez.X = -rA.Y * iA - rB.Y * iB;
            K.ex.Y = K.ey.X;
            K.ey.Y = mA + mB + rA.X * rA.X * iA + rB.X * rB.X * iB;
            K.ez.Y = rA.X * iA + rB.X * iB;
            K.ex.Z = K.ez.X;
            K.ey.Z = K.ez.Y;
            K.ez.Z = iA + iB;

            if (FrequencyHz > 0.0f)
            {
                Vector2 C1 = cB + rB - cA - rA;

                positionError = C1.Length();
                angularError = 0.0f;

                Vector2 P = -K.Solve22(C1);

                cA -= mA * P;
                aA -= iA * MathUtils.Cross(ref rA, ref P);

                cB += mB * P;
                aB += iB * MathUtils.Cross(ref rB, ref P);
            }
            else
            {
                Vector2 C1 = cB + rB - cA - rA;
                float C2 = aB - aA - ReferenceAngle;

                positionError = C1.Length();
                angularError = Math.Abs(C2);

                Vector3 C = new Vector3(C1.X, C1.Y, C2);

                Vector3 impulse;
                if (K.ez.Z <= 0.0f)
                {
                    Vector2 impulse2 = -K.Solve22(C1);
                    impulse = new Vector3(impulse2.X, impulse2.Y, 0.0f);
                }
                else 
                {
                    impulse = -K.Solve33(C);
                }
                Vector2 P = new Vector2(impulse.X, impulse.Y);

                cA -= mA * P;
                aA -= iA * (MathUtils.Cross(ref rA, ref P) + impulse.Z);

                cB += mB * P;
                aB += iB * (MathUtils.Cross(ref rB, ref P) + impulse.Z);
            }

            data.positions[_indexA].c = cA;
            data.positions[_indexA].a = aA;
            data.positions[_indexB].c = cB;
            data.positions[_indexB].a = aB;

            return positionError <= Settings.LinearSlop && angularError <= Settings.AngularSlop;
        }
    }
}