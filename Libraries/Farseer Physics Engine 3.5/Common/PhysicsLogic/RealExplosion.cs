/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using FarseerPhysics.Collision;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Common.PhysicsLogic
{
    // Original Code by Steven Lu - see http://www.box2d.org/forum/viewtopic.php?f=3&t=1688
    // Ported to Farseer 3.0 by Nicolás Hormazábal

    internal struct ShapeData
    {
        public Body Body;
        public float Max;
        public float Min; // absolute angles
    }

    /// <summary>
    /// This is a comprarer used for 
    /// detecting angle difference between rays
    /// </summary>
    internal class RayDataComparer : IComparer<float>
    {
        #region IComparer<float> Members

        int IComparer<float>.Compare(float a, float b)
        {
            float diff = (a - b);
            if (diff > 0)
                return 1;
            if (diff < 0)
                return -1;
            return 0;
        }

        #endregion
    }

    /* Methodology:
     * Force applied at a ray is inversely proportional to the square of distance from source
     * AABB is used to query for shapes that may be affected
     * For each RIGID BODY (not shape -- this is an optimization) that is matched, loop through its vertices to determine
     *		the extreme points -- if there is structure that contains outlining polygon, use that as an additional optimization
     * Evenly cast a number of rays against the shape - number roughly proportional to the arc coverage
     *		- Something like every 3 degrees should do the trick although this can be altered depending on the distance (if really close don't need such a high density of rays)
     *		- There should be a minimum number of rays (3-5?) applied to each body so that small bodies far away are still accurately modeled
     *		- Be sure to have the forces of each ray be proportional to the average arc length covered by each.
     * For each ray that actually intersects with the shape (non intersections indicate something blocking the path of explosion):
     *		- Apply the appropriate force dotted with the negative of the collision normal at the collision point
     *		- Optionally apply linear interpolation between aforementioned Normal force and the original explosion force in the direction of ray to simulate "surface friction" of sorts
     */

    /// <summary>
    /// Creates a realistic explosion based on raycasting. Objects in the open will be affected, but objects behind
    /// static bodies will not. A body that is half in cover, half in the open will get half the force applied to the end in
    /// the open.
    /// </summary>
    public sealed class RealExplosion : PhysicsLogic
    {
        /// <summary>
        /// Two degrees: maximum angle from edges to first ray tested
        /// </summary>
        private const float MaxEdgeOffset = MathHelper.Pi / 90;

        /// <summary>
        /// Ratio of arc length to angle from edges to first ray tested.
        /// Defaults to 1/40.
        /// </summary>
        public float EdgeRatio = 1.0f / 40.0f;

        /// <summary>
        /// Ignore Explosion if it happens inside a shape.
        /// Default value is false.
        /// </summary>
        public bool IgnoreWhenInsideShape = false;

        /// <summary>
        /// Max angle between rays (used when segment is large).
        /// Defaults to 15 degrees
        /// </summary>
        public float MaxAngle = MathHelper.Pi / 15;

        /// <summary>
        /// Maximum number of shapes involved in the explosion.
        /// Defaults to 100
        /// </summary>
        public int MaxShapes = 100;

        /// <summary>
        /// How many rays per shape/body/segment.
        /// Defaults to 5
        /// </summary>
        public int MinRays = 5;

        private List<ShapeData> _data = new List<ShapeData>();
        private RayDataComparer _rdc;

        public RealExplosion(World world) : base(world)
        {
            _rdc = new RayDataComparer();
            _data = new List<ShapeData>();
        }

        /// <summary>
        /// Activate the explosion at the specified position.
        /// </summary>
        /// <param name="pos">The position where the explosion happens </param>
        /// <param name="radius">The explosion radius </param>
        /// <param name="maxForce">The explosion force at the explosion point (then is inversely proportional to the square of the distance)</param>
        /// <returns>A list of bodies and the amount of force that was applied to them.</returns>
        public Dictionary<Fixture, Vector2> Activate(Vector2 pos, float radius, float maxForce)
        {
            AABB aabb;
            aabb.LowerBound = pos + new Vector2(-radius, -radius);
            aabb.UpperBound = pos + new Vector2(radius, radius);
            Fixture[] shapes = new Fixture[MaxShapes];

            // More than 5 shapes in an explosion could be possible, but still strange.
            Fixture[] containedShapes = new Fixture[5];
            bool exit = false;

            int shapeCount = 0;
            int containedShapeCount = 0;

            // Query the world for overlapping shapes.
            World.QueryAABB(
                fixture =>
                {
                    if (fixture.TestPoint(ref pos))
                    {
                        if (IgnoreWhenInsideShape)
                        {
                            exit = true;
                            return false;
                        }

                        containedShapes[containedShapeCount++] = fixture;
                    }
                    else
                    {
                        shapes[shapeCount++] = fixture;
                    }

                    // Continue the query.
                    return true;
                }, ref aabb);

            if (exit)
                return new Dictionary<Fixture, Vector2>();

            Dictionary<Fixture, Vector2> exploded = new Dictionary<Fixture, Vector2>(shapeCount + containedShapeCount);

            // Per shape max/min angles for now.
            float[] vals = new float[shapeCount * 2];
            int valIndex = 0;
            for (int i = 0; i < shapeCount; ++i)
            {
                PolygonShape ps;
                CircleShape cs = shapes[i].Shape as CircleShape;
                if (cs != null)
                {
                    // We create a "diamond" approximation of the circle
                    Vertices v = new Vertices();
                    Vector2 vec = Vector2.Zero + new Vector2(cs.Radius, 0);
                    v.Add(vec);
                    vec = Vector2.Zero + new Vector2(0, cs.Radius);
                    v.Add(vec);
                    vec = Vector2.Zero + new Vector2(-cs.Radius, cs.Radius);
                    v.Add(vec);
                    vec = Vector2.Zero + new Vector2(0, -cs.Radius);
                    v.Add(vec);
                    ps = new PolygonShape(v, 0);
                }
                else
                    ps = shapes[i].Shape as PolygonShape;

                if ((shapes[i].Body.BodyType == BodyType.Dynamic) && ps != null)
                {
                    Vector2 toCentroid = shapes[i].Body.GetWorldPoint(ps.MassData.Centroid) - pos;
                    float angleToCentroid = (float)Math.Atan2(toCentroid.Y, toCentroid.X);
                    float min = float.MaxValue;
                    float max = float.MinValue;
                    float minAbsolute = 0.0f;
                    float maxAbsolute = 0.0f;

                    for (int j = 0; j < ps.Vertices.Count; ++j)
                    {
                        Vector2 toVertex = (shapes[i].Body.GetWorldPoint(ps.Vertices[j]) - pos);
                        float newAngle = (float)Math.Atan2(toVertex.Y, toVertex.X);
                        float diff = (newAngle - angleToCentroid);

                        diff = (diff - MathHelper.Pi) % (2 * MathHelper.Pi);
                        // the minus pi is important. It means cutoff for going other direction is at 180 deg where it needs to be

                        if (diff < 0.0f)
                            diff += 2 * MathHelper.Pi; // correction for not handling negs

                        diff -= MathHelper.Pi;

                        if (Math.Abs(diff) > MathHelper.Pi)
                            continue; // Something's wrong, point not in shape but exists angle diff > 180

                        if (diff > max)
                        {
                            max = diff;
                            maxAbsolute = newAngle;
                        }
                        if (diff < min)
                        {
                            min = diff;
                            minAbsolute = newAngle;
                        }
                    }

                    vals[valIndex] = minAbsolute;
                    ++valIndex;
                    vals[valIndex] = maxAbsolute;
                    ++valIndex;
                }
            }

            Array.Sort(vals, 0, valIndex, _rdc);
            _data.Clear();
            bool rayMissed = true;

            for (int i = 0; i < valIndex; ++i)
            {
                Fixture fixture = null;
                float midpt;

                int iplus = (i == valIndex - 1 ? 0 : i + 1);
                if (vals[i] == vals[iplus])
                    continue;

                if (i == valIndex - 1)
                {
                    // the single edgecase
                    midpt = (vals[0] + MathHelper.Pi * 2 + vals[i]);
                }
                else
                {
                    midpt = (vals[i + 1] + vals[i]);
                }

                midpt = midpt / 2;

                Vector2 p1 = pos;
                Vector2 p2 = radius * new Vector2((float)Math.Cos(midpt), (float)Math.Sin(midpt)) + pos;

                // RaycastOne
                bool hitClosest = false;
                World.RayCast((f, p, n, fr) =>
                                  {
                                      Body body = f.Body;

                                      if (!IsActiveOn(body))
                                          return 0;

                                      hitClosest = true;
                                      fixture = f;
                                      return fr;
                                  }, p1, p2);

                //draws radius points
                if ((hitClosest) && (fixture.Body.BodyType == BodyType.Dynamic))
                {
                    if ((_data.Any()) && (_data.Last().Body == fixture.Body) && (!rayMissed))
                    {
                        int laPos = _data.Count - 1;
                        ShapeData la = _data[laPos];
                        la.Max = vals[iplus];
                        _data[laPos] = la;
                    }
                    else
                    {
                        // make new
                        ShapeData d;
                        d.Body = fixture.Body;
                        d.Min = vals[i];
                        d.Max = vals[iplus];
                        _data.Add(d);
                    }

                    if ((_data.Count > 1)
                        && (i == valIndex - 1)
                        && (_data.Last().Body == _data.First().Body)
                        && (_data.Last().Max == _data.First().Min))
                    {
                        ShapeData fi = _data[0];
                        fi.Min = _data.Last().Min;
                        _data.RemoveAt(_data.Count - 1);
                        _data[0] = fi;
                        while (_data.First().Min >= _data.First().Max)
                        {
                            fi.Min -= MathHelper.Pi * 2;
                            _data[0] = fi;
                        }
                    }

                    int lastPos = _data.Count - 1;
                    ShapeData last = _data[lastPos];
                    while ((_data.Count > 0)
                           && (_data.Last().Min >= _data.Last().Max)) // just making sure min<max
                    {
                        last.Min = _data.Last().Min - 2 * MathHelper.Pi;
                        _data[lastPos] = last;
                    }
                    rayMissed = false;
                }
                else
                {
                    rayMissed = true; // raycast did not find a shape
                }
            }

            for (int i = 0; i < _data.Count; ++i)
            {
                if (!IsActiveOn(_data[i].Body))
                    continue;

                float arclen = _data[i].Max - _data[i].Min;

                float first = MathHelper.Min(MaxEdgeOffset, EdgeRatio * arclen);
                int insertedRays = (int)Math.Ceiling(((arclen - 2.0f * first) - (MinRays - 1) * MaxAngle) / MaxAngle);

                if (insertedRays < 0)
                    insertedRays = 0;

                float offset = (arclen - first * 2.0f) / ((float)MinRays + insertedRays - 1);

                //Note: This loop can go into infinite as it operates on floats.
                //Added FloatEquals with a large epsilon.
                for (float j = _data[i].Min + first;
                     j < _data[i].Max || MathUtils.FloatEquals(j, _data[i].Max, 0.0001f);
                     j += offset)
                {
                    Vector2 p1 = pos;
                    Vector2 p2 = pos + radius * new Vector2((float)Math.Cos(j), (float)Math.Sin(j));
                    Vector2 hitpoint = Vector2.Zero;
                    float minlambda = float.MaxValue;

                    List<Fixture> fl = _data[i].Body.FixtureList;
                    for (int x = 0; x < fl.Count; x++)
                    {
                        Fixture f = fl[x];
                        RayCastInput ri;
                        ri.Point1 = p1;
                        ri.Point2 = p2;
                        ri.MaxFraction = 50f;

                        RayCastOutput ro;
                        if (f.RayCast(out ro, ref ri, 0))
                        {
                            if (minlambda > ro.Fraction)
                            {
                                minlambda = ro.Fraction;
                                hitpoint = ro.Fraction * p2 + (1 - ro.Fraction) * p1;
                            }
                        }

                        // the force that is to be applied for this particular ray.
                        // offset is angular coverage. lambda*length of segment is distance.
                        float impulse = (arclen / (MinRays + insertedRays)) * maxForce * 180.0f / MathHelper.Pi * (1.0f - Math.Min(1.0f, minlambda));

                        // We Apply the impulse!!!
                        Vector2 vectImp = Vector2.Dot(impulse * new Vector2((float)Math.Cos(j), (float)Math.Sin(j)), -ro.Normal) * new Vector2((float)Math.Cos(j), (float)Math.Sin(j));
                        _data[i].Body.ApplyLinearImpulse(ref vectImp, ref hitpoint);

                        // We gather the fixtures for returning them
                        if (exploded.ContainsKey(f))
                            exploded[f] += vectImp;
                        else
                            exploded.Add(f, vectImp);

                        if (minlambda > 1.0f)
                            hitpoint = p2;
                    }
                }
            }

            // We check contained shapes
            for (int i = 0; i < containedShapeCount; ++i)
            {
                Fixture fix = containedShapes[i];

                if (!IsActiveOn(fix.Body))
                    continue;

                float impulse = MinRays * maxForce * 180.0f / MathHelper.Pi;
                Vector2 hitPoint;

                CircleShape circShape = fix.Shape as CircleShape;
                if (circShape != null)
                {
                    hitPoint = fix.Body.GetWorldPoint(circShape.Position);
                }
                else
                {
                    PolygonShape shape = fix.Shape as PolygonShape;
                    hitPoint = fix.Body.GetWorldPoint(shape.MassData.Centroid);
                }

                Vector2 vectImp = impulse * (hitPoint - pos);

                fix.Body.ApplyLinearImpulse(ref vectImp, ref hitPoint);

                if (!exploded.ContainsKey(fix))
                    exploded.Add(fix, vectImp);
            }

            return exploded;
        }
    }
}