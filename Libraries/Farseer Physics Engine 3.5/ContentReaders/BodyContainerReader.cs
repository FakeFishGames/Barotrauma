/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework.Content;

namespace FarseerPhysics.Content
{
    public class BodyContainerReader : ContentTypeReader<BodyContainer>
    {
        protected override BodyContainer Read(ContentReader input, BodyContainer existingInstance)
        {
            BodyContainer bodies = existingInstance ?? new BodyContainer();

            int count = input.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string name = input.ReadString();
                BodyTemplate body = new BodyTemplate
                    {
                    Mass = input.ReadSingle(),
                    BodyType = (BodyType)input.ReadInt32()
                };
                int fixtureCount = input.ReadInt32();
                for (int j = 0; j < fixtureCount; j++)
                {
                    FixtureTemplate fixture = new FixtureTemplate
                        {
                        Name = input.ReadString(),
                        Restitution = input.ReadSingle(),
                        Friction = input.ReadSingle()
                    };
                    ShapeType type = (ShapeType)input.ReadInt32();
                    switch (type)
                    {
                        case ShapeType.Circle:
                            {
                                float density = input.ReadSingle();
                                float radius = input.ReadSingle();
                                CircleShape circle = new CircleShape(radius, density);
                                circle.Position = input.ReadVector2();
                                fixture.Shape = circle;
                            } break;
                        case ShapeType.Polygon:
                            {
                                Vertices verts = new Vertices(Settings.MaxPolygonVertices);
                                float density = input.ReadSingle();
                                int verticeCount = input.ReadInt32();
                                for (int k = 0; k < verticeCount; k++)
                                {
                                    verts.Add(input.ReadVector2());
                                }
                                PolygonShape poly = new PolygonShape(verts, density);
                                poly.MassData.Centroid = input.ReadVector2();
                                fixture.Shape = poly;
                            } break;
                        case ShapeType.Edge:
                            {
                                EdgeShape edge = new EdgeShape(input.ReadVector2(), input.ReadVector2());
                                edge.HasVertex0 = input.ReadBoolean();
                                if (edge.HasVertex0)
                                {
                                    edge.Vertex0 = input.ReadVector2();
                                }
                                edge.HasVertex3 = input.ReadBoolean();
                                if (edge.HasVertex3)
                                {
                                    edge.Vertex3 = input.ReadVector2();
                                }
                                fixture.Shape = edge;
                            } break;
                        case ShapeType.Chain:
                            {
                                Vertices verts = new Vertices();
                                int verticeCount = input.ReadInt32();
                                for (int k = 0; k < verticeCount; k++)
                                {
                                    verts.Add(input.ReadVector2());
                                }
                                fixture.Shape = new ChainShape(verts);
                            } break;
                    }
                    body.Fixtures.Add(fixture);
                }
                bodies[name] = body;
            }
            return bodies;
        }
    }
}