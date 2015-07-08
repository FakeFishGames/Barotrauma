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

using FarseerPhysics.Common;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Collision.Shapes
{
    /// <summary>
    /// A line segment (edge) shape. These can be connected in chains or loops
    /// to other edge shapes.
    /// The connectivity information is used to ensure correct contact normals.
    /// </summary>
    public class EdgeShape : Shape
    {
        /// <summary>
        /// Edge start vertex
        /// </summary>
        internal Vector2 _vertex1;

        /// <summary>
        /// Edge end vertex
        /// </summary>
        internal Vector2 _vertex2;

        internal EdgeShape()
            : base(0)
        {
            ShapeType = ShapeType.Edge;
            _radius = Settings.PolygonRadius;
        }

        /// <summary>
        /// Create a new EdgeShape with the specified start and end.
        /// </summary>
        /// <param name="start">The start of the edge.</param>
        /// <param name="end">The end of the edge.</param>
        public EdgeShape(Vector2 start, Vector2 end)
            : base(0)
        {
            ShapeType = ShapeType.Edge;
            _radius = Settings.PolygonRadius;
            Set(start, end);
        }

        public override int ChildCount
        {
            get { return 1; }
        }

        /// <summary>
        /// Is true if the edge is connected to an adjacent vertex before vertex 1.
        /// </summary>
        public bool HasVertex0 { get; set; }

        /// <summary>
        /// Is true if the edge is connected to an adjacent vertex after vertex2.
        /// </summary>
        public bool HasVertex3 { get; set; }

        /// <summary>
        /// Optional adjacent vertices. These are used for smooth collision.
        /// </summary>
        public Vector2 Vertex0 { get; set; }

        /// <summary>
        /// Optional adjacent vertices. These are used for smooth collision.
        /// </summary>
        public Vector2 Vertex3 { get; set; }

        /// <summary>
        /// These are the edge vertices
        /// </summary>
        public Vector2 Vertex1
        {
            get { return _vertex1; }
            set
            {
                _vertex1 = value;
                ComputeProperties();
            }
        }

        /// <summary>
        /// These are the edge vertices
        /// </summary>
        public Vector2 Vertex2
        {
            get { return _vertex2; }
            set
            {
                _vertex2 = value;
                ComputeProperties();
            }
        }

        /// <summary>
        /// Set this as an isolated edge.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        public void Set(Vector2 start, Vector2 end)
        {
            _vertex1 = start;
            _vertex2 = end;
            HasVertex0 = false;
            HasVertex3 = false;

            ComputeProperties();
        }

        public override bool TestPoint(ref Transform transform, ref Vector2 point)
        {
            return false;
        }

        public override bool RayCast(out RayCastOutput output, ref RayCastInput input, ref Transform transform, int childIndex)
        {
            // p = p1 + t * d
            // v = v1 + s * e
            // p1 + t * d = v1 + s * e
            // s * e - t * d = p1 - v1

            output = new RayCastOutput();

            // Put the ray into the edge's frame of reference.
            Vector2 p1 = MathUtils.MulT(transform.q, input.Point1 - transform.p);
            Vector2 p2 = MathUtils.MulT(transform.q, input.Point2 - transform.p);
            Vector2 d = p2 - p1;

            Vector2 v1 = _vertex1;
            Vector2 v2 = _vertex2;
            Vector2 e = v2 - v1;
            Vector2 normal = new Vector2(e.Y, -e.X); //TODO: Could possibly cache the normal.
            normal.Normalize();

            // q = p1 + t * d
            // dot(normal, q - v1) = 0
            // dot(normal, p1 - v1) + t * dot(normal, d) = 0
            float numerator = Vector2.Dot(normal, v1 - p1);
            float denominator = Vector2.Dot(normal, d);

            if (denominator == 0.0f)
            {
                return false;
            }

            float t = numerator / denominator;
            if (t < 0.0f || input.MaxFraction < t)
            {
                return false;
            }

            Vector2 q = p1 + t * d;

            // q = v1 + s * r
            // s = dot(q - v1, r) / dot(r, r)
            Vector2 r = v2 - v1;
            float rr = Vector2.Dot(r, r);
            if (rr == 0.0f)
            {
                return false;
            }

            float s = Vector2.Dot(q - v1, r) / rr;
            if (s < 0.0f || 1.0f < s)
            {
                return false;
            }

            output.Fraction = t;
            if (numerator > 0.0f)
            {
                output.Normal = -normal;
            }
            else
            {
                output.Normal = normal;
            }
            return true;
        }

        public override void ComputeAABB(out AABB aabb, ref Transform transform, int childIndex)
        {
            Vector2 v1 = MathUtils.Mul(ref transform, _vertex1);
            Vector2 v2 = MathUtils.Mul(ref transform, _vertex2);

            Vector2 lower = Vector2.Min(v1, v2);
            Vector2 upper = Vector2.Max(v1, v2);

            Vector2 r = new Vector2(Radius, Radius);
            aabb.LowerBound = lower - r;
            aabb.UpperBound = upper + r;
        }

        protected override void ComputeProperties()
        {
            MassData.Centroid = 0.5f * (_vertex1 + _vertex2);
        }

        public override float ComputeSubmergedArea(ref Vector2 normal, float offset, ref Transform xf, out Vector2 sc)
        {
            sc = Vector2.Zero;
            return 0;
        }

        public bool CompareTo(EdgeShape shape)
        {
            return (HasVertex0 == shape.HasVertex0 &&
                    HasVertex3 == shape.HasVertex3 &&
                    Vertex0 == shape.Vertex0 &&
                    Vertex1 == shape.Vertex1 &&
                    Vertex2 == shape.Vertex2 &&
                    Vertex3 == shape.Vertex3);
        }

        public override Shape Clone()
        {
            EdgeShape clone = new EdgeShape();
            clone.ShapeType = ShapeType;
            clone._radius = _radius;
            clone._density = _density;
            clone.HasVertex0 = HasVertex0;
            clone.HasVertex3 = HasVertex3;
            clone.Vertex0 = Vertex0;
            clone._vertex1 = _vertex1;
            clone._vertex2 = _vertex2;
            clone.Vertex3 = Vertex3;
            clone.MassData = MassData;
            return clone;
        }
    }
}