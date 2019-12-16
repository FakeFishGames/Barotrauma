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
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using FarseerPhysics.Common.Maths;

namespace FarseerPhysics.Common
{
    public static class MathUtils
    {
        public static float Cross(ref Vector2 a, ref Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return Cross(ref a, ref b);
        }

        /// Perform the cross product on two vectors.
        public static Vector3 Cross(ref Vector3 a, ref Vector3 b)
        {
            return new Vector3( a.Y * b.Z - a.Z * b.Y, 
                                a.Z * b.X - a.X * b.Z, 
                                a.X * b.Y - a.Y * b.X);
        }

        public static Vector2 Cross(Vector2 a, float s)
        {
            return new Vector2(s * a.Y, -s * a.X);
        }

        public static Vector2 Rot270(ref Vector2 a)
        {
            return new Vector2(a.Y, -a.X);
        }

        public static Vector2 Cross(float s, ref Vector2 a)
        {
            return new Vector2(-s * a.Y, s * a.X);
        }

        public static Vector2 Rot90(ref Vector2 a)
        {
            return new Vector2(-a.Y, a.X);
        }

        public static Vector2 Abs(Vector2 v)
        {
            return new Vector2(Math.Abs(v.X), Math.Abs(v.Y));
        }

        public static Vector2 Mul(ref Mat22 A, Vector2 v)
        {
            return Mul(ref A, ref v);
        }

        public static Vector2 Mul(ref Mat22 A, ref Vector2 v)
        {
            return new Vector2(A.ex.X * v.X + A.ey.X * v.Y, A.ex.Y * v.X + A.ey.Y * v.Y);
        }
        
        public static Vector2 MulT(ref Mat22 A, Vector2 v)
        {
            return MulT(ref A, ref v);
        }

        public static Vector2 MulT(ref Mat22 A, ref Vector2 v)
        {
            return new Vector2(v.X * A.ex.X + v.Y * A.ex.Y, v.X * A.ey.X + v.Y * A.ey.Y);
        }


        // A^T * B
        public static void MulT(ref Mat22 A, ref Mat22 B, out Mat22 C)
        {
            C = new Mat22();
            C.ex.X = A.ex.X * B.ex.X + A.ex.Y * B.ex.Y;
            C.ex.Y = A.ey.X * B.ex.X + A.ey.Y * B.ex.Y;
            C.ey.X = A.ex.X * B.ey.X + A.ex.Y * B.ey.Y;
            C.ey.Y = A.ey.X * B.ey.X + A.ey.Y * B.ey.Y;
        }

        /// Multiply a matrix times a vector.
        public static Vector3 Mul(Mat33 A, Vector3 v)
        {
            return v.X * A.ex + v.Y * A.ey + v.Z * A.ez;
        }
        
        public static void Swap<T>(ref T a, ref T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }

        /// Multiply a matrix times a vector.
        public static Vector2 Mul22(Mat33 A, Vector2 v)
        {
            return new Vector2(A.ex.X * v.X + A.ey.X * v.Y, A.ex.Y * v.X + A.ey.Y * v.Y);
        }
        
        /// Get the skew vector such that dot(skew_vec, other) == cross(vec, other)
        public static Vector2 Skew(Vector2 input)
        {
            return new Vector2(-input.Y, input.X);
        }

        /// <summary>
        /// This function is used to ensure that a floating point number is
        /// not a NaN or infinity.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <returns>
        /// 	<c>true</c> if the specified x is valid; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsValid(float x)
        {
            if (float.IsNaN(x))
            {
                // NaN.
                return false;
            }

            return !float.IsInfinity(x);
        }

        public static bool IsValid(this Vector2 x)
        {
            return IsValid(x.X) && IsValid(x.Y);
        }

        /// <summary>
        /// This is a approximate yet fast inverse square-root.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <returns></returns>
        public static float InvSqrt(float x)
        {
            FloatConverter convert = new FloatConverter();
            convert.x = x;
            float xhalf = 0.5f * x;
            convert.i = 0x5f3759df - (convert.i >> 1);
            x = convert.x;
            x = x * (1.5f - xhalf * x * x);
            return x;
        }

        public static int Clamp(int a, int low, int high)
        {
            return Math.Max(low, Math.Min(a, high));
        }

        public static float Clamp(float a, float low, float high)
        {
            return Math.Max(low, Math.Min(a, high));
        }

        public static Vector2 Clamp(Vector2 a, Vector2 low, Vector2 high)
        {
            return Vector2.Max(low, Vector2.Min(a, high));
        }

        public static void Cross(ref Vector2 a, ref Vector2 b, out float c)
        {
            c = a.X * b.Y - a.Y * b.X;
        }

        /// <summary>
        /// Return the angle between two vectors on a plane
        /// The angle is from vector 1 to vector 2, positive anticlockwise
        /// The result is between -pi -> pi
        /// </summary>
        public static double VectorAngle(ref Vector2 p1, ref Vector2 p2)
        {
            double theta1 = Math.Atan2(p1.Y, p1.X);
            double theta2 = Math.Atan2(p2.Y, p2.X);
            double dtheta = theta2 - theta1;
            while (dtheta > Math.PI)
                dtheta -= (2 * Math.PI);
            while (dtheta < -Math.PI)
                dtheta += (2 * Math.PI);

            return (dtheta);
        }

        /// Perform the dot product on two vectors.
        public static float Dot(Vector3 a, Vector3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// Perform the dot product on two vectors.
        public static float Dot(Vector2 a, ref Vector2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static double VectorAngle(Vector2 p1, Vector2 p2)
        {
            return VectorAngle(ref p1, ref p2);
        }

        /// <summary>
        /// Returns a positive number if c is to the left of the line going from a to b.
        /// </summary>
        /// <returns>Positive number if point is left, negative if point is right, 
        /// and 0 if points are collinear.</returns>
        public static float Area(Vector2 a, Vector2 b, Vector2 c)
        {
            return Area(ref a, ref b, ref c);
        }

        /// <summary>
        /// Returns a positive number if c is to the left of the line going from a to b.
        /// </summary>
        /// <returns>Positive number if point is left, negative if point is right, 
        /// and 0 if points are collinear.</returns>
        public static float Area(ref Vector2 a, ref Vector2 b, ref Vector2 c)
        {
            return a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y);
        }

        /// <summary>
        /// Determines if three vertices are collinear (ie. on a straight line)
        /// </summary>
        /// <param name="a">First vertex</param>
        /// <param name="b">Second vertex</param>
        /// <param name="c">Third vertex</param>
        /// <param name="tolerance">The tolerance</param>
        /// <returns></returns>
        public static bool IsCollinear(ref Vector2 a, ref Vector2 b, ref Vector2 c, float tolerance = 0)
        {
            return FloatInRange(Area(ref a, ref b, ref c), -tolerance, tolerance);
        }

        public static void Cross(float s, ref Vector2 a, out Vector2 b)
        {
            b = new Vector2(-s * a.Y, s * a.X);
        }

        public static bool FloatEquals(float value1, float value2)
        {
            return Math.Abs(value1 - value2) <= Settings.Epsilon;
        }

        /// <summary>
        /// Checks if a floating point Value is equal to another,
        /// within a certain tolerance.
        /// </summary>
        /// <param name="value1">The first floating point Value.</param>
        /// <param name="value2">The second floating point Value.</param>
        /// <param name="delta">The floating point tolerance.</param>
        /// <returns>True if the values are "equal", false otherwise.</returns>
        public static bool FloatEquals(float value1, float value2, float delta)
        {
            return FloatInRange(value1, value2 - delta, value2 + delta);
        }

        /// <summary>
        /// Checks if a floating point Value is within a specified
        /// range of values (inclusive).
        /// </summary>
        /// <param name="value">The Value to check.</param>
        /// <param name="min">The minimum Value.</param>
        /// <param name="max">The maximum Value.</param>
        /// <returns>True if the Value is within the range specified,
        /// false otherwise.</returns>
        public static bool FloatInRange(float value, float min, float max)
        {
            return (value >= min && value <= max);
        }

        #region Nested type: FloatConverter

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatConverter
        {
            [FieldOffset(0)]
            public float x;
            [FieldOffset(0)]
            public int i;
        }

        #endregion

    }

    /// <summary>
    /// A 2-by-2 matrix. Stored in column-major order.
    /// </summary>
    public struct Mat22
    {
        public Vector2 ex, ey;

        /// <summary>
        /// Construct this matrix using columns.
        /// </summary>
        /// <param name="c1">The c1.</param>
        /// <param name="c2">The c2.</param>
        public Mat22(Vector2 c1, Vector2 c2)
        {
            ex = c1;
            ey = c2;
        }

        /// <summary>
        /// Construct this matrix using scalars.
        /// </summary>
        /// <param name="a11">The a11.</param>
        /// <param name="a12">The a12.</param>
        /// <param name="a21">The a21.</param>
        /// <param name="a22">The a22.</param>
        public Mat22(float a11, float a12, float a21, float a22)
        {
            ex = new Vector2(a11, a21);
            ey = new Vector2(a12, a22);
        }

        public Mat22 Inverse
        {
            get
            {
                float a = ex.X, b = ey.X, c = ex.Y, d = ey.Y;
                float det = a * d - b * c;
                if (det != 0.0f)
                {
                    det = 1.0f / det;
                }

                Mat22 result = new Mat22();
                result.ex.X = det * d;
                result.ex.Y = -det * c;

                result.ey.X = -det * b;
                result.ey.Y = det * a;

                return result;
            }
        }

        /// <summary>
        /// Initialize this matrix using columns.
        /// </summary>
        /// <param name="c1">The c1.</param>
        /// <param name="c2">The c2.</param>
        public void Set(Vector2 c1, Vector2 c2)
        {
            ex = c1;
            ey = c2;
        }

        /// <summary>
        /// Set this to the identity matrix.
        /// </summary>
        public void SetIdentity()
        {
            ex.X = 1.0f;
            ey.X = 0.0f;
            ex.Y = 0.0f;
            ey.Y = 1.0f;
        }

        /// <summary>
        /// Set this matrix to all zeros.
        /// </summary>
        public void SetZero()
        {
            ex.X = 0.0f;
            ey.X = 0.0f;
            ex.Y = 0.0f;
            ey.Y = 0.0f;
        }

        /// <summary>
        /// Solve A * x = b, where b is a column vector. This is more efficient
        /// than computing the inverse in one-shot cases.
        /// </summary>
        /// <param name="b">The b.</param>
        /// <returns></returns>
        public Vector2 Solve(Vector2 b)
        {
            float a11 = ex.X, a12 = ey.X, a21 = ex.Y, a22 = ey.Y;
            float det = a11 * a22 - a12 * a21;
            if (det != 0.0f)
            {
                det = 1.0f / det;
            }

            return new Vector2(det * (a22 * b.X - a12 * b.Y), det * (a11 * b.Y - a21 * b.X));
        }

        public static void Add(ref Mat22 A, ref Mat22 B, out Mat22 R)
        {
            R.ex = A.ex + B.ex;
            R.ey = A.ey + B.ey;
        }
    }

    /// <summary>
    /// A 3-by-3 matrix. Stored in column-major order.
    /// </summary>
    public struct Mat33
    {
        public Vector3 ex, ey, ez;

        /// <summary>
        /// Construct this matrix using columns.
        /// </summary>
        /// <param name="c1">The c1.</param>
        /// <param name="c2">The c2.</param>
        /// <param name="c3">The c3.</param>
        public Mat33(Vector3 c1, Vector3 c2, Vector3 c3)
        {
            ex = c1;
            ey = c2;
            ez = c3;
        }

        /// <summary>
        /// Set this matrix to all zeros.
        /// </summary>
        public void SetZero()
        {
            ex = Vector3.Zero;
            ey = Vector3.Zero;
            ez = Vector3.Zero;
        }

        /// <summary>
        /// Solve A * x = b, where b is a column vector. This is more efficient
        /// than computing the inverse in one-shot cases.
        /// </summary>
        /// <param name="b">The b.</param>
        /// <returns></returns>
        public Vector3 Solve33(Vector3 b)
        {
            float det = Vector3.Dot(ex, Vector3.Cross(ey, ez));
            if (det != 0.0f)
            {
                det = 1.0f / det;
            }

            return new Vector3(det * Vector3.Dot(b, Vector3.Cross(ey, ez)), det * Vector3.Dot(ex, Vector3.Cross(b, ez)), det * Vector3.Dot(ex, Vector3.Cross(ey, b)));
        }

        /// <summary>
        /// Solve A * x = b, where b is a column vector. This is more efficient
        /// than computing the inverse in one-shot cases. Solve only the upper
        /// 2-by-2 matrix equation.
        /// </summary>
        /// <param name="b">The b.</param>
        /// <returns></returns>
        public Vector2 Solve22(Vector2 b)
        {
            float a11 = ex.X, a12 = ey.X, a21 = ex.Y, a22 = ey.Y;
            float det = a11 * a22 - a12 * a21;

            if (det != 0.0f)
            {
                det = 1.0f / det;
            }

            return new Vector2(det * (a22 * b.X - a12 * b.Y), det * (a11 * b.Y - a21 * b.X));
        }

        /// Get the inverse of this matrix as a 2-by-2.
        /// Returns the zero matrix if singular.
        public void GetInverse22(ref Mat33 M)
        {
            float a = ex.X, b = ey.X, c = ex.Y, d = ey.Y;
            float det = a * d - b * c;
            if (det != 0.0f)
            {
                det = 1.0f / det;
            }

            M.ex.X = det * d; M.ey.X = -det * b; M.ex.Z = 0.0f;
            M.ex.Y = -det * c; M.ey.Y = det * a; M.ey.Z = 0.0f;
            M.ez.X = 0.0f; M.ez.Y = 0.0f; M.ez.Z = 0.0f;
        }

        /// Get the symmetric inverse of this matrix as a 3-by-3.
        /// Returns the zero matrix if singular.
        public void GetSymInverse33(ref Mat33 M)
        {
            float det = MathUtils.Dot(ex, MathUtils.Cross(ref ey, ref ez));
            if (det != 0.0f)
            {
                det = 1.0f / det;
            }

            float a11 = ex.X, a12 = ey.X, a13 = ez.X;
            float a22 = ey.Y, a23 = ez.Y;
            float a33 = ez.Z;

            M.ex.X = det * (a22 * a33 - a23 * a23);
            M.ex.Y = det * (a13 * a23 - a12 * a33);
            M.ex.Z = det * (a12 * a23 - a13 * a22);

            M.ey.X = M.ex.Y;
            M.ey.Y = det * (a11 * a33 - a13 * a13);
            M.ey.Z = det * (a13 * a12 - a11 * a23);

            M.ez.X = M.ex.Z;
            M.ez.Y = M.ey.Z;
            M.ez.Z = det * (a11 * a22 - a12 * a12);
        }
    }

    
    /// <summary>
    /// A transform contains translation and rotation. It is used to represent
    /// the position and orientation of rigid frames.
    /// </summary>
    public struct Transform
    {
        private static readonly Transform _identity = new Transform(Vector2.Zero, Complex.One);

        public Complex q;
        public Vector2 p;

        public static Transform Identity { get { return _identity; } }

        /// <summary>
        /// Initialize using a position vector and a Complex rotation.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation</param>
        public Transform(Vector2 position, Complex rotation)
        {
            q = rotation;
            p = position;
        }

        /// <summary>
        /// Initialize using a position vector and a rotation.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="angle">The rotation angle</param>
        public Transform(Vector2 position, float angle)
            : this(position, Complex.FromAngle(angle))
        {
        }
                
        public static Vector2 Multiply(Vector2 left, ref Transform right)
        {
            return Multiply(ref left, ref right);
        }

        public static Vector2 Multiply(ref Vector2 left, ref Transform right)
        {
            // Opt: var result = Complex.Multiply(left, right.q) + right.p;
            return new Vector2(
                (left.X * right.q.Real - left.Y * right.q.Imaginary) + right.p.X,
                (left.Y * right.q.Real + left.X * right.q.Imaginary) + right.p.Y);
        }

        public static Vector2 Divide(Vector2 left, ref Transform right)
        {
            return Divide(ref left, ref right);
        }

        public static Vector2 Divide(ref Vector2 left, ref Transform right)
        {
            // Opt: var result = Complex.Divide(left - right.p, right);
            float px = left.X - right.p.X;
            float py = left.Y - right.p.Y;
            return new Vector2(
                (px * right.q.Real + py * right.q.Imaginary),
                (py * right.q.Real - px * right.q.Imaginary));
        }

        public static void Divide(Vector2 left, ref Transform right, out Vector2 result)
        {
            // Opt: var result = Complex.Divide(left - right.p, right);
            float px = left.X - right.p.X;
            float py = left.Y - right.p.Y;
            result.X = (px * right.q.Real + py * right.q.Imaginary);
            result.Y = (py * right.q.Real - px * right.q.Imaginary);
        }

        public static Transform Multiply(ref Transform left, ref Transform right)
        {
            return new Transform(
                    Complex.Multiply(ref left.p, ref right.q) + right.p,
                    Complex.Multiply(ref left.q, ref right.q));
        }

        public static Transform Divide(ref Transform left, ref Transform right)
        {
            return new Transform(
                Complex.Divide(left.p - right.p, ref right.q),
                Complex.Divide(ref left.q, ref right.q));
        }
        
        public static void Divide(ref Transform left, ref Transform right, out Transform result)
        {
            Complex.Divide(left.p - right.p, ref right.q, out result.p);
            Complex.Divide(ref left.q, ref right.q, out result.q);
        }
            
        public static void Multiply(ref Transform left, Complex right, out Transform result)
        {
            result.p = Complex.Multiply(ref left.p, ref right);
            result.q = Complex.Multiply(ref left.q, ref right);
        }
        
        public static void Divide(ref Transform left, Complex right, out Transform result)
        {
            result.p = Complex.Divide(ref left.p, ref right);
            result.q = Complex.Divide(ref left.q, ref right);
        }
    }

    /// <summary>
    /// This describes the motion of a body/shape for TOI computation.
    /// Shapes are defined with respect to the body origin, which may
    /// no coincide with the center of mass. However, to support dynamics
    /// we must interpolate the center of mass position.
    /// </summary>
    public struct Sweep
    {
        /// <summary>
        /// World angles
        /// </summary>
        public float A;

        public float A0;

        /// <summary>
        /// Fraction of the current time step in the range [0,1]
        /// c0 and a0 are the positions at alpha0.
        /// </summary>
        public float Alpha0;

        /// <summary>
        /// Center world positions
        /// </summary>
        public Vector2 C;

        public Vector2 C0;

        /// <summary>
        /// Local center of mass position
        /// </summary>
        public Vector2 LocalCenter;

        /// <summary>
        /// Get the interpolated transform at a specific time.
        /// </summary>
        /// <param name="xfb">The transform.</param>
        /// <param name="beta">beta is a factor in [0,1], where 0 indicates alpha0.</param>
        public void GetTransform(out Transform xfb, float beta)
        {
            xfb = new Transform();
            xfb.p.X = (1.0f - beta) * C0.X + beta * C.X;
            xfb.p.Y = (1.0f - beta) * C0.Y + beta * C.Y;
            float angle = (1.0f - beta) * A0 + beta * A;
            xfb.q.Phase = angle;

            // Shift to origin
            xfb.p -= Complex.Multiply(ref LocalCenter, ref xfb.q);
        }

        /// <summary>
        /// Advance the sweep forward, yielding a new initial state.
        /// </summary>
        /// <param name="alpha">new initial time..</param>
        public void Advance(float alpha)
        {
            Debug.Assert(Alpha0 < 1.0f);
            float beta = (alpha - Alpha0) / (1.0f - Alpha0);
            C0 += beta * (C - C0);
            A0 += beta * (A - A0);
            Alpha0 = alpha;
        }

        /// <summary>
        /// Normalize the angles.
        /// </summary>
        public void Normalize()
        {
            float d = MathHelper.TwoPi * (float)Math.Floor(A0 / MathHelper.TwoPi);
            A0 -= d;
            A -= d;
        }
    }
}