using System;
using System.Collections.Generic;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Factories
{
    public static class BodyFactory
    {
        public static Body CreateBody(World world, object userData = null)
        {
            Body body = new Body(world, null, 0, userData);
            return body;
        }

        public static Body CreateBody(World world, Vector2 position, float rotation = 0, object userData = null)
        {
            Body body = new Body(world, position, rotation, userData);
            return body;
        }

        public static Body CreateEdge(World world, Vector2 start, Vector2 end, object userData = null)
        {
            Body body = CreateBody(world);
            FixtureFactory.AttachEdge(start, end, body, userData);
            return body;
        }

        public static Body CreateChainShape(World world, Vertices vertices, object userData = null)
        {
            return CreateChainShape(world, vertices, Vector2.Zero, userData);
        }

        public static Body CreateChainShape(World world, Vertices vertices, Vector2 position, object userData = null)
        {
            Body body = CreateBody(world, position);
            FixtureFactory.AttachChainShape(vertices, body, userData);
            return body;
        }

        public static Body CreateLoopShape(World world, Vertices vertices, object userData = null)
        {
            return CreateLoopShape(world, vertices, Vector2.Zero, userData);
        }

        public static Body CreateLoopShape(World world, Vertices vertices, Vector2 position, object userData = null)
        {
            Body body = CreateBody(world, position);
            FixtureFactory.AttachLoopShape(vertices, body, userData);
            return body;
        }

        public static Body CreateRectangle(World world, float width, float height, float density, object userData = null)
        {
            return CreateRectangle(world, width, height, density, Vector2.Zero, userData);
        }

        public static Body CreateRectangle(World world, float width, float height, float density, Vector2 position, object userData = null)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width", "Width must be more than 0 meters");

            if (height <= 0)
                throw new ArgumentOutOfRangeException("height", "Height must be more than 0 meters");

            Body newBody = CreateBody(world, position);
            newBody.UserData = userData;

            Vertices rectangleVertices = PolygonTools.CreateRectangle(width / 2, height / 2);
            PolygonShape rectangleShape = new PolygonShape(rectangleVertices, density);
            newBody.CreateFixture(rectangleShape);

            return newBody;
        }

        public static Body CreateCircle(World world, float radius, float density, object userData = null)
        {
            return CreateCircle(world, radius, density, Vector2.Zero, userData);
        }

        public static Body CreateCircle(World world, float radius, float density, Vector2 position, object userData = null)
        {
            Body body = CreateBody(world, position);
            FixtureFactory.AttachCircle(radius, density, body, userData);
            return body;
        }

        public static Body CreateEllipse(World world, float xRadius, float yRadius, int edges, float density, object userData = null)
        {
            return CreateEllipse(world, xRadius, yRadius, edges, density, Vector2.Zero, userData);
        }

        public static Body CreateEllipse(World world, float xRadius, float yRadius, int edges, float density,
                                         Vector2 position, object userData = null)
        {
            Body body = CreateBody(world, position);
            FixtureFactory.AttachEllipse(xRadius, yRadius, edges, density, body, userData);
            return body;
        }

        public static Body CreatePolygon(World world, Vertices vertices, float density, object userData = null)
        {
            return CreatePolygon(world, vertices, density, Vector2.Zero, userData);
        }

        public static Body CreatePolygon(World world, Vertices vertices, float density, Vector2 position, object userData = null)
        {
            Body body = CreateBody(world, position);
            FixtureFactory.AttachPolygon(vertices, density, body, userData);
            return body;
        }

        public static Body CreateCompoundPolygon(World world, List<Vertices> list, float density, object userData = null)
        {
            return CreateCompoundPolygon(world, list, density, Vector2.Zero, userData);
        }

        public static Body CreateCompoundPolygon(World world, List<Vertices> list, float density, Vector2 position, object userData = null)
        {
            //We create a single body
            Body polygonBody = CreateBody(world, position);
            FixtureFactory.AttachCompoundPolygon(list, density, polygonBody, userData);
            return polygonBody;
        }

        public static Body CreateGear(World world, float radius, int numberOfTeeth, float tipPercentage, float toothHeight, float density, object userData = null)
        {
            Vertices gearPolygon = PolygonTools.CreateGear(radius, numberOfTeeth, tipPercentage, toothHeight);

            //Gears can in some cases be convex
            if (!gearPolygon.IsConvex())
            {
                //Decompose the gear:
                List<Vertices> list = Triangulate.ConvexPartition(gearPolygon, TriangulationAlgorithm.Earclip);

                return CreateCompoundPolygon(world, list, density, userData);
            }

            return CreatePolygon(world, gearPolygon, density, userData);
        }

        /// <summary>
        /// Creates a capsule.
        /// Note: Automatically decomposes the capsule if it contains too many vertices (controlled by Settings.MaxPolygonVertices)
        /// </summary>
        /// <returns></returns>
        public static Body CreateCapsule(World world, float height, float topRadius, int topEdges, float bottomRadius, int bottomEdges, float density, Vector2 position, object userData = null)
        {
            Vertices verts = PolygonTools.CreateCapsule(height, topRadius, topEdges, bottomRadius, bottomEdges);

            Body body;

            //There are too many vertices in the capsule. We decompose it.
            if (verts.Count >= Settings.MaxPolygonVertices)
            {
                List<Vertices> vertList = Triangulate.ConvexPartition(verts, TriangulationAlgorithm.Earclip);
                body = CreateCompoundPolygon(world, vertList, density, userData);
                body.Position = position;

                return body;
            }

            body = CreatePolygon(world, verts, density, userData);
            body.Position = position;

            return body;
        }

        public static Body CreateCapsule(World world, float height, float endRadius, float density,
                                         object userData = null)
        {
            //Create the middle rectangle
            Vertices rectangle = PolygonTools.CreateRectangle(endRadius, height / 2);

            List<Vertices> list = new List<Vertices>();
            list.Add(rectangle);

            Body body = CreateCompoundPolygon(world, list, density, userData);
            body.UserData = userData;

            //Create the two circles
            CircleShape topCircle = new CircleShape(endRadius, density);
            topCircle.Position = new Vector2(0, height / 2);
            body.CreateFixture(topCircle);

            CircleShape bottomCircle = new CircleShape(endRadius, density);
            bottomCircle.Position = new Vector2(0, -(height / 2));
            body.CreateFixture(bottomCircle);
            return body;
        }

        /// <summary>
        /// Creates a rounded rectangle.
        /// Note: Automatically decomposes the capsule if it contains too many vertices (controlled by Settings.MaxPolygonVertices)
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="width">The width.</param>
        /// <param name="height">The height.</param>
        /// <param name="xRadius">The x radius.</param>
        /// <param name="yRadius">The y radius.</param>
        /// <param name="segments">The segments.</param>
        /// <param name="density">The density.</param>
        /// <param name="position">The position.</param>
        /// <returns></returns>
        public static Body CreateRoundedRectangle(World world, float width, float height, float xRadius, float yRadius, int segments, float density, Vector2 position, object userData = null)
        {
            Vertices verts = PolygonTools.CreateRoundedRectangle(width, height, xRadius, yRadius, segments);

            //There are too many vertices in the capsule. We decompose it.
            if (verts.Count >= Settings.MaxPolygonVertices)
            {
                List<Vertices> vertList = Triangulate.ConvexPartition(verts, TriangulationAlgorithm.Earclip);
                Body body = CreateCompoundPolygon(world, vertList, density, userData);
                body.Position = position;
                return body;
            }

            return CreatePolygon(world, verts, density);
        }

        public static Body CreateRoundedRectangle(World world, float width, float height, float xRadius, float yRadius, int segments, float density, object userData = null)
        {
            return CreateRoundedRectangle(world, width, height, xRadius, yRadius, segments, density, Vector2.Zero, userData);
        }

        public static BreakableBody CreateBreakableBody(World world, Vertices vertices, float density)
        {
            return CreateBreakableBody(world, vertices, density, Vector2.Zero);
        }

        public static BreakableBody CreateBreakableBody(World world, IEnumerable<Shape> shapes)
        {
            return CreateBreakableBody(world, shapes, Vector2.Zero);
        }

        /// <summary>
        /// Creates a breakable body. You would want to remove collinear points before using this.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="vertices">The vertices.</param>
        /// <param name="density">The density.</param>
        /// <param name="position">The position.</param>
        /// <returns></returns>
        public static BreakableBody CreateBreakableBody(World world, Vertices vertices, float density, Vector2 position)
        {
            List<Vertices> triangles = Triangulate.ConvexPartition(vertices, TriangulationAlgorithm.Earclip);

            BreakableBody breakableBody = new BreakableBody(triangles, world, density);
            breakableBody.MainBody.Position = position;
            world.AddBreakableBody(breakableBody);

            return breakableBody;
        }

        public static BreakableBody CreateBreakableBody(World world, IEnumerable<Shape> shapes, Vector2 position)
        {
            BreakableBody breakableBody = new BreakableBody(shapes, world);
            breakableBody.MainBody.Position = position;
            world.AddBreakableBody(breakableBody);

            return breakableBody;
        }

        public static Body CreateLineArc(World world, float radians, int sides, float radius, Vector2 position, float angle, bool closed)
        {
            Body body = CreateBody(world);
            FixtureFactory.AttachLineArc(radians, sides, radius, position, angle, closed, body);
            return body;
        }

        public static Body CreateSolidArc(World world, float density, float radians, int sides, float radius, Vector2 position, float angle)
        {
            Body body = CreateBody(world);
            FixtureFactory.AttachSolidArc(density, radians, sides, radius, position, angle, body);
            return body;
        }
    }
}