// Copyright (c) 2017 Kastellanos Nikolaos

/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics.Joints;

namespace FarseerPhysics.Dynamics
{
    public partial class World
    {
        public virtual Body CreateBody(Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Body body = new Body();
            body.Position = position;
            body.Rotation = rotation;            
            body.BodyType = bodyType;
            
            AddAsync(body);

            return body;
        }

        public Body CreateEdge(Vector2 start, Vector2 end)
        {
            Body body = CreateBody();

            body.CreateEdge(start, end);
            return body;
        }

        public Body CreateChainShape(Vertices vertices, Vector2 position = new Vector2())
        {
            Body body = CreateBody(position);

            body.CreateChainShape(vertices);
            return body;
        }

        public Body CreateLoopShape(Vertices vertices, Vector2 position = new Vector2())
        {
            Body body = CreateBody(position);

            body.CreateLoopShape(vertices);
            return body;
        }

        public Body CreateRectangle(float width, float height, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width", "Width must be more than 0 meters");

            if (height <= 0)
                throw new ArgumentOutOfRangeException("height", "Height must be more than 0 meters");

            Body body = CreateBody(position, rotation, bodyType);

            Vertices rectangleVertices = PolygonTools.CreateRectangle(width / 2, height / 2);
            body.CreatePolygon(rectangleVertices, density);

            return body;
        }

        public Body CreateCircle(float radius, float density, Vector2 position = new Vector2(), BodyType bodyType = BodyType.Static)
        {
            Body body = CreateBody(position, 0, bodyType);
            body.CreateCircle(radius, density);
            return body;
        }

        public Body CreateEllipse(float xRadius, float yRadius, int edges, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Body body = CreateBody(position, rotation, bodyType);
            body.CreateEllipse(xRadius, yRadius, edges, density);
            return body;
        }

        public Body CreatePolygon(Vertices vertices, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Body body = CreateBody(position, rotation, bodyType);
            body.CreatePolygon(vertices, density);
            return body;
        }

        public Body CreateCompoundPolygon(List<Vertices> list, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            //We create a single body
            Body body = CreateBody(position, rotation, bodyType);
            body.CreateCompoundPolygon(list, density);
            return body;
        }

        public Body CreateGear(float radius, int numberOfTeeth, float tipPercentage, float toothHeight, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Vertices gearPolygon = PolygonTools.CreateGear(radius, numberOfTeeth, tipPercentage, toothHeight);

            //Gears can in some cases be convex
            if (!gearPolygon.IsConvex())
            {
                //Decompose the gear:
                List<Vertices> list = Triangulate.ConvexPartition(gearPolygon, TriangulationAlgorithm.Earclip);

                return CreateCompoundPolygon(list, density, position, rotation, bodyType);
            }

            return CreatePolygon(gearPolygon, density, position, rotation, bodyType);
        }

        public Body CreateCapsule(float height, float topRadius, int topEdges, float bottomRadius, int bottomEdges, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Vertices verts = PolygonTools.CreateCapsule(height, topRadius, topEdges, bottomRadius, bottomEdges);

            //There are too many vertices in the capsule. We decompose it.
            if (verts.Count >= Settings.MaxPolygonVertices)
            {
                List<Vertices> vertList = Triangulate.ConvexPartition(verts, TriangulationAlgorithm.Earclip);
                return CreateCompoundPolygon(vertList, density, position, rotation, bodyType);
            }

            return CreatePolygon(verts, density, position, rotation, bodyType);
        }

        public Body CreateCapsule(float width, float endRadius, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            //Create the middle rectangle
            Vertices rectangle = PolygonTools.CreateRectangle(width / 2, endRadius);

            List<Vertices> list = new List<Vertices>();
            list.Add(rectangle);

            Body body = CreateCompoundPolygon(list, density, position, rotation, bodyType);
            body.CreateCircle(endRadius, density, new Vector2(width / 2, 0));
            body.CreateCircle(endRadius, density, new Vector2(-width / 2, 0));

            //Create the two circles
            //CircleShape topCircle = new CircleShape(endRadius, density);
            //topCircle.Position = new Vector2(0, height / 2);
            //body.CreateFixture(topCircle);

            //CircleShape bottomCircle = new CircleShape(endRadius, density);
            //bottomCircle.Position = new Vector2(0, -(height / 2));
            //body.CreateFixture(bottomCircle);
            return body;
        }
        public Body CreateCapsuleHorizontal(float height, float endRadius, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            //Create the middle rectangle
            Vertices rectangle = PolygonTools.CreateRectangle(endRadius, height / 2);

            List<Vertices> list = new List<Vertices>();
            list.Add(rectangle);

            Body body = CreateCompoundPolygon(list, density, position, rotation, bodyType);
            body.CreateCircle(endRadius, density, new Vector2(0, height / 2));
            body.CreateCircle(endRadius, density, new Vector2(0, -(height / 2)));

            //Create the two circles
            //CircleShape topCircle = new CircleShape(endRadius, density);
            //topCircle.Position = new Vector2(0, height / 2);
            //body.CreateFixture(topCircle);

            //CircleShape bottomCircle = new CircleShape(endRadius, density);
            //bottomCircle.Position = new Vector2(0, -(height / 2));
            //body.CreateFixture(bottomCircle);
            return body;
        }

        public Body CreateRoundedRectangle(float width, float height, float xRadius, float yRadius, int segments, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Vertices verts = PolygonTools.CreateRoundedRectangle(width, height, xRadius, yRadius, segments);

            //There are too many vertices in the capsule. We decompose it.
            if (verts.Count >= Settings.MaxPolygonVertices)
            {
                List<Vertices> vertList = Triangulate.ConvexPartition(verts, TriangulationAlgorithm.Earclip);
                return CreateCompoundPolygon(vertList, density, position, rotation, bodyType);
            }

            return CreatePolygon(verts, density, position, rotation, bodyType);
        }

        public Body CreateLineArc(float radians, int sides, float radius, bool closed = false, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Body body = CreateBody(position, rotation, bodyType);
            body.CreateLineArc(radians, sides, radius, closed);
            return body;
        }

        public Body CreateSolidArc(float density, float radians, int sides, float radius, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static)
        {
            Body body = CreateBody(position, rotation, bodyType);
            body.CreateSolidArc(density, radians, sides, radius);

            return body;
        }

        /// <summary>
        /// Creates a chain.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="start">The start.</param>
        /// <param name="end">The end.</param>
        /// <param name="linkWidth">The width.</param>
        /// <param name="linkHeight">The height.</param>
        /// <param name="numberOfLinks">The number of links.</param>
        /// <param name="linkDensity">The link density.</param>
        /// <param name="attachRopeJoint">Creates a rope joint between start and end. This enforces the length of the rope. Said in another way: it makes the rope less bouncy.</param>
        /// <returns></returns>
        public Path CreateChain(Vector2 start, Vector2 end, float linkWidth, float linkHeight, int numberOfLinks, float linkDensity, bool attachRopeJoint)
        {
            System.Diagnostics.Debug.Assert(numberOfLinks >= 2);

            //Chain start / end
            Path path = new Path();
            path.Add(start);
            path.Add(end);

            //A single chainlink
            PolygonShape shape = new PolygonShape(PolygonTools.CreateRectangle(linkWidth, linkHeight), linkDensity);

            //Use PathManager to create all the chainlinks based on the chainlink created before.
            List<Body> chainLinks = PathManager.EvenlyDistributeShapesAlongPath(this, path, shape, BodyType.Dynamic, numberOfLinks);

            //TODO
            //if (fixStart)
            //{
            //    //Fix the first chainlink to the world
            //    JointFactory.CreateFixedRevoluteJoint(this, chainLinks[0], new Vector2(0, -(linkHeight / 2)),
            //                                          chainLinks[0].Position);
            //}

            //if (fixEnd)
            //{
            //    //Fix the last chainlink to the world
            //    JointFactory.CreateFixedRevoluteJoint(this, chainLinks[chainLinks.Count - 1],
            //                                          new Vector2(0, (linkHeight / 2)),
            //                                          chainLinks[chainLinks.Count - 1].Position);
            //}

            //Attach all the chainlinks together with a revolute joint
            PathManager.AttachBodiesWithRevoluteJoint(this, chainLinks, new Vector2(0, -linkHeight), new Vector2(0, linkHeight), false, false);

            if (attachRopeJoint)
                JointFactory.CreateRopeJoint(this, chainLinks[0], chainLinks[chainLinks.Count - 1], Vector2.Zero, Vector2.Zero);

            return path;
        }



    }
}