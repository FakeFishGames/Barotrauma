/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

/*
* Farseer Physics Engine:
* Copyright (c) 2012 Ian Qvist
*/

using System;

namespace FarseerPhysics.Diagnostics
{
    [Flags]
    public enum DebugViewFlags
    {
        /// <summary>
        /// Draw shapes.
        /// </summary>
        Shape = (1 << 0),

        /// <summary>
        /// Draw joint connections.
        /// </summary>
        Joint = (1 << 1),

        /// <summary>
        /// Draw axis aligned bounding boxes.
        /// </summary>
        AABB = (1 << 2),

        /// <summary>
        /// Draw broad-phase pairs.
        /// </summary>
        //Pair = (1 << 3),

        /// <summary>
        /// Draw center of mass frame.
        /// </summary>
        CenterOfMass = (1 << 4),

        /// <summary>
        /// Draw useful debug data such as timings and number of bodies, joints, contacts and more.
        /// </summary>
        DebugPanel = (1 << 5),

        /// <summary>
        /// Draw contact points between colliding bodies.
        /// </summary>
        ContactPoints = (1 << 6),

        /// <summary>
        /// Draw contact normals. Need ContactPoints to be enabled first.
        /// </summary>
        ContactNormals = (1 << 7),

        /// <summary>
        /// Draws the vertices of polygons.
        /// </summary>
        PolygonPoints = (1 << 8),

        /// <summary>
        /// Draws the performance graph.
        /// </summary>
        PerformanceGraph = (1 << 9),

        /// <summary>
        /// Draws controllers.
        /// </summary>
        Controllers = (1 << 10)
    }
}