/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using FarseerPhysics.Common.PhysicsLogic;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Controllers
{
    public enum GravityType
    {
        Linear,
        DistanceSquared
    }

    public class GravityController : Controller
    {
        public GravityController(float strength)
        {
            Strength = strength;
            MaxRadius = float.MaxValue;
            GravityType = GravityType.DistanceSquared;
            Points = new List<Vector2>();
            Bodies = new List<Body>();
        }

        public GravityController(float strength, float maxRadius, float minRadius)
        {
            MinRadius = minRadius;
            MaxRadius = maxRadius;
            Strength = strength;
            GravityType = GravityType.DistanceSquared;
            Points = new List<Vector2>();
            Bodies = new List<Body>();
        }

        public float MinRadius { get; set; }
        public float MaxRadius { get; set; }
        public float Strength { get; set; }
        public GravityType GravityType { get; set; }
        public List<Body> Bodies { get; set; }
        public List<Vector2> Points { get; set; }

        public override void Update(float dt)
        {
            Vector2 f = Vector2.Zero;

            foreach (Body worldBody in World.BodyList)
            {
                if (!IsActiveOn(worldBody))
                    continue;

                foreach (Body controllerBody in Bodies)
                {
                    if (worldBody == controllerBody || (worldBody.BodyType == BodyType.Static && controllerBody.BodyType == BodyType.Static) || !controllerBody.Enabled)
                        continue;

                    Vector2 d = controllerBody.Position - worldBody.Position;
                    float r2 = d.LengthSquared();

                    if (r2 <= Settings.Epsilon || r2 > MaxRadius * MaxRadius || r2 < MinRadius * MinRadius)
                        continue;

                    switch (GravityType)
                    {
                        case GravityType.DistanceSquared:
                            f = Strength / r2 * worldBody.Mass * controllerBody.Mass * d;
                            break;
                        case GravityType.Linear:
                            f = Strength / (float)Math.Sqrt(r2) * worldBody.Mass * controllerBody.Mass * d;
                            break;
                    }

                    worldBody.ApplyForce(ref f);
                }

                foreach (Vector2 point in Points)
                {
                    Vector2 d = point - worldBody.Position;
                    float r2 = d.LengthSquared();

                    if (r2 <= Settings.Epsilon || r2 > MaxRadius * MaxRadius || r2 < MinRadius * MinRadius)
                        continue;

                    switch (GravityType)
                    {
                        case GravityType.DistanceSquared:
                            f = Strength / r2 * worldBody.Mass * d;
                            break;
                        case GravityType.Linear:
                            f = Strength / (float)Math.Sqrt(r2) * worldBody.Mass * d;
                            break;
                    }

                    worldBody.ApplyForce(ref f);
                }
            }
        }

        public void AddBody(Body body)
        {
            Bodies.Add(body);
        }

        public void AddPoint(Vector2 point)
        {
            Points.Add(point);
        }
    }
}