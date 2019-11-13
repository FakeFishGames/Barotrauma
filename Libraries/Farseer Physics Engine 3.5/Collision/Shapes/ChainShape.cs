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

using System.Diagnostics;
using FarseerPhysics.Common;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Collision.Shapes
{
    /// <summary>
    /// A chain shape is a free form sequence of line segments.
    /// The chain has two-sided collision, so you can use inside and outside collision.
    /// Therefore, you may use any winding order.
    /// Connectivity information is used to create smooth collisions.
    /// WARNING: The chain will not collide properly if there are self-intersections.
    /// </summary>
    public class ChainShape : Shape
    {
        /// <summary>
        /// The vertices. These are not owned/freed by the chain Shape.
        /// </summary>
        public Vertices Vertices;
        private Vector2 _prevVertex, _nextVertex;
        private bool _hasPrevVertex, _hasNextVertex;
        private static EdgeShape _edgeShape = new EdgeShape();

        /// <summary>
        /// Constructor for ChainShape. By default have 0 in density.
        /// </summary>
        public ChainShape()
            : base(0)
        {
            ShapeType = ShapeType.Chain;
            _radius = Settings.PolygonRadius;
        }

        /// <summary>
        /// Create a new chainshape from the vertices.
        /// </summary>
        /// <param name="vertices">The vertices to use. Must contain 2 or more vertices.</param>
        /// <param name="createLoop">Set to true to create a closed loop. It connects the first vertice to the last, and automatically adjusts connectivity to create smooth collisions along the chain.</param>
        public ChainShape(Vertices vertices, bool createLoop = false)
            : base(0)
        {
            ShapeType = ShapeType.Chain;
            _radius = Settings.PolygonRadius;

            Debug.Assert(vertices != null && vertices.Count >= 3);
            Debug.Assert(vertices[0] != vertices[vertices.Count - 1]); // FPE. See http://www.box2d.org/forum/viewtopic.php?f=4&t=7973&p=35363

            for (int i = 1; i < vertices.Count; ++i)
            {
                Vector2 v1 = vertices[i - 1];
                Vector2 v2 = vertices[i];

                // If the code crashes here, it means your vertices are too close together.
                Debug.Assert(Vector2.DistanceSquared(v1, v2) > Settings.LinearSlop * Settings.LinearSlop);
            }

            Vertices = new Vertices(vertices);

            if (createLoop)
            {
                Vertices.Add(vertices[0]);
                PrevVertex = Vertices[Vertices.Count - 2]; //FPE: We use the properties instead of the private fields here.
                NextVertex = Vertices[1]; //FPE: We use the properties instead of the private fields here.
            }
        }

        public override int ChildCount
        {
            // edge count = vertex count - 1
            get { return Vertices.Count - 1; }
        }

        /// <summary>
        /// Establish connectivity to a vertex that precedes the first vertex.
        /// Don't call this for loops.
        /// </summary>
        public Vector2 PrevVertex
        {
            get { return _prevVertex; }
            set
            {
                Debug.Assert(value != null);

                _prevVertex = value;
                _hasPrevVertex = true;
            }
        }

        /// <summary>
        /// Establish connectivity to a vertex that follows the last vertex.
        /// Don't call this for loops.
        /// </summary>
        public Vector2 NextVertex
        {
            get { return _nextVertex; }
            set
            {
                Debug.Assert(value != null);

                _nextVertex = value;
                _hasNextVertex = true;
            }
        }

        /// <summary>
        /// This method has been optimized to reduce garbage.
        /// </summary>
        /// <param name="edge">The cached edge to set properties on.</param>
        /// <param name="index">The index.</param>
        internal void GetChildEdge(EdgeShape edge, int index)
        {
            Debug.Assert(0 <= index && index < Vertices.Count - 1);
            Debug.Assert(edge != null);

            edge.ShapeType = ShapeType.Edge;
            edge._radius = _radius;

            edge.Vertex1 = Vertices[index + 0];
            edge.Vertex2 = Vertices[index + 1];

            if (index > 0)
            {
                edge.Vertex0 = Vertices[index - 1];
                edge.HasVertex0 = true;
            }
            else
            {
                edge.Vertex0 = _prevVertex;
                edge.HasVertex0 = _hasPrevVertex;
            }

            if (index < Vertices.Count - 2)
            {
                edge.Vertex3 = Vertices[index + 2];
                edge.HasVertex3 = true;
            }
            else
            {
                edge.Vertex3 = _nextVertex;
                edge.HasVertex3 = _hasNextVertex;
            }
        }

        /// <summary>
        /// Get a child edge.
        /// </summary>
        /// <param name="index">The index.</param>
        public EdgeShape GetChildEdge(int index)
        {
            EdgeShape edgeShape = new EdgeShape();
            GetChildEdge(edgeShape, index);
            return edgeShape;
        }

        public override bool TestPoint(ref Transform transform, ref Vector2 point)
        {
            return false;
        }

        public override bool RayCast(out RayCastOutput output, ref RayCastInput input, ref Transform transform, int childIndex)
        {
            Debug.Assert(childIndex < Vertices.Count);

            int i1 = childIndex;
            int i2 = childIndex + 1;
            if (i2 == Vertices.Count)
            {
                i2 = 0;
            }

            _edgeShape.Vertex1 = Vertices[i1];
            _edgeShape.Vertex2 = Vertices[i2];

            return _edgeShape.RayCast(out output, ref input, ref transform, 0);
        }

        public override void ComputeAABB(out AABB aabb, ref Transform transform, int childIndex)
        {
            Debug.Assert(childIndex < Vertices.Count);

            int i1 = childIndex;
            int i2 = childIndex + 1;
            if (i2 == Vertices.Count)
            {
                i2 = 0;
            }

            Vector2 v1 = Transform.Multiply(Vertices[i1], ref transform);
            Vector2 v2 = Transform.Multiply(Vertices[i2], ref transform);

            Vector2.Min(ref v1, ref v2, out aabb.LowerBound);
            Vector2.Max(ref v1, ref v2, out aabb.UpperBound);
        }

        protected override void ComputeProperties()
        {
            //Does nothing. Chain shapes don't have properties.
        }

        public override float ComputeSubmergedArea(ref Vector2 normal, float offset, ref Transform xf, out Vector2 sc)
        {
            sc = Vector2.Zero;
            return 0;
        }

        /// <summary>
        /// Compare the chain to another chain
        /// </summary>
        /// <param name="shape">The other chain</param>
        /// <returns>True if the two chain shapes are the same</returns>
        public bool CompareTo(ChainShape shape)
        {
            if (Vertices.Count != shape.Vertices.Count)
                return false;

            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices[i] != shape.Vertices[i])
                    return false;
            }

            return PrevVertex == shape.PrevVertex && NextVertex == shape.NextVertex;
        }

        public override Shape Clone()
        {
            ChainShape clone = new ChainShape();
            clone.ShapeType = ShapeType;
            clone._density = _density;
            clone._radius = _radius;
            clone.PrevVertex = _prevVertex;
            clone.NextVertex = _nextVertex;
            clone._hasNextVertex = _hasNextVertex;
            clone._hasPrevVertex = _hasPrevVertex;
            clone.Vertices = new Vertices(Vertices);
            clone.MassData = MassData;
            return clone;
        }
    }
}