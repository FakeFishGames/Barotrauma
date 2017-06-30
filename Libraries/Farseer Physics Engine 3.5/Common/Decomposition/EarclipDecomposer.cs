/*
* C# Version Ported by Matt Bettcher and Ian Qvist 2009-2010
* 
* Original C++ Version Copyright (c) 2007 Eric Jordan
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
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.Decomposition
{
    /// <summary>
    /// Convex decomposition algorithm using ear clipping
    /// 
    /// Properties:
    /// - Only works on simple polygons.
    /// - Does not support holes.
    /// - Running time is O(n^2), n = number of vertices.
    /// 
    /// Source: http://www.ewjordan.com/earClip/
    /// </summary>
    internal static class EarclipDecomposer
    {
        //box2D rev 32 - for details, see http://www.box2d.org/forum/viewtopic.php?f=4&t=83&start=50 

        /// <summary>
        /// Decompose the polygon into several smaller non-concave polygon.
        /// Each resulting polygon will have no more than Settings.MaxPolygonVertices vertices.
        /// </summary>
        /// <param name="vertices">The vertices.</param>
        /// <param name="tolerance">The tolerance.</param>
        public static List<Vertices> ConvexPartition(Vertices vertices, float tolerance = 0.001f)
        {
            Debug.Assert(vertices.Count > 3);
            Debug.Assert(!vertices.IsCounterClockWise());

            return TriangulatePolygon(vertices, tolerance);
        }

        /// <summary>
        /// Triangulates a polygon using simple ear-clipping algorithm. Returns
        /// size of Triangle array unless the polygon can't be triangulated.
        /// This should only happen if the polygon self-intersects,
        /// though it will not _always_ return null for a bad polygon - it is the
        /// caller's responsibility to check for self-intersection, and if it
        /// doesn't, it should at least check that the return value is non-null
        /// before using. You're warned!
        ///
        /// Triangles may be degenerate, especially if you have identical points
        /// in the input to the algorithm.  Check this before you use them.
        ///
        /// This is totally unoptimized, so for large polygons it should not be part
        /// of the simulation loop.
        /// </summary>
        /// <remarks>
        /// Only works on simple polygons.
        /// </remarks>
        private static List<Vertices> TriangulatePolygon(Vertices vertices, float tolerance)
        {
            //FPE note: Check is needed as invalid triangles can be returned in recursive calls.
            if (vertices.Count < 3)
                return new List<Vertices>();

            List<Vertices> results = new List<Vertices>();

            //Recurse and split on pinch points
            Vertices pA, pB;
            Vertices pin = new Vertices(vertices);
            if (ResolvePinchPoint(pin, out pA, out pB, tolerance))
            {
                List<Vertices> mergeA = TriangulatePolygon(pA, tolerance);
                List<Vertices> mergeB = TriangulatePolygon(pB, tolerance);

                if (mergeA.Count == -1 || mergeB.Count == -1)
                    throw new Exception("Can't triangulate your polygon.");

                for (int i = 0; i < mergeA.Count; ++i)
                {
                    results.Add(new Vertices(mergeA[i]));
                }
                for (int i = 0; i < mergeB.Count; ++i)
                {
                    results.Add(new Vertices(mergeB[i]));
                }

                return results;
            }

            Vertices[] buffer = new Vertices[vertices.Count - 2];
            int bufferSize = 0;
            float[] xrem = new float[vertices.Count];
            float[] yrem = new float[vertices.Count];
            for (int i = 0; i < vertices.Count; ++i)
            {
                xrem[i] = vertices[i].X;
                yrem[i] = vertices[i].Y;
            }

            int vNum = vertices.Count;

            while (vNum > 3)
            {
                // Find an ear
                int earIndex = -1;
                float earMaxMinCross = -10.0f;
                for (int i = 0; i < vNum; ++i)
                {
                    if (IsEar(i, xrem, yrem, vNum))
                    {
                        int lower = Remainder(i - 1, vNum);
                        int upper = Remainder(i + 1, vNum);
                        Vector2 d1 = new Vector2(xrem[upper] - xrem[i], yrem[upper] - yrem[i]);
                        Vector2 d2 = new Vector2(xrem[i] - xrem[lower], yrem[i] - yrem[lower]);
                        Vector2 d3 = new Vector2(xrem[lower] - xrem[upper], yrem[lower] - yrem[upper]);

                        d1.Normalize();
                        d2.Normalize();
                        d3.Normalize();
                        float cross12;
                        MathUtils.Cross(ref d1, ref d2, out cross12);
                        cross12 = Math.Abs(cross12);

                        float cross23;
                        MathUtils.Cross(ref d2, ref d3, out cross23);
                        cross23 = Math.Abs(cross23);

                        float cross31;
                        MathUtils.Cross(ref d3, ref d1, out cross31);
                        cross31 = Math.Abs(cross31);

                        //Find the maximum minimum angle
                        float minCross = Math.Min(cross12, Math.Min(cross23, cross31));
                        if (minCross > earMaxMinCross)
                        {
                            earIndex = i;
                            earMaxMinCross = minCross;
                        }
                    }
                }

                // If we still haven't found an ear, we're screwed.
                // Note: sometimes this is happening because the
                // remaining points are collinear.  Really these
                // should just be thrown out without halting triangulation.
                if (earIndex == -1)
                {
                    for (int i = 0; i < bufferSize; i++)
                    {
                        results.Add(buffer[i]);
                    }

                    return results;
                }

                // Clip off the ear:
                // - remove the ear tip from the list

                --vNum;
                float[] newx = new float[vNum];
                float[] newy = new float[vNum];
                int currDest = 0;
                for (int i = 0; i < vNum; ++i)
                {
                    if (currDest == earIndex) ++currDest;
                    newx[i] = xrem[currDest];
                    newy[i] = yrem[currDest];
                    ++currDest;
                }

                // - add the clipped triangle to the triangle list
                int under = (earIndex == 0) ? (vNum) : (earIndex - 1);
                int over = (earIndex == vNum) ? 0 : (earIndex + 1);
                Triangle toAdd = new Triangle(xrem[earIndex], yrem[earIndex], xrem[over], yrem[over], xrem[under],
                                              yrem[under]);
                buffer[bufferSize] = toAdd;
                ++bufferSize;

                // - replace the old list with the new one
                xrem = newx;
                yrem = newy;
            }

            Triangle tooAdd = new Triangle(xrem[1], yrem[1], xrem[2], yrem[2], xrem[0], yrem[0]);
            buffer[bufferSize] = tooAdd;
            ++bufferSize;

            for (int i = 0; i < bufferSize; i++)
            {
                results.Add(new Vertices(buffer[i]));
            }

            return results;
        }

        /// <summary>
        /// Finds and fixes "pinch points," points where two polygon
        /// vertices are at the same point.
        /// 
        /// If a pinch point is found, pin is broken up into poutA and poutB
        /// and true is returned; otherwise, returns false.
        /// 
        /// Mostly for internal use.
        /// 
        /// O(N^2) time, which sucks...
        /// </summary>
        /// <param name="pin">The pin.</param>
        /// <param name="poutA">The pout A.</param>
        /// <param name="poutB">The pout B.</param>
        /// <param name="tolerance"></param>
        private static bool ResolvePinchPoint(Vertices pin, out Vertices poutA, out Vertices poutB, float tolerance)
        {
            poutA = new Vertices();
            poutB = new Vertices();

            if (pin.Count < 3)
                return false;

            bool hasPinchPoint = false;
            int pinchIndexA = -1;
            int pinchIndexB = -1;
            for (int i = 0; i < pin.Count; ++i)
            {
                for (int j = i + 1; j < pin.Count; ++j)
                {
                    //Don't worry about pinch points where the points
                    //are actually just dupe neighbors
                    if (Math.Abs(pin[i].X - pin[j].X) < tolerance && Math.Abs(pin[i].Y - pin[j].Y) < tolerance && j != i + 1)
                    {
                        pinchIndexA = i;
                        pinchIndexB = j;
                        hasPinchPoint = true;
                        break;
                    }
                }
                if (hasPinchPoint) break;
            }
            if (hasPinchPoint)
            {
                int sizeA = pinchIndexB - pinchIndexA;
                if (sizeA == pin.Count) return false; //has dupe points at wraparound, not a problem here
                for (int i = 0; i < sizeA; ++i)
                {
                    int ind = Remainder(pinchIndexA + i, pin.Count); // is this right
                    poutA.Add(pin[ind]);
                }

                int sizeB = pin.Count - sizeA;
                for (int i = 0; i < sizeB; ++i)
                {
                    int ind = Remainder(pinchIndexB + i, pin.Count); // is this right    
                    poutB.Add(pin[ind]);
                }
            }
            return hasPinchPoint;
        }

        /// <summary>
        /// Fix for obnoxious behavior for the % operator for negative numbers...
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="modulus">The modulus.</param>
        /// <returns></returns>
        private static int Remainder(int x, int modulus)
        {
            int rem = x % modulus;
            while (rem < 0)
            {
                rem += modulus;
            }
            return rem;
        }

        /// <summary>
        /// Checks if vertex i is the tip of an ear in polygon defined by xv[] and  yv[].
        /// </summary>
        /// <param name="i">The i.</param>
        /// <param name="xv">The xv.</param>
        /// <param name="yv">The yv.</param>
        /// <param name="xvLength">Length of the xv.</param>
        /// <remarks>
        /// Assumes clockwise orientation of polygon.
        /// </remarks>
        /// <returns>
        /// 	<c>true</c> if the specified i is ear; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsEar(int i, float[] xv, float[] yv, int xvLength)
        {
            float dx0, dy0, dx1, dy1;
            if (i >= xvLength || i < 0 || xvLength < 3)
            {
                return false;
            }
            int upper = i + 1;
            int lower = i - 1;
            if (i == 0)
            {
                dx0 = xv[0] - xv[xvLength - 1];
                dy0 = yv[0] - yv[xvLength - 1];
                dx1 = xv[1] - xv[0];
                dy1 = yv[1] - yv[0];
                lower = xvLength - 1;
            }
            else if (i == xvLength - 1)
            {
                dx0 = xv[i] - xv[i - 1];
                dy0 = yv[i] - yv[i - 1];
                dx1 = xv[0] - xv[i];
                dy1 = yv[0] - yv[i];
                upper = 0;
            }
            else
            {
                dx0 = xv[i] - xv[i - 1];
                dy0 = yv[i] - yv[i - 1];
                dx1 = xv[i + 1] - xv[i];
                dy1 = yv[i + 1] - yv[i];
            }

            float cross = dx0 * dy1 - dx1 * dy0;

            if (cross > 0)
                return false;

            Triangle myTri = new Triangle(xv[i], yv[i], xv[upper], yv[upper], xv[lower], yv[lower]);

            for (int j = 0; j < xvLength; ++j)
            {
                if (j == i || j == lower || j == upper)
                    continue;
                if (myTri.IsInside(xv[j], yv[j]))
                    return false;
            }
            return true;
        }

        private class Triangle : Vertices
        {
            //Constructor automatically fixes orientation to ccw
            public Triangle(float x1, float y1, float x2, float y2, float x3, float y3)
            {
                float cross = (x2 - x1) * (y3 - y1) - (x3 - x1) * (y2 - y1);
                if (cross > 0)
                {
                    Add(new Vector2(x1, y1));
                    Add(new Vector2(x2, y2));
                    Add(new Vector2(x3, y3));
                }
                else
                {
                    Add(new Vector2(x1, y1));
                    Add(new Vector2(x3, y3));
                    Add(new Vector2(x2, y2));
                }
            }

            public bool IsInside(float x, float y)
            {
                Vector2 a = this[0];
                Vector2 b = this[1];
                Vector2 c = this[2];

                if (x < a.X && x < b.X && x < c.X) return false;
                if (x > a.X && x > b.X && x > c.X) return false;
                if (y < a.Y && y < b.Y && y < c.Y) return false;
                if (y > a.Y && y > b.Y && y > c.Y) return false;

                float vx2 = x - a.X;
                float vy2 = y - a.Y;
                float vx1 = b.X - a.X;
                float vy1 = b.Y - a.Y;
                float vx0 = c.X - a.X;
                float vy0 = c.Y - a.Y;

                float dot00 = vx0 * vx0 + vy0 * vy0;
                float dot01 = vx0 * vx1 + vy0 * vy1;
                float dot02 = vx0 * vx2 + vy0 * vy2;
                float dot11 = vx1 * vx1 + vy1 * vy1;
                float dot12 = vx1 * vx2 + vy1 * vy2;
                float invDenom = 1.0f / (dot00 * dot11 - dot01 * dot01);
                float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
                float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

                return ((u > 0) && (v > 0) && (u + v < 1));
            }
        }
    }
}