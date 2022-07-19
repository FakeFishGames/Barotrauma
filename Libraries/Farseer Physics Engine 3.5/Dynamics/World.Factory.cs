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
        public virtual Body CreateBody(Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, bool findNewContacts = true)
        {
            Body body = new Body
            {
                Position = position,
                Rotation = rotation,
                BodyType = bodyType
            };

            AddAsync(body, findNewContacts);

            return body;
        }

        public Body CreateEdge(Vector2 start, Vector2 end, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Body body = CreateBody(bodyType: bodyType, findNewContacts: findNewContacts);
            body.CreateEdge(start, end, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateChainShape(Vertices vertices, Vector2 position = new Vector2(), Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Body body = CreateBody(position, findNewContacts: findNewContacts);
            body.CreateChainShape(vertices, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateLoopShape(Vertices vertices, Vector2 position = new Vector2(), Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Body body = CreateBody(position, findNewContacts: findNewContacts);
            body.CreateLoopShape(vertices, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateRectangle(float width, float height, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException("width", "Width must be more than 0 meters");

            if (height <= 0)
                throw new ArgumentOutOfRangeException("height", "Height must be more than 0 meters");

            Body body = CreateBody(position, rotation, bodyType, findNewContacts);

            Vertices rectangleVertices = PolygonTools.CreateRectangle(width / 2, height / 2);
            body.CreatePolygon(rectangleVertices, density, collisionCategory, collidesWith);

            return body;
        }

        public Body CreateCircle(float radius, float density, Vector2 position = new Vector2(), BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Body body = CreateBody(position, 0, bodyType, findNewContacts);
            body.CreateCircle(radius, density, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateEllipse(float xRadius, float yRadius, int edges, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Body body = CreateBody(position, rotation, bodyType, findNewContacts);
            body.CreateEllipse(xRadius, yRadius, edges, density, collisionCategory, collidesWith);
            return body;
        }

        public Body CreatePolygon(Vertices vertices, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Body body = CreateBody(position, rotation, bodyType, findNewContacts);
            body.CreatePolygon(vertices, density, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateCompoundPolygon(List<Vertices> list, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            //We create a single body
            Body body = CreateBody(position, rotation, bodyType, findNewContacts);
            body.CreateCompoundPolygon(list, density, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateGear(float radius, int numberOfTeeth, float tipPercentage, float toothHeight, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All)
        {
            Vertices gearPolygon = PolygonTools.CreateGear(radius, numberOfTeeth, tipPercentage, toothHeight);

            //Gears can in some cases be convex
            if (!gearPolygon.IsConvex())
            {
                //Decompose the gear:
                List<Vertices> list = Triangulate.ConvexPartition(gearPolygon, TriangulationAlgorithm.Earclip);

                return CreateCompoundPolygon(list, density, position, rotation, bodyType, collisionCategory, collidesWith);
            }

            return CreatePolygon(gearPolygon, density, position, rotation, bodyType, collisionCategory, collidesWith);
        }

        public Body CreateCapsule(float height, float topRadius, int topEdges, float bottomRadius, int bottomEdges, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            Vertices verts = PolygonTools.CreateCapsule(height, topRadius, topEdges, bottomRadius, bottomEdges);

            //There are too many vertices in the capsule. We decompose it.
            if (verts.Count >= Settings.MaxPolygonVertices)
            {
                List<Vertices> vertList = Triangulate.ConvexPartition(verts, TriangulationAlgorithm.Earclip);
                return CreateCompoundPolygon(vertList, density, position, rotation, bodyType, collisionCategory, collidesWith, findNewContacts);
            }

            return CreatePolygon(verts, density, position, rotation, bodyType, collisionCategory, collidesWith, findNewContacts);
        }

        public Body CreateCapsuleHorizontal(float width, float endRadius, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            //Create the middle rectangle
            Vertices rectangle = PolygonTools.CreateRectangle(width / 2, endRadius);

            List<Vertices> list = new List<Vertices>
            {
                rectangle
            };

            Body body = CreateCompoundPolygon(list, density, position, rotation, bodyType, collisionCategory, collidesWith, findNewContacts);
            body.CreateCircle(endRadius, density, new Vector2(width / 2, 0), collisionCategory, collidesWith);
            body.CreateCircle(endRadius, density, new Vector2(-width / 2, 0), collisionCategory, collidesWith);

            //Create the two circles
            //CircleShape topCircle = new CircleShape(endRadius, density);
            //topCircle.Position = new Vector2(0, height / 2);
            //body.CreateFixture(topCircle);

            //CircleShape bottomCircle = new CircleShape(endRadius, density);
            //bottomCircle.Position = new Vector2(0, -(height / 2));
            //body.CreateFixture(bottomCircle);
            return body;
        }
        public Body CreateCapsule(float height, float endRadius, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All, bool findNewContacts = true)
        {
            //Create the middle rectangle
            Vertices rectangle = PolygonTools.CreateRectangle(endRadius, height / 2);

            List<Vertices> list = new List<Vertices>()
            {
                rectangle
            };

            Body body = CreateCompoundPolygon(list, density, position, rotation, bodyType, collisionCategory, collidesWith, findNewContacts);
            body.CreateCircle(endRadius, density, new Vector2(0, height / 2), collisionCategory, collidesWith);
            body.CreateCircle(endRadius, density, new Vector2(0, -(height / 2)), collisionCategory, collidesWith);

            //Create the two circles
            //CircleShape topCircle = new CircleShape(endRadius, density);
            //topCircle.Position = new Vector2(0, height / 2);
            //body.CreateFixture(topCircle);

            //CircleShape bottomCircle = new CircleShape(endRadius, density);
            //bottomCircle.Position = new Vector2(0, -(height / 2));
            //body.CreateFixture(bottomCircle);
            return body;
        }

        public Body CreateRoundedRectangle(float width, float height, float xRadius, float yRadius, int segments, float density, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All)
        {
            Vertices verts = PolygonTools.CreateRoundedRectangle(width, height, xRadius, yRadius, segments);

            //There are too many vertices in the capsule. We decompose it.
            if (verts.Count >= Settings.MaxPolygonVertices)
            {
                List<Vertices> vertList = Triangulate.ConvexPartition(verts, TriangulationAlgorithm.Earclip);
                return CreateCompoundPolygon(vertList, density, position, rotation, bodyType, collisionCategory, collidesWith);
            }

            return CreatePolygon(verts, density, position, rotation, bodyType, collisionCategory, collidesWith);
        }

        public Body CreateLineArc(float radians, int sides, float radius, bool closed = false, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All)
        {
            Body body = CreateBody(position, rotation, bodyType);
            body.CreateLineArc(radians, sides, radius, closed, collisionCategory, collidesWith);
            return body;
        }

        public Body CreateSolidArc(float density, float radians, int sides, float radius, Vector2 position = new Vector2(), float rotation = 0, BodyType bodyType = BodyType.Static, Category collisionCategory = Category.Cat1, Category collidesWith = Category.All)
        {
            Body body = CreateBody(position, rotation, bodyType);
            body.CreateSolidArc(density, radians, sides, radius, collisionCategory, collidesWith);

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