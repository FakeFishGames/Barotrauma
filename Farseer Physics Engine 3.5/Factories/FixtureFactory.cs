using System;
using System.Collections.Generic;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Factories
{
    /// <summary>
    /// An easy to use factory for creating bodies
    /// </summary>
    public static class FixtureFactory
    {
        public static Fixture AttachEdge(Vector2 start, Vector2 end, Body body, object userData = null)
        {
            EdgeShape edgeShape = new EdgeShape(start, end);
            return body.CreateFixture(edgeShape, userData);
        }

        public static Fixture AttachChainShape(Vertices vertices, Body body, object userData = null)
        {
            ChainShape shape = new ChainShape(vertices);
            return body.CreateFixture(shape, userData);
        }

        public static Fixture AttachLoopShape(Vertices vertices, Body body, object userData = null)
        {
            ChainShape shape = new ChainShape(vertices, true);
            return body.CreateFixture(shape, userData);
        }

        public static Fixture AttachRectangle(float width, float height, float density, Vector2 offset, Body body, object userData = null)
        {
            Vertices rectangleVertices = PolygonTools.CreateRectangle(width / 2, height / 2);
            rectangleVertices.Translate(ref offset);
            PolygonShape rectangleShape = new PolygonShape(rectangleVertices, density);
            return body.CreateFixture(rectangleShape, userData);
        }

        public static Fixture AttachCircle(float radius, float density, Body body, object userData = null)
        {
            if (radius <= 0)
                throw new ArgumentOutOfRangeException("radius", "Radius must be more than 0 meters");

            CircleShape circleShape = new CircleShape(radius, density);
            return body.CreateFixture(circleShape, userData);
        }

        public static Fixture AttachCircle(float radius, float density, Body body, Vector2 offset, object userData = null)
        {
            if (radius <= 0)
                throw new ArgumentOutOfRangeException("radius", "Radius must be more than 0 meters");

            CircleShape circleShape = new CircleShape(radius, density);
            circleShape.Position = offset;
            return body.CreateFixture(circleShape, userData);
        }

        public static Fixture AttachPolygon(Vertices vertices, float density, Body body, object userData = null)
        {
            if (vertices.Count <= 1)
                throw new ArgumentOutOfRangeException("vertices", "Too few points to be a polygon");

            PolygonShape polygon = new PolygonShape(vertices, density);
            return body.CreateFixture(polygon, userData);
        }

        public static Fixture AttachEllipse(float xRadius, float yRadius, int edges, float density, Body body, object userData = null)
        {
            if (xRadius <= 0)
                throw new ArgumentOutOfRangeException("xRadius", "X-radius must be more than 0");

            if (yRadius <= 0)
                throw new ArgumentOutOfRangeException("yRadius", "Y-radius must be more than 0");

            Vertices ellipseVertices = PolygonTools.CreateEllipse(xRadius, yRadius, edges);
            PolygonShape polygonShape = new PolygonShape(ellipseVertices, density);
            return body.CreateFixture(polygonShape, userData);
        }

        public static List<Fixture> AttachCompoundPolygon(List<Vertices> list, float density, Body body, object userData = null)
        {
            List<Fixture> res = new List<Fixture>(list.Count);

            //Then we create several fixtures using the body
            foreach (Vertices vertices in list)
            {
                if (vertices.Count == 2)
                {
                    EdgeShape shape = new EdgeShape(vertices[0], vertices[1]);
                    res.Add(body.CreateFixture(shape, userData));
                }
                else
                {
                    PolygonShape shape = new PolygonShape(vertices, density);
                    res.Add(body.CreateFixture(shape, userData));
                }
            }

            return res;
        }

        public static Fixture AttachLineArc(float radians, int sides, float radius, Vector2 position, float angle, bool closed, Body body)
        {
            Vertices arc = PolygonTools.CreateArc(radians, sides, radius);
            arc.Rotate((MathHelper.Pi - radians) / 2 + angle);
            arc.Translate(ref position);

            return closed ? AttachLoopShape(arc, body) : AttachChainShape(arc, body);
        }

        public static List<Fixture> AttachSolidArc(float density, float radians, int sides, float radius, Vector2 position, float angle, Body body)
        {
            Vertices arc = PolygonTools.CreateArc(radians, sides, radius);
            arc.Rotate((MathHelper.Pi - radians) / 2 + angle);

            arc.Translate(ref position);

            //Close the arc
            arc.Add(arc[0]);

            List<Vertices> triangles = Triangulate.ConvexPartition(arc, TriangulationAlgorithm.Earclip);

            return AttachCompoundPolygon(triangles, density, body);
        }
    }
}