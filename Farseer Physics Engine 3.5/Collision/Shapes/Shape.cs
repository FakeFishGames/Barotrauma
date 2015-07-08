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
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Collision.Shapes
{
    /// <summary>
    /// This holds the mass data computed for a shape.
    /// </summary>
    public struct MassData : IEquatable<MassData>
    {
        /// <summary>
        /// The area of the shape
        /// </summary>
        public float Area { get; internal set; }

        /// <summary>
        /// The position of the shape's centroid relative to the shape's origin.
        /// </summary>
        public Vector2 Centroid { get; internal set; }

        /// <summary>
        /// The rotational inertia of the shape about the local origin.
        /// </summary>
        public float Inertia { get; internal set; }

        /// <summary>
        /// The mass of the shape, usually in kilograms.
        /// </summary>
        public float Mass { get; internal set; }

        /// <summary>
        /// The equal operator
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(MassData left, MassData right)
        {
            return (left.Area == right.Area && left.Mass == right.Mass && left.Centroid == right.Centroid && left.Inertia == right.Inertia);
        }

        /// <summary>
        /// The not equal operator
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(MassData left, MassData right)
        {
            return !(left == right);
        }

        public bool Equals(MassData other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;

            if (obj.GetType() != typeof(MassData))
                return false;

            return Equals((MassData)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int result = Area.GetHashCode();
                result = (result * 397) ^ Centroid.GetHashCode();
                result = (result * 397) ^ Inertia.GetHashCode();
                result = (result * 397) ^ Mass.GetHashCode();
                return result;
            }
        }
    }

    public enum ShapeType
    {
        Unknown = -1,
        Circle = 0,
        Edge = 1,
        Polygon = 2,
        Chain = 3,
        TypeCount = 4,
    }

    /// <summary>
    /// A shape is used for collision detection. You can create a shape however you like.
    /// Shapes used for simulation in World are created automatically when a Fixture
    /// is created. Shapes may encapsulate a one or more child shapes.
    /// </summary>
    public abstract class Shape
    {
        internal float _density;
        internal float _radius;
        internal float _2radius;

        protected Shape(float density)
        {
            _density = density;
            ShapeType = ShapeType.Unknown;
        }

        /// <summary>
        /// Contains the properties of the shape such as:
        /// - Area of the shape
        /// - Centroid
        /// - Inertia
        /// - Mass
        /// </summary>
        public MassData MassData;

        /// <summary>
        /// Get the type of this shape.
        /// </summary>
        /// <value>The type of the shape.</value>
        public ShapeType ShapeType { get; internal set; }

        /// <summary>
        /// Get the number of child primitives.
        /// </summary>
        /// <value></value>
        public abstract int ChildCount { get; }

        /// <summary>
        /// Gets or sets the density.
        /// Changing the density causes a recalculation of shape properties.
        /// </summary>
        /// <value>The density.</value>
        public float Density
        {
            get { return _density; }
            set
            {
                Debug.Assert(value >= 0);

                _density = value;
                ComputeProperties();
            }
        }

        /// <summary>
        /// Radius of the Shape
        /// Changing the radius causes a recalculation of shape properties.
        /// </summary>
        public float Radius
        {
            get { return _radius; }
            set
            {
                Debug.Assert(value >= 0);

                _radius = value;
                _2radius = _radius * _radius;

                ComputeProperties();
            }
        }

        /// <summary>
        /// Clone the concrete shape
        /// </summary>
        /// <returns>A clone of the shape</returns>
        public abstract Shape Clone();

        /// <summary>
        /// Test a point for containment in this shape.
        /// Note: This only works for convex shapes.
        /// </summary>
        /// <param name="transform">The shape world transform.</param>
        /// <param name="point">A point in world coordinates.</param>
        /// <returns>True if the point is inside the shape</returns>
        public abstract bool TestPoint(ref Transform transform, ref Vector2 point);

        /// <summary>
        /// Cast a ray against a child shape.
        /// </summary>
        /// <param name="output">The ray-cast results.</param>
        /// <param name="input">The ray-cast input parameters.</param>
        /// <param name="transform">The transform to be applied to the shape.</param>
        /// <param name="childIndex">The child shape index.</param>
        /// <returns>True if the ray-cast hits the shape</returns>
        public abstract bool RayCast(out RayCastOutput output, ref RayCastInput input, ref Transform transform, int childIndex);

        /// <summary>
        /// Given a transform, compute the associated axis aligned bounding box for a child shape.
        /// </summary>
        /// <param name="aabb">The aabb results.</param>
        /// <param name="transform">The world transform of the shape.</param>
        /// <param name="childIndex">The child shape index.</param>
        public abstract void ComputeAABB(out AABB aabb, ref Transform transform, int childIndex);

        /// <summary>
        /// Compute the mass properties of this shape using its dimensions and density.
        /// The inertia tensor is computed about the local origin, not the centroid.
        /// </summary>
        protected abstract void ComputeProperties();

        /// <summary>
        /// Compare this shape to another shape based on type and properties.
        /// </summary>
        /// <param name="shape">The other shape</param>
        /// <returns>True if the two shapes are the same.</returns>
        public bool CompareTo(Shape shape)
        {
            if (shape is PolygonShape && this is PolygonShape)
                return ((PolygonShape)this).CompareTo((PolygonShape)shape);

            if (shape is CircleShape && this is CircleShape)
                return ((CircleShape)this).CompareTo((CircleShape)shape);

            if (shape is EdgeShape && this is EdgeShape)
                return ((EdgeShape)this).CompareTo((EdgeShape)shape);

            if (shape is ChainShape && this is ChainShape)
                return ((ChainShape)this).CompareTo((ChainShape)shape);

            return false;
        }

        /// <summary>
        /// Used for the buoyancy controller
        /// </summary>
        public abstract float ComputeSubmergedArea(ref Vector2 normal, float offset, ref Transform xf, out Vector2 sc);
    }
}