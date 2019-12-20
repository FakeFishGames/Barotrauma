/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System.Collections.Generic;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Dynamics;

namespace FarseerPhysics.Content
{
    public class FixtureTemplate
    {
        public Shape Shape;
        public float Restitution;
        public float Friction;
        public string Name;
    }

    public class BodyTemplate
    {
        public List<FixtureTemplate> Fixtures;
        public float Mass;
        public BodyType BodyType;

        public BodyTemplate()
        {
            Fixtures = new List<FixtureTemplate>();
        }

        public Body Create(World world)
        {
            Body body = world.CreateBody();
            body.BodyType = BodyType;

            foreach (FixtureTemplate fixtureTemplate in Fixtures)
            {
                Fixture fixture = body.CreateFixture(fixtureTemplate.Shape);
                fixture.UserData = fixtureTemplate.Name;
                fixture.Restitution = fixtureTemplate.Restitution;
                fixture.Friction = fixtureTemplate.Friction;
            }

            if (Mass > 0f)
                body.Mass = Mass;

            return body;
        }

    }

    public class BodyContainer : Dictionary<string, BodyTemplate> { }
}
