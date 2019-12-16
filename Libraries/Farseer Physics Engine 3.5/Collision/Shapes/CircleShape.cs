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

namespace FarseerPhysics.Collision.Shapes
{
    /// <summary>
    /// A circle shape.
    /// </summary>
    public class CircleShape : Shape
    {
        internal Vector2 _position;

        /// <summary>
        /// Create a new circle with the desired radius and density.
        /// </summary>
        /// <param name="radius">The radius of the circle.</param>
        /// <param name="density">The density of the circle.</param>
        public CircleShape(float radius, float density)
            : base(density)
        {
            Debug.Assert(radius >= 0);
            Debug.Assert(density >= 0);

            ShapeType = ShapeType.Circle;
            _position = Vector2.Zero;
            Radius = radius; // The Radius property cache 2radius and calls ComputeProperties(). So no need to call ComputeProperties() here.
        }

        internal CircleShape()
            : base(0)
        {
            ShapeType = ShapeType.Circle;
            _radius = 0.0f;
            _position = Vector2.Zero;
        }

        public override int ChildCount
        {
            get { return 1; }
        }

        /// <summary>
        /// Get or set the position of the circle
        /// </summary>
        public Vector2 Position
        {
            get { return _position; }
            set
            {
                _position = value;
                ComputeProperties(); //TODO: Optimize here
            }
        }

        public override bool TestPoint(ref Transform transform, ref Vector2 point)
        {
            Vector2 center = transform.p + Complex.Multiply(ref _position, ref transform.q);
            Vector2 d = point - center;
            return Vector2.Dot(d, d) <= _2radius;
        }

        public override bool RayCast(out RayCastOutput output, ref RayCastInput input, ref Transform transform, int childIndex)
        {
            // Collision Detection in Interactive 3D Environments by Gino van den Bergen
            // From Section 3.1.2
            // x = s + a * r
            // norm(x) = radius

            output = new RayCastOutput();

            Vector2 position = transform.p + Complex.Multiply(ref _position, ref transform.q);
            Vector2 s = input.Point1 - position;
            float b = Vector2.Dot(s, s) - _2radius;

            // Solve quadratic equation.
            Vector2 r = input.Point2 - input.Point1;
            float c = Vector2.Dot(s, r);
            float rr = Vector2.Dot(r, r);
            float sigma = c * c - rr * b;

            // Check for negative discriminant and short segment.
            if (sigma < 0.0f || rr < Settings.Epsilon)
            {
                return false;
            }

            // Find the point of intersection of the line with the circle.
            float a = -(c + (float)Math.Sqrt(sigma));

            // Is the intersection point on the segment?
            if (0.0f <= a && a <= input.MaxFraction * rr)
            {
                a /= rr;
                output.Fraction = a;

                //TODO: Check results here
                output.Normal = s + a * r;
                output.Normal.Normalize();
                return true;
            }

            return false;
        }

        public override void ComputeAABB(out AABB aabb, ref Transform transform, int childIndex)
        {
            // OPT: Vector2 p = transform.p + Complex.Multiply(ref _position, ref transform.q);
            var pX = (_position.X * transform.q.Real - _position.Y * transform.q.Imaginary) + transform.p.X;
            var pY = (_position.Y * transform.q.Real + _position.X * transform.q.Imaginary) + transform.p.Y;

            // OPT: aabb.LowerBound = new Vector2(p.X - Radius, p.Y - Radius);
            // OPT: aabb.UpperBound = new Vector2(p.X + Radius, p.Y + Radius);
            aabb.LowerBound.X = pX - Radius;
            aabb.LowerBound.Y = pY - Radius;
            aabb.UpperBound.X = pX + Radius;
            aabb.UpperBound.Y = pY + Radius;
        }

        protected override sealed void ComputeProperties()
        {
            float area = MathHelper.Pi * _2radius;
            MassData.Area = area;
            MassData.Mass = Density * area;
            MassData.Centroid = Position;

            // inertia about the local origin
            MassData.Inertia = MassData.Mass * (0.5f * _2radius + Vector2.Dot(Position, Position));
        }

        public override float ComputeSubmergedArea(ref Vector2 normal, float offset, ref Transform xf, out Vector2 sc)
        {
            sc = Vector2.Zero;

            Vector2 p = Transform.Multiply(ref _position, ref xf);
            float l = -(Vector2.Dot(normal, p) - offset);
            if (l < -Radius + Settings.Epsilon)
            {
                //Completely dry
                return 0;
            }
            if (l > Radius)
            {
                //Completely wet
                sc = p;
                return MathHelper.Pi * _2radius;
            }

            //Magic
            float l2 = l * l;
            float area = _2radius * (float)((Math.Asin(l / Radius) + MathHelper.Pi / 2) + l * Math.Sqrt(_2radius - l2));
            float com = -2.0f / 3.0f * (float)Math.Pow(_2radius - l2, 1.5f) / area;

            sc.X = p.X + normal.X * com;
            sc.Y = p.Y + normal.Y * com;

            return area;
        }

        /// <summary>
        /// Compare the circle to another circle
        /// </summary>
        /// <param name="shape">The other circle</param>
        /// <returns>True if the two circles are the same size and have the same position</returns>
        public bool CompareTo(CircleShape shape)
        {
            return (Radius == shape.Radius && Position == shape.Position);
        }

        public override Shape Clone()
        {
            CircleShape clone = new CircleShape();
            clone.ShapeType = ShapeType;
            clone._radius = Radius;
            clone._2radius = _2radius; //FPE note: We also copy the cache
            clone._density = _density;
            clone._position = _position;
            clone.MassData = MassData;
            return clone;
        }
    }
}