/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common.Decomposition;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common
{
    /// <summary>
    /// An easy to use manager for creating paths.
    /// </summary>
    public static class PathManager
    {
        #region LinkType enum

        public enum LinkType
        {
            Revolute,
            Slider
        }

        #endregion

        //Contributed by Matthew Bettcher

        /// <summary>
        /// Convert a path into a set of edges and attaches them to the specified body.
        /// Note: use only for static edges.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="body">The body.</param>
        /// <param name="subdivisions">The subdivisions.</param>
        public static void ConvertPathToEdges(Path path, Body body, int subdivisions)
        {
            Vertices verts = path.GetVertices(subdivisions);

            if (path.Closed)
            {
                ChainShape chain = new ChainShape(verts, true);
                body.CreateFixture(chain, Category.Cat1, Category.All);
            }
            else
            {
                for (int i = 1; i < verts.Count; i++)
                {
                    body.CreateFixture(new EdgeShape(verts[i], verts[i - 1]), Category.Cat1, Category.All);
                }
            }
        }

        /// <summary>
        /// Convert a closed path into a polygon.
        /// Convex decomposition is automatically performed.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="body">The body.</param>
        /// <param name="density">The density.</param>
        /// <param name="subdivisions">The subdivisions.</param>
        public static void ConvertPathToPolygon(Path path, Body body, float density, int subdivisions)
        {
            if (!path.Closed)
                throw new Exception("The path must be closed to convert to a polygon.");

            List<Vector2> verts = path.GetVertices(subdivisions);

            List<Vertices> decomposedVerts = Triangulate.ConvexPartition(new Vertices(verts), TriangulationAlgorithm.Bayazit);

            foreach (Vertices item in decomposedVerts)
            {
                body.CreateFixture(new PolygonShape(item, density), Category.Cat1, Category.All);
            }
        }

        /// <summary>
        /// Duplicates the given Body along the given path for approximatly the given copies.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="path">The path.</param>
        /// <param name="shapes">The shapes.</param>
        /// <param name="type">The type.</param>
        /// <param name="copies">The copies.</param>
        /// <param name="userData"></param>
        /// <returns></returns>
        public static List<Body> EvenlyDistributeShapesAlongPath(World world, Path path, IEnumerable<Shape> shapes, BodyType type, int copies, object userData = null)
        {
            List<Vector3> centers = path.SubdivideEvenly(copies);
            List<Body> bodyList = new List<Body>();

            for (int i = 0; i < centers.Count; i++)
            {
                Body b = world.CreateBody();

                // copy the type from original body
                b.BodyType = type;
                b.Position = new Vector2(centers[i].X, centers[i].Y);
                b.Rotation = centers[i].Z;
                b.UserData = userData;

                foreach (Shape shape in shapes)
                {
                    b.CreateFixture(shape, Category.Cat1, Category.All);
                }

                bodyList.Add(b);
            }

            return bodyList;
        }


        /// <summary>
        /// Duplicates the given Body along the given path for approximatly the given copies.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="path">The path.</param>
        /// <param name="shape">The shape.</param>
        /// <param name="type">The type.</param>
        /// <param name="copies">The copies.</param>
        /// <param name="userData">The user data.</param>
        /// <returns></returns>
        public static List<Body> EvenlyDistributeShapesAlongPath(World world, Path path, Shape shape, BodyType type, int copies, object userData = null)
        {
            List<Shape> shapes = new List<Shape>(1);
            shapes.Add(shape);

            return EvenlyDistributeShapesAlongPath(world, path, shapes, type, copies, userData);
        }
        
        /// <summary>
        /// Moves the given body along the defined path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="body">The body.</param>
        /// <param name="time">The time.</param>
        /// <param name="strength">The strength.</param>
        /// <param name="timeStep">The time step.</param>
        public static void MoveBodyOnPath(Path path, Body body, float time, float strength, float timeStep)
        {
            Vector2 destination = path.GetPosition(time);
            Vector2 positionDelta = body.Position - destination;
            Vector2 velocity = (positionDelta / timeStep) * strength;

            body.LinearVelocity = -velocity;
        }

        /// <summary>
        /// Attaches the bodies with revolute joints.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="bodies">The bodies.</param>
        /// <param name="localAnchorA">The local anchor A.</param>
        /// <param name="localAnchorB">The local anchor B.</param>
        /// <param name="connectFirstAndLast">if set to <c>true</c> [connect first and last].</param>
        /// <param name="collideConnected">if set to <c>true</c> [collide connected].</param>
        public static List<RevoluteJoint> AttachBodiesWithRevoluteJoint(World world, List<Body> bodies, Vector2 localAnchorA, Vector2 localAnchorB, bool connectFirstAndLast, bool collideConnected)
        {
            List<RevoluteJoint> joints = new List<RevoluteJoint>(bodies.Count + 1);

            for (int i = 1; i < bodies.Count; i++)
            {
                RevoluteJoint joint = new RevoluteJoint(bodies[i], bodies[i - 1], localAnchorA, localAnchorB);
                joint.CollideConnected = collideConnected;
                world.Add(joint);
                joints.Add(joint);
            }

            if (connectFirstAndLast)
            {
                RevoluteJoint lastjoint = new RevoluteJoint(bodies[0], bodies[bodies.Count - 1], localAnchorA, localAnchorB);
                lastjoint.CollideConnected = collideConnected;
                world.Add(lastjoint);
                joints.Add(lastjoint);
            }

            return joints;
        }
    }
}