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
using FarseerPhysics.Common;
using FarseerPhysics.Common.Maths;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Collision
{
    /// <summary>
    /// Input parameters for CalculateTimeOfImpact
    /// </summary>
    public class TOIInput
    {
        public DistanceProxy ProxyA;
        public DistanceProxy ProxyB;
        public Sweep SweepA;
        public Sweep SweepB;
        public float TMax; // defines sweep interval [0, tMax]
    }

    public enum TOIOutputState
    {
        Unknown,
        Failed,
        Overlapped,
        Touching,
        Seperated,
    }

    public struct TOIOutput
    {
        public TOIOutputState State;
        public float T;
    }

    public enum SeparationFunctionType
    {
        Points,
        FaceA,
        FaceB
    }

    public static class SeparationFunction
    {
        [ThreadStatic]
        private static Vector2 _axis;
        [ThreadStatic]
        private static Vector2 _localPoint;
        [ThreadStatic]
        private static DistanceProxy _proxyA;
        [ThreadStatic]
        private static DistanceProxy _proxyB;
        [ThreadStatic]
        private static Sweep _sweepA, _sweepB;
        [ThreadStatic]
        private static SeparationFunctionType _type;

        public static void Set(ref SimplexCache cache, ref DistanceProxy proxyA, ref Sweep sweepA, ref DistanceProxy proxyB, ref Sweep sweepB, float t1)
        {
            _localPoint = Vector2.Zero;
            _proxyA = proxyA;
            _proxyB = proxyB;
            int count = cache.Count;
            Debug.Assert(0 < count && count < 3);

            _sweepA = sweepA;
            _sweepB = sweepB;

            Transform xfA, xfB;
            _sweepA.GetTransform(out xfA, t1);
            _sweepB.GetTransform(out xfB, t1);

            if (count == 1)
            {
                _type = SeparationFunctionType.Points;
                Vector2 localPointA = _proxyA.Vertices[cache.IndexA[0]];
                Vector2 localPointB = _proxyB.Vertices[cache.IndexB[0]];
                Vector2 pointA = Transform.Multiply(ref localPointA, ref xfA);
                Vector2 pointB = Transform.Multiply(ref localPointB, ref xfB);
                _axis = pointB - pointA;
                _axis.Normalize();
            }
            else if (cache.IndexA[0] == cache.IndexA[1])
            {
                // Two points on B and one on A.
                _type = SeparationFunctionType.FaceB;
                Vector2 localPointB1 = proxyB.Vertices[cache.IndexB[0]];
                Vector2 localPointB2 = proxyB.Vertices[cache.IndexB[1]];

                Vector2 a = localPointB2 - localPointB1;
                _axis = new Vector2(a.Y, -a.X);
                _axis.Normalize();
                Vector2 normal = Complex.Multiply(ref _axis, ref xfB.q);

                _localPoint = 0.5f * (localPointB1 + localPointB2);
                Vector2 pointB = Transform.Multiply(ref _localPoint, ref xfB);

                Vector2 localPointA = proxyA.Vertices[cache.IndexA[0]];
                Vector2 pointA = Transform.Multiply(ref localPointA, ref xfA);

                float s = Vector2.Dot(pointA - pointB, normal);
                if (s < 0.0f)
                {
                    _axis = -_axis;
                }
            }
            else
            {
                // Two points on A and one or two points on B.
                _type = SeparationFunctionType.FaceA;
                Vector2 localPointA1 = _proxyA.Vertices[cache.IndexA[0]];
                Vector2 localPointA2 = _proxyA.Vertices[cache.IndexA[1]];

                Vector2 a = localPointA2 - localPointA1;
                _axis = new Vector2(a.Y, -a.X);
                _axis.Normalize();
                Vector2 normal = Complex.Multiply(ref _axis, ref xfA.q);

                _localPoint = 0.5f * (localPointA1 + localPointA2);
                Vector2 pointA = Transform.Multiply(ref _localPoint, ref xfA);

                Vector2 localPointB = _proxyB.Vertices[cache.IndexB[0]];
                Vector2 pointB = Transform.Multiply(ref localPointB, ref xfB);

                float s = Vector2.Dot(pointB - pointA, normal);
                if (s < 0.0f)
                {
                    _axis = -_axis;
                }
            }
        }

        public static float FindMinSeparation(out int indexA, out int indexB, float t)
        {
            Transform xfA, xfB;
            _sweepA.GetTransform(out xfA, t);
            _sweepB.GetTransform(out xfB, t);

            switch (_type)
            {
                case SeparationFunctionType.Points:
                    {
                        Vector2 axisA =  Complex.Divide(ref _axis, ref xfA.q);
                        Vector2 axisB = -Complex.Divide(ref _axis, ref xfB.q);

                        indexA = _proxyA.GetSupport(axisA);
                        indexB = _proxyB.GetSupport(axisB);

                        Vector2 localPointA = _proxyA.Vertices[indexA];
                        Vector2 localPointB = _proxyB.Vertices[indexB];

                        Vector2 pointA = Transform.Multiply(ref localPointA, ref xfA);
                        Vector2 pointB = Transform.Multiply(ref localPointB, ref xfB);

                        float separation = Vector2.Dot(pointB - pointA, _axis);
                        return separation;
                    }

                case SeparationFunctionType.FaceA:
                    {
                        Vector2 normal = Complex.Multiply(ref _axis, ref xfA.q);
                        Vector2 pointA = Transform.Multiply(ref _localPoint, ref xfA);

                        Vector2 axisB = -Complex.Divide(ref normal, ref xfB.q);

                        indexA = -1;
                        indexB = _proxyB.GetSupport(axisB);

                        Vector2 localPointB = _proxyB.Vertices[indexB];
                        Vector2 pointB = Transform.Multiply(ref localPointB, ref xfB);

                        float separation = Vector2.Dot(pointB - pointA, normal);
                        return separation;
                    }

                case SeparationFunctionType.FaceB:
                    {
                        Vector2 normal = Complex.Multiply(ref _axis, ref xfB.q);
                        Vector2 pointB = Transform.Multiply(ref _localPoint, ref xfB);

                        Vector2 axisA = -Complex.Divide(ref normal, ref xfA.q);

                        indexB = -1;
                        indexA = _proxyA.GetSupport(axisA);

                        Vector2 localPointA = _proxyA.Vertices[indexA];
                        Vector2 pointA = Transform.Multiply(ref localPointA, ref xfA);

                        float separation = Vector2.Dot(pointA - pointB, normal);
                        return separation;
                    }

                default:
                    Debug.Assert(false);
                    indexA = -1;
                    indexB = -1;
                    return 0.0f;
            }
        }

        public static float Evaluate(int indexA, int indexB, float t)
        {
            Transform xfA, xfB;
            _sweepA.GetTransform(out xfA, t);
            _sweepB.GetTransform(out xfB, t);

            switch (_type)
            {
                case SeparationFunctionType.Points:
                    {
                        Vector2 localPointA = _proxyA.Vertices[indexA];
                        Vector2 localPointB = _proxyB.Vertices[indexB];

                        Vector2 pointA = Transform.Multiply(ref localPointA, ref xfA);
                        Vector2 pointB = Transform.Multiply(ref localPointB, ref xfB);
                        float separation = Vector2.Dot(pointB - pointA, _axis);

                        return separation;
                    }
                case SeparationFunctionType.FaceA:
                    {
                        Vector2 normal = Complex.Multiply(ref _axis, ref xfA.q);
                        Vector2 pointA = Transform.Multiply(ref _localPoint, ref xfA);

                        Vector2 localPointB = _proxyB.Vertices[indexB];
                        Vector2 pointB = Transform.Multiply(ref localPointB, ref xfB);

                        float separation = Vector2.Dot(pointB - pointA, normal);
                        return separation;
                    }
                case SeparationFunctionType.FaceB:
                    {
                        Vector2 normal = Complex.Multiply(ref _axis, ref xfB.q);
                        Vector2 pointB = Transform.Multiply(ref _localPoint, ref xfB);

                        Vector2 localPointA = _proxyA.Vertices[indexA];
                        Vector2 pointA = Transform.Multiply(ref localPointA, ref xfA);

                        float separation = Vector2.Dot(pointA - pointB, normal);
                        return separation;
                    }
                default:
                    Debug.Assert(false);
                    return 0.0f;
            }
        }
    }

    public static class TimeOfImpact
    {
        // CCD via the local separating axis method. This seeks progression
        // by computing the largest time at which separation is maintained.

        [ThreadStatic]
        public static int TOICalls, TOIIters, TOIMaxIters;
        [ThreadStatic]
        public static int TOIRootIters, TOIMaxRootIters;

        /// <summary>
        /// Compute the upper bound on time before two shapes penetrate. Time is represented as
        /// a fraction between [0,tMax]. This uses a swept separating axis and may miss some intermediate,
        /// non-tunneling collision. If you change the time interval, you should call this function
        /// again.
        /// Note: use Distance() to compute the contact point and normal at the time of impact.
        /// </summary>
        /// <param name="output">The output.</param>
        /// <param name="input">The input.</param>
        public static void CalculateTimeOfImpact(out TOIOutput output, ref TOIInput input)
        {
            if (Settings.EnableDiagnostics) //FPE: We only gather diagnostics when enabled
                ++TOICalls;

            output = new TOIOutput();
            output.State = TOIOutputState.Unknown;
            output.T = input.TMax;

            Sweep sweepA = input.SweepA;
            Sweep sweepB = input.SweepB;

            // Large rotations can make the root finder fail, so we normalize the
            // sweep angles.
            sweepA.Normalize();
            sweepB.Normalize();

            float tMax = input.TMax;

            float totalRadius = input.ProxyA.Radius + input.ProxyB.Radius;
            float target = Math.Max(Settings.LinearSlop, totalRadius - 3.0f * Settings.LinearSlop);
            const float tolerance = 0.25f * Settings.LinearSlop;
            Debug.Assert(target > tolerance);

            float t1 = 0.0f;
            const int k_maxIterations = 20;
            int iter = 0;

            // Prepare input for distance query.
            DistanceInput distanceInput = new DistanceInput();
            distanceInput.ProxyA = input.ProxyA;
            distanceInput.ProxyB = input.ProxyB;
            distanceInput.UseRadii = false;

            // The outer loop progressively attempts to compute new separating axes.
            // This loop terminates when an axis is repeated (no progress is made).
            for (; ; )
            {
                Transform xfA, xfB;
                sweepA.GetTransform(out xfA, t1);
                sweepB.GetTransform(out xfB, t1);

                // Get the distance between shapes. We can also use the results
                // to get a separating axis.
                distanceInput.TransformA = xfA;
                distanceInput.TransformB = xfB;
                DistanceOutput distanceOutput;
                SimplexCache cache;
                Distance.ComputeDistance(out distanceOutput, out cache, distanceInput);

                // If the shapes are overlapped, we give up on continuous collision.
                if (distanceOutput.Distance <= 0.0f)
                {
                    // Failure!
                    output.State = TOIOutputState.Overlapped;
                    output.T = 0.0f;
                    break;
                }

                if (distanceOutput.Distance < target + tolerance)
                {
                    // Victory!
                    output.State = TOIOutputState.Touching;
                    output.T = t1;
                    break;
                }

                SeparationFunction.Set(ref cache, ref input.ProxyA, ref sweepA, ref input.ProxyB, ref sweepB, t1);

                // Compute the TOI on the separating axis. We do this by successively
                // resolving the deepest point. This loop is bounded by the number of vertices.
                bool done = false;
                float t2 = tMax;
                int pushBackIter = 0;
                for (; ; )
                {
                    // Find the deepest point at t2. Store the witness point indices.
                    int indexA, indexB;
                    float s2 = SeparationFunction.FindMinSeparation(out indexA, out indexB, t2);

                    // Is the final configuration separated?
                    if (s2 > target + tolerance)
                    {
                        // Victory!
                        output.State = TOIOutputState.Seperated;
                        output.T = tMax;
                        done = true;
                        break;
                    }

                    // Has the separation reached tolerance?
                    if (s2 > target - tolerance)
                    {
                        // Advance the sweeps
                        t1 = t2;
                        break;
                    }

                    // Compute the initial separation of the witness points.
                    float s1 = SeparationFunction.Evaluate(indexA, indexB, t1);

                    // Check for initial overlap. This might happen if the root finder
                    // runs out of iterations.
                    if (s1 < target - tolerance)
                    {
                        output.State = TOIOutputState.Failed;
                        output.T = t1;
                        done = true;
                        break;
                    }

                    // Check for touching
                    if (s1 <= target + tolerance)
                    {
                        // Victory! t1 should hold the TOI (could be 0.0).
                        output.State = TOIOutputState.Touching;
                        output.T = t1;
                        done = true;
                        break;
                    }

                    // Compute 1D root of: f(x) - target = 0
                    int rootIterCount = 0;
                    float a1 = t1, a2 = t2;
                    for (; ; )
                    {
                        // Use a mix of the secant rule and bisection.
                        float t;
                        if ((rootIterCount & 1) != 0)
                        {
                            // Secant rule to improve convergence.
                            t = a1 + (target - s1) * (a2 - a1) / (s2 - s1);
                        }
                        else
                        {
                            // Bisection to guarantee progress.
                            t = 0.5f * (a1 + a2);
                        }

                        ++rootIterCount;

                        if (Settings.EnableDiagnostics) //FPE: We only gather diagnostics when enabled
                            ++TOIRootIters;

                        float s = SeparationFunction.Evaluate(indexA, indexB, t);

                        if (Math.Abs(s - target) < tolerance)
                        {
                            // t2 holds a tentative value for t1
                            t2 = t;
                            break;
                        }

                        // Ensure we continue to bracket the root.
                        if (s > target)
                        {
                            a1 = t;
                            s1 = s;
                        }
                        else
                        {
                            a2 = t;
                            s2 = s;
                        }

                        if (rootIterCount == 50)
                        {
                            break;
                        }
                    }

                    if (Settings.EnableDiagnostics) //FPE: We only gather diagnostics when enabled
                        TOIMaxRootIters = Math.Max(TOIMaxRootIters, rootIterCount);

                    ++pushBackIter;

                    if (pushBackIter == Settings.MaxPolygonVertices)
                    {
                        break;
                    }
                }

                ++iter;

                if (Settings.EnableDiagnostics) //FPE: We only gather diagnostics when enabled
                    ++TOIIters;

                if (done)
                {
                    break;
                }

                if (iter == k_maxIterations)
                {
                    // Root finder got stuck. Semi-victory.
                    output.State = TOIOutputState.Failed;
                    output.T = t1;
                    break;
                }
            }

            if (Settings.EnableDiagnostics) //FPE: We only gather diagnostics when enabled
                TOIMaxIters = Math.Max(TOIMaxIters, iter);
        }
    }
}