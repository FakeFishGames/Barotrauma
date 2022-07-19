/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Dynamics
{
    /// <summary>
    /// An easy to use factory for creating bodies
    /// </summary>
    public partial class Body
    {
        /// <summary>
        /// Creates a fixture and attach it to this body.
        /// If the density is non-zero, this function automatically updates the mass of the body.
        /// Contacts are not created until the next time step.
        /// Warning: This method is locked during callbacks.
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="userData">Application specific data</param>
        /// <returns></returns>
        public virtual Fixture CreateFixture(Shape shape, Category collisionCategory, Category collidesWith)
        {
            Fixture fixture = new Fixture(shape, collisionCategory, collidesWith);
            Add(fixture);
            return fixture;
        }

        public Fixture CreateEdge(Vector2 start, Vector2 end, Category collisionCategory, Category collidesWith)
        {
            EdgeShape edgeShape = new EdgeShape(start, end);
            return CreateFixture(edgeShape, collisionCategory, collidesWith);
        }

        public Fixture CreateChainShape(Vertices vertices, Category collisionCategory, Category collidesWith)
        {
            ChainShape shape = new ChainShape(vertices);
            return CreateFixture(shape, collisionCategory, collidesWith);
        }

        public Fixture CreateLoopShape(Vertices vertices, Category collisionCategory, Category collidesWith)
        {
            ChainShape shape = new ChainShape(vertices, true);
            return CreateFixture(shape, collisionCategory, collidesWith);
        }

        public Fixture CreateRectangle(float width, float height, float density, Vector2 offset, Category collisionCategory, Category collidesWith)
        {
            Vertices rectangleVertices = PolygonTools.CreateRectangle(width / 2, height / 2);
            rectangleVertices.Translate(ref offset);
            PolygonShape rectangleShape = new PolygonShape(rectangleVertices, density);
            return CreateFixture(rectangleShape, collisionCategory, collidesWith);
        }

        public Fixture CreateRectangle(float width, float height, float density, float rotation, Vector2 offset, Category collisionCategory, Category collidesWith)
        {
            Vertices rectangleVertices = PolygonTools.CreateRectangle(width / 2, height / 2, Vector2.Zero, rotation);
            rectangleVertices.Translate(ref offset);
            PolygonShape rectangleShape = new PolygonShape(rectangleVertices, density);
            return CreateFixture(rectangleShape, collisionCategory, collidesWith);
        }

        public Fixture CreateCircle(float radius, float density, Category collisionCategory, Category collidesWith)
        {
            if (radius <= 0)
                throw new ArgumentOutOfRangeException("radius", "Radius must be more than 0 meters");

            CircleShape circleShape = new CircleShape(radius, density);
            return CreateFixture(circleShape, collisionCategory, collidesWith);
        }

        public Fixture CreateCircle(float radius, float density, Vector2 offset, Category collisionCategory, Category collidesWith)
        {
            if (radius <= 0)
                throw new ArgumentOutOfRangeException("radius", "Radius must be more than 0 meters");

            CircleShape circleShape = new CircleShape(radius, density);
            circleShape.Position = offset;
            return CreateFixture(circleShape, collisionCategory, collidesWith);
        }

        public Fixture CreatePolygon(Vertices vertices, float density, Category collisionCategory, Category collidesWith)
        {
            if (vertices.Count <= 1)
                throw new ArgumentOutOfRangeException("vertices", "Too few points to be a polygon");

            PolygonShape polygon = new PolygonShape(vertices, density);
            return CreateFixture(polygon, collisionCategory, collidesWith);
        }

        public Fixture CreateEllipse(float xRadius, float yRadius, int edges, float density, Category collisionCategory, Category collidesWith)
        {
            if (xRadius <= 0)
                throw new ArgumentOutOfRangeException("xRadius", "X-radius must be more than 0");

            if (yRadius <= 0)
                throw new ArgumentOutOfRangeException("yRadius", "Y-radius must be more than 0");

            Vertices ellipseVertices = PolygonTools.CreateEllipse(xRadius, yRadius, edges);
            PolygonShape polygonShape = new PolygonShape(ellipseVertices, density);
            return CreateFixture(polygonShape, collisionCategory, collidesWith);
        }

        public List<Fixture> CreateCompoundPolygon(List<Vertices> list, float density, Category collisionCategory, Category collidesWith)
        {
            List<Fixture> res = new List<Fixture>(list.Count);

            //Then we create several fixtures using the body
            foreach (Vertices vertices in list)
            {
                if (vertices.Count == 2)
                {
                    EdgeShape shape = new EdgeShape(vertices[0], vertices[1]);
                    res.Add(CreateFixture(shape, collisionCategory, collidesWith));
                }
                else
                {
                    PolygonShape shape = new PolygonShape(vertices, density);
                    res.Add(CreateFixture(shape, collisionCategory, collidesWith));
                }
            }

            return res;
        }

        public Fixture CreateLineArc(float radians, int sides, float radius, bool closed, Category collisionCategory, Category collidesWith)
        {
            Vertices arc = PolygonTools.CreateArc(radians, sides, radius);
            arc.Rotate((MathHelper.Pi - radians) / 2);
            return closed ? CreateLoopShape(arc, collisionCategory, collidesWith) : CreateChainShape(arc, collisionCategory, collidesWith);
        }

        public List<Fixture> CreateSolidArc(float density, float radians, int sides, float radius, Category collisionCategory, Category collidesWith)
        {
            Vertices arc = PolygonTools.CreateArc(radians, sides, radius);
            arc.Rotate((MathHelper.Pi - radians) / 2);

            //Close the arc
            arc.Add(arc[0]);

            List<Vertices> triangles = Triangulate.ConvexPartition(arc, TriangulationAlgorithm.Earclip);

            return CreateCompoundPolygon(triangles, density, collisionCategory, collidesWith);
        }
    }
}