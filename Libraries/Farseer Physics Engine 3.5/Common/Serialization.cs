using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common
{
    /// <summary>
    /// Serialize the world into an XML file
    /// </summary>
    public static class WorldSerializer
    {
        /// <summary>
        /// Serialize the world to an XML file
        /// </summary>
        /// <param name="world"></param>
        /// <param name="filename"></param>
        public static void Serialize(World world, string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                WorldXmlSerializer.Serialize(world, fs);
            }
        }

        /// <summary>
        /// Deserialize the world from an XML file
        /// </summary>
        /// <param name="filename"></param>
        public static World Deserialize(string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Open))
            {
                return WorldXmlDeserializer.Deserialize(fs);
            }
        }
    }

    internal static class WorldXmlSerializer
    {
        private static XmlWriter _writer;

        private static void SerializeShape(Shape shape)
        {
            _writer.WriteStartElement("Shape");
            _writer.WriteAttributeString("Type", shape.ShapeType.ToString());
            _writer.WriteAttributeString("Density", shape.Density.ToString());

            switch (shape.ShapeType)
            {
                case ShapeType.Circle:
                    {
                        CircleShape circle = (CircleShape)shape;

                        _writer.WriteElementString("Radius", circle.Radius.ToString());

                        WriteElement("Position", circle.Position);
                    }
                    break;
                case ShapeType.Polygon:
                    {
                        PolygonShape poly = (PolygonShape)shape;

                        _writer.WriteStartElement("Vertices");
                        foreach (Vector2 v in poly.Vertices)
                            WriteElement("Vertex", v);
                        _writer.WriteEndElement();

                        WriteElement("Centroid", poly.MassData.Centroid);
                    }
                    break;
                case ShapeType.Edge:
                    {
                        EdgeShape poly = (EdgeShape)shape;
                        WriteElement("Vertex1", poly.Vertex1);
                        WriteElement("Vertex2", poly.Vertex2);
                    }
                    break;
                case ShapeType.Chain:
                    {
                        ChainShape chain = (ChainShape)shape;

                        _writer.WriteStartElement("Vertices");
                        foreach (Vector2 v in chain.Vertices)
                            WriteElement("Vertex", v);
                        _writer.WriteEndElement();

                        WriteElement("NextVertex", chain.NextVertex);
                        WriteElement("PrevVertex", chain.PrevVertex);
                    }
                    break;
                default:
                    throw new Exception();
            }

            _writer.WriteEndElement();
        }

        private static void SerializeFixture(Fixture fixture)
        {
            _writer.WriteStartElement("Fixture");
            _writer.WriteAttributeString("Id", fixture.FixtureId.ToString());

            _writer.WriteStartElement("FilterData");
            _writer.WriteElementString("CategoryBits", ((int)fixture.CollisionCategories).ToString());
            _writer.WriteElementString("MaskBits", ((int)fixture.CollidesWith).ToString());
            _writer.WriteElementString("GroupIndex", fixture.CollisionGroup.ToString());
            _writer.WriteElementString("CollisionIgnores", Join("|", fixture._collisionIgnores));
            _writer.WriteEndElement();

            _writer.WriteElementString("Friction", fixture.Friction.ToString());
            _writer.WriteElementString("IsSensor", fixture.IsSensor.ToString());
            _writer.WriteElementString("Restitution", fixture.Restitution.ToString());

            if (fixture.UserData != null)
            {
                _writer.WriteStartElement("UserData");
                WriteDynamicType(fixture.UserData.GetType(), fixture.UserData);
                _writer.WriteEndElement();
            }

            _writer.WriteEndElement();
        }

        private static void SerializeBody(List<Fixture> fixtures, List<Shape> shapes, Body body)
        {
            _writer.WriteStartElement("Body");
            _writer.WriteAttributeString("Type", body.BodyType.ToString());
            _writer.WriteElementString("Active", body.Enabled.ToString());
            _writer.WriteElementString("AllowSleep", body.SleepingAllowed.ToString());
            _writer.WriteElementString("Angle", body.Rotation.ToString());
            _writer.WriteElementString("AngularDamping", body.AngularDamping.ToString());
            _writer.WriteElementString("AngularVelocity", body.AngularVelocity.ToString());
            _writer.WriteElementString("Awake", body.Awake.ToString());
            _writer.WriteElementString("Bullet", body.IsBullet.ToString());
            _writer.WriteElementString("FixedRotation", body.FixedRotation.ToString());
            _writer.WriteElementString("LinearDamping", body.LinearDamping.ToString());
            WriteElement("LinearVelocity", body.LinearVelocity);
            WriteElement("Position", body.Position);

            if (body.UserData != null)
            {
                _writer.WriteStartElement("UserData");
                WriteDynamicType(body.UserData.GetType(), body.UserData);
                _writer.WriteEndElement();
            }

            _writer.WriteStartElement("Bindings");
            for (int i = 0; i < body.FixtureList.Count; i++)
            {
                _writer.WriteStartElement("Pair");
                _writer.WriteAttributeString("FixtureId", FindIndex(fixtures, body.FixtureList[i]).ToString());
                _writer.WriteAttributeString("ShapeId", FindIndex(shapes, body.FixtureList[i].Shape).ToString());
                _writer.WriteEndElement();
            }

            _writer.WriteEndElement();
            _writer.WriteEndElement();
        }

        private static void SerializeJoint(List<Body> bodies, Joint joint)
        {
            _writer.WriteStartElement("Joint");
            _writer.WriteAttributeString("Type", joint.JointType.ToString());

            WriteElement("BodyA", FindIndex(bodies, joint.BodyA));
            WriteElement("BodyB", FindIndex(bodies, joint.BodyB));

            WriteElement("CollideConnected", joint.CollideConnected);

            WriteElement("Breakpoint", joint.Breakpoint);

            if (joint.UserData != null)
            {
                _writer.WriteStartElement("UserData");
                WriteDynamicType(joint.UserData.GetType(), joint.UserData);
                _writer.WriteEndElement();
            }

            switch (joint.JointType)
            {
                case JointType.Distance:
                    {
                        DistanceJoint distanceJoint = (DistanceJoint)joint;
                        WriteElement("DampingRatio", distanceJoint.DampingRatio);
                        WriteElement("FrequencyHz", distanceJoint.Frequency);
                        WriteElement("Length", distanceJoint.Length);
                        WriteElement("LocalAnchorA", distanceJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", distanceJoint.LocalAnchorB);
                    }
                    break;
                case JointType.Friction:
                    {
                        FrictionJoint frictionJoint = (FrictionJoint)joint;
                        WriteElement("LocalAnchorA", frictionJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", frictionJoint.LocalAnchorB);
                        WriteElement("MaxForce", frictionJoint.MaxForce);
                        WriteElement("MaxTorque", frictionJoint.MaxTorque);
                    }
                    break;
                case JointType.Gear:
                    throw new Exception("Gear joint not supported by serialization");
                case JointType.Wheel:
                    {
                        WheelJoint wheelJoint = (WheelJoint)joint;
                        WriteElement("EnableMotor", wheelJoint.MotorEnabled);
                        WriteElement("LocalAnchorA", wheelJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", wheelJoint.LocalAnchorB);
                        WriteElement("MotorSpeed", wheelJoint.MotorSpeed);
                        WriteElement("DampingRatio", wheelJoint.DampingRatio);
                        WriteElement("MaxMotorTorque", wheelJoint.MaxMotorTorque);
                        WriteElement("FrequencyHz", wheelJoint.Frequency);
                        WriteElement("Axis", wheelJoint.Axis);
                    }
                    break;
                case JointType.Prismatic:
                    {
                        //NOTE: Does not conform with Box2DScene

                        PrismaticJoint prismaticJoint = (PrismaticJoint)joint;
                        WriteElement("EnableLimit", prismaticJoint.LimitEnabled);
                        WriteElement("EnableMotor", prismaticJoint.MotorEnabled);
                        WriteElement("LocalAnchorA", prismaticJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", prismaticJoint.LocalAnchorB);
                        WriteElement("Axis", prismaticJoint.Axis);
                        WriteElement("LowerTranslation", prismaticJoint.LowerLimit);
                        WriteElement("UpperTranslation", prismaticJoint.UpperLimit);
                        WriteElement("MaxMotorForce", prismaticJoint.MaxMotorForce);
                        WriteElement("MotorSpeed", prismaticJoint.MotorSpeed);
                    }
                    break;
                case JointType.Pulley:
                    {
                        PulleyJoint pulleyJoint = (PulleyJoint)joint;
                        WriteElement("WorldAnchorA", pulleyJoint.WorldAnchorA);
                        WriteElement("WorldAnchorB", pulleyJoint.WorldAnchorB);
                        WriteElement("LengthA", pulleyJoint.LengthA);
                        WriteElement("LengthB", pulleyJoint.LengthB);
                        WriteElement("LocalAnchorA", pulleyJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", pulleyJoint.LocalAnchorB);
                        WriteElement("Ratio", pulleyJoint.Ratio);
                        WriteElement("Constant", pulleyJoint.Constant);
                    }
                    break;
                case JointType.Revolute:
                    {
                        RevoluteJoint revoluteJoint = (RevoluteJoint)joint;
                        WriteElement("EnableLimit", revoluteJoint.LimitEnabled);
                        WriteElement("EnableMotor", revoluteJoint.MotorEnabled);
                        WriteElement("LocalAnchorA", revoluteJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", revoluteJoint.LocalAnchorB);
                        WriteElement("LowerAngle", revoluteJoint.LowerLimit);
                        WriteElement("MaxMotorTorque", revoluteJoint.MaxMotorTorque);
                        WriteElement("MotorSpeed", revoluteJoint.MotorSpeed);
                        WriteElement("ReferenceAngle", revoluteJoint.ReferenceAngle);
                        WriteElement("UpperAngle", revoluteJoint.UpperLimit);
                    }
                    break;
                case JointType.Weld:
                    {
                        WeldJoint weldJoint = (WeldJoint)joint;
                        WriteElement("LocalAnchorA", weldJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", weldJoint.LocalAnchorB);
                    }
                    break;
                //
                // Not part of Box2DScene
                //
                case JointType.Rope:
                    {
                        RopeJoint ropeJoint = (RopeJoint)joint;
                        WriteElement("LocalAnchorA", ropeJoint.LocalAnchorA);
                        WriteElement("LocalAnchorB", ropeJoint.LocalAnchorB);
                        WriteElement("MaxLength", ropeJoint.MaxLength);
                    }
                    break;
                case JointType.Angle:
                    {
                        AngleJoint angleJoint = (AngleJoint)joint;
                        WriteElement("BiasFactor", angleJoint.BiasFactor);
                        WriteElement("MaxImpulse", angleJoint.MaxImpulse);
                        WriteElement("Softness", angleJoint.Softness);
                        WriteElement("TargetAngle", angleJoint.TargetAngle);
                    }
                    break;
                case JointType.Motor:
                    {
                        MotorJoint motorJoint = (MotorJoint)joint;
                        WriteElement("AngularOffset", motorJoint.AngularOffset);
                        WriteElement("LinearOffset", motorJoint.LinearOffset);
                        WriteElement("MaxForce", motorJoint.MaxForce);
                        WriteElement("MaxTorque", motorJoint.MaxTorque);
                        WriteElement("CorrectionFactor", motorJoint.CorrectionFactor);
                    }
                    break;
                default:
                    throw new Exception("Joint not supported");
            }

            _writer.WriteEndElement();
        }

        private static void WriteDynamicType(Type type, object val)
        {
            _writer.WriteElementString("Type", type.AssemblyQualifiedName);

            _writer.WriteStartElement("Value");
            XmlSerializer serializer = new XmlSerializer(type);
            XmlSerializerNamespaces xmlnsEmpty = new XmlSerializerNamespaces();
            xmlnsEmpty.Add("", "");
            serializer.Serialize(_writer, val, xmlnsEmpty);
            _writer.WriteEndElement();
        }

        private static void WriteElement(string name, Vector2 vec)
        {
            _writer.WriteElementString(name, vec.X + " " + vec.Y);
        }

        private static void WriteElement(string name, int val)
        {
            _writer.WriteElementString(name, val.ToString());
        }

        private static void WriteElement(string name, bool val)
        {
            _writer.WriteElementString(name, val.ToString());
        }

        private static void WriteElement(string name, float val)
        {
            _writer.WriteElementString(name, val.ToString());
        }

        private static int FindIndex(List<Body> list, Body item)
        {
            for (int i = 0; i < list.Count; ++i)
                if (list[i] == item)
                    return i;

            return -1;
        }

        private static int FindIndex(List<Fixture> list, Fixture item)
        {
            for (int i = 0; i < list.Count; ++i)
                if (list[i].CompareTo(item))
                    return i;

            return -1;
        }

        private static int FindIndex(List<Shape> list, Shape item)
        {
            for (int i = 0; i < list.Count; ++i)
                if (list[i].CompareTo(item))
                    return i;

            return -1;
        }

        private static String Join<T>(String separator, IEnumerable<T> values)
        {
            using (IEnumerator<T> en = values.GetEnumerator())
            {
                if (!en.MoveNext())
                    return String.Empty;

                StringBuilder result = new StringBuilder();
                if (en.Current != null)
                {
                    // handle the case that the enumeration has null entries
                    // and the case where their ToString() override is broken
                    string value = en.Current.ToString();
                    if (value != null)
                        result.Append(value);
                }

                while (en.MoveNext())
                {
                    result.Append(separator);
                    if (en.Current != null)
                    {
                        // handle the case that the enumeration has null entries
                        // and the case where their ToString() override is broken
                        string value = en.Current.ToString();
                        if (value != null)
                            result.Append(value);
                    }
                }
                return result.ToString();
            }
        }

        internal static void Serialize(World world, Stream stream)
        {
            List<Body> bodies = new List<Body>();
            List<Fixture> fixtures = new List<Fixture>();
            List<Shape> shapes = new List<Shape>();

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.NewLineOnAttributes = false;
            settings.OmitXmlDeclaration = true;

            _writer = XmlWriter.Create(stream, settings);

            _writer.WriteStartElement("World");
            _writer.WriteAttributeString("Version", "3");
            WriteElement("Gravity", world.Gravity);

            _writer.WriteStartElement("Shapes");

            foreach (Body body in world.BodyList)
            {
                foreach (Fixture fixture in body.FixtureList)
                {
                    if (!shapes.Any(s2 => fixture.Shape.CompareTo(s2)))
                    {
                        SerializeShape(fixture.Shape);
                        shapes.Add(fixture.Shape);
                    }
                }
            }

            _writer.WriteEndElement();
            _writer.WriteStartElement("Fixtures");

            foreach (Body body in world.BodyList)
            {
                foreach (Fixture fixture in body.FixtureList)
                {
                    if (!fixtures.Any(f2 => fixture.CompareTo(f2)))
                    {
                        SerializeFixture(fixture);
                        fixtures.Add(fixture);
                    }
                }
            }

            _writer.WriteEndElement();
            _writer.WriteStartElement("Bodies");

            foreach (Body body in world.BodyList)
            {
                bodies.Add(body);
                SerializeBody(fixtures, shapes, body);
            }

            _writer.WriteEndElement();
            _writer.WriteStartElement("Joints");

            foreach (Joint joint in world.JointList)
            {
                SerializeJoint(bodies, joint);
            }

            _writer.WriteEndElement();
            _writer.WriteEndElement();

            _writer.Flush();
            _writer.Close();
        }
    }

    internal static class WorldXmlDeserializer
    {
        internal static World Deserialize(Stream stream)
        {
            World world = new World(Vector2.Zero);
            Deserialize(world, stream);
            return world;
        }

        private static void Deserialize(World world, Stream stream)
        {
            List<Body> bodies = new List<Body>();
            List<Fixture> fixtures = new List<Fixture>();
            List<Joint> joints = new List<Joint>();
            List<Shape> shapes = new List<Shape>();

            XMLFragmentElement root = XMLFragmentParser.LoadFromStream(stream);

            if (root.Name.ToLower() != "world")
                throw new Exception();

            //Read gravity
            foreach (XMLFragmentElement element in root.Elements)
            {
                if (element.Name.ToLower() == "gravity")
                {
                    world.Gravity = ReadVector(element);
                    break;
                }
            }

            //Read shapes
            foreach (XMLFragmentElement shapeElement in root.Elements)
            {
                if (shapeElement.Name.ToLower() == "shapes")
                {
                    foreach (XMLFragmentElement element in shapeElement.Elements)
                    {
                        if (element.Name.ToLower() != "shape")
                            throw new Exception();

                        ShapeType type = (ShapeType)Enum.Parse(typeof(ShapeType), element.Attributes[0].Value, true);
                        float density = float.Parse(element.Attributes[1].Value);

                        switch (type)
                        {
                            case ShapeType.Circle:
                                {
                                    CircleShape shape = new CircleShape();
                                    shape._density = density;

                                    foreach (XMLFragmentElement sn in element.Elements)
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "radius":
                                                shape.Radius = float.Parse(sn.Value);
                                                break;
                                            case "position":
                                                shape.Position = ReadVector(sn);
                                                break;
                                            default:
                                                throw new Exception();
                                        }
                                    }

                                    shapes.Add(shape);
                                }
                                break;
                            case ShapeType.Polygon:
                                {
                                    PolygonShape shape = new PolygonShape();
                                    shape._density = density;

                                    foreach (XMLFragmentElement sn in element.Elements)
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "vertices":
                                                {
                                                    List<Vector2> verts = new List<Vector2>(sn.Elements.Count);

                                                    foreach (XMLFragmentElement vert in sn.Elements)
                                                        verts.Add(ReadVector(vert));

                                                    shape.Vertices = new Vertices(verts);
                                                }
                                                break;
                                            case "centroid":
                                                shape.MassData.Centroid = ReadVector(sn);
                                                break;
                                        }
                                    }

                                    shapes.Add(shape);
                                }
                                break;
                            case ShapeType.Edge:
                                {
                                    EdgeShape shape = new EdgeShape();
                                    shape._density = density;

                                    foreach (XMLFragmentElement sn in element.Elements)
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "hasvertex0":
                                                shape.HasVertex0 = bool.Parse(sn.Value);
                                                break;
                                            case "hasvertex3":
                                                shape.HasVertex0 = bool.Parse(sn.Value);
                                                break;
                                            case "vertex0":
                                                shape.Vertex0 = ReadVector(sn);
                                                break;
                                            case "vertex1":
                                                shape.Vertex1 = ReadVector(sn);
                                                break;
                                            case "vertex2":
                                                shape.Vertex2 = ReadVector(sn);
                                                break;
                                            case "vertex3":
                                                shape.Vertex3 = ReadVector(sn);
                                                break;
                                            default:
                                                throw new Exception();
                                        }
                                    }
                                    shapes.Add(shape);
                                }
                                break;
                            case ShapeType.Chain:
                                {
                                    ChainShape shape = new ChainShape();
                                    shape._density = density;

                                    foreach (XMLFragmentElement sn in element.Elements)
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "vertices":
                                                {
                                                    List<Vector2> verts = new List<Vector2>(sn.Elements.Count);

                                                    foreach (XMLFragmentElement vert in sn.Elements)
                                                        verts.Add(ReadVector(vert));

                                                    shape.Vertices = new Vertices(verts);
                                                }
                                                break;
                                            case "nextvertex":
                                                shape.NextVertex = ReadVector(sn);
                                                break;
                                            case "prevvertex":
                                                shape.PrevVertex = ReadVector(sn);
                                                break;

                                            default:
                                                throw new Exception();
                                        }
                                    }
                                    shapes.Add(shape);
                                }
                                break;
                        }
                    }
                }
            }

            //Read fixtures
            foreach (XMLFragmentElement fixtureElement in root.Elements)
            {
                if (fixtureElement.Name.ToLower() == "fixtures")
                {
                    foreach (XMLFragmentElement element in fixtureElement.Elements)
                    {
                        Fixture fixture = new Fixture();

                        if (element.Name.ToLower() != "fixture")
                            throw new Exception();

                        fixture.FixtureId = int.Parse(element.Attributes[0].Value);

                        foreach (XMLFragmentElement sn in element.Elements)
                        {
                            switch (sn.Name.ToLower())
                            {
                                case "filterdata":
                                    foreach (XMLFragmentElement ssn in sn.Elements)
                                    {
                                        switch (ssn.Name.ToLower())
                                        {
                                            case "categorybits":
                                                fixture._collisionCategories = (Category)int.Parse(ssn.Value);
                                                break;
                                            case "maskbits":
                                                fixture._collidesWith = (Category)int.Parse(ssn.Value);
                                                break;
                                            case "groupindex":
                                                fixture._collisionGroup = short.Parse(ssn.Value);
                                                break;
                                            case "CollisionIgnores":
                                                string[] split = ssn.Value.Split('|');
                                                foreach (string s in split)
                                                {
                                                    fixture._collisionIgnores.Add(int.Parse(s));
                                                }
                                                break;
                                        }
                                    }

                                    break;
                                case "friction":
                                    fixture.Friction = float.Parse(sn.Value);
                                    break;
                                case "issensor":
                                    fixture.IsSensor = bool.Parse(sn.Value);
                                    break;
                                case "restitution":
                                    fixture.Restitution = float.Parse(sn.Value);
                                    break;
                                case "userdata":
                                    fixture.UserData = ReadSimpleType(sn, null, false);
                                    break;
                            }
                        }

                        fixtures.Add(fixture);
                    }
                }
            }

            //Read bodies
            foreach (XMLFragmentElement bodyElement in root.Elements)
            {
                if (bodyElement.Name.ToLower() == "bodies")
                {
                    foreach (XMLFragmentElement element in bodyElement.Elements)
                    {
                        Body body = new Body(world);

                        if (element.Name.ToLower() != "body")
                            throw new Exception();

                        body.BodyType = (BodyType)Enum.Parse(typeof(BodyType), element.Attributes[0].Value, true);

                        foreach (XMLFragmentElement sn in element.Elements)
                        {
                            switch (sn.Name.ToLower())
                            {
                                case "active":
                                    body._enabled = bool.Parse(sn.Value);
                                    break;
                                case "allowsleep":
                                    body.SleepingAllowed = bool.Parse(sn.Value);
                                    break;
                                case "angle":
                                    {
                                        Vector2 position = body.Position;
                                        body.SetTransformIgnoreContacts(ref position, float.Parse(sn.Value));
                                    }
                                    break;
                                case "angulardamping":
                                    body.AngularDamping = float.Parse(sn.Value);
                                    break;
                                case "angularvelocity":
                                    body.AngularVelocity = float.Parse(sn.Value);
                                    break;
                                case "awake":
                                    body.Awake = bool.Parse(sn.Value);
                                    break;
                                case "bullet":
                                    body.IsBullet = bool.Parse(sn.Value);
                                    break;
                                case "fixedrotation":
                                    body.FixedRotation = bool.Parse(sn.Value);
                                    break;
                                case "lineardamping":
                                    body.LinearDamping = float.Parse(sn.Value);
                                    break;
                                case "linearvelocity":
                                    body.LinearVelocity = ReadVector(sn);
                                    break;
                                case "position":
                                    {
                                        float rotation = body.Rotation;
                                        Vector2 position = ReadVector(sn);
                                        body.SetTransformIgnoreContacts(ref position, rotation);
                                    }
                                    break;
                                case "userdata":
                                    body.UserData = ReadSimpleType(sn, null, false);
                                    break;
                                case "bindings":
                                    {
                                        foreach (XMLFragmentElement pair in sn.Elements)
                                        {
                                            Fixture fix = fixtures[int.Parse(pair.Attributes[0].Value)];
                                            fix.Shape = shapes[int.Parse(pair.Attributes[1].Value)].Clone();
                                            fix.CloneOnto(body);
                                        }
                                        break;
                                    }
                            }
                        }

                        bodies.Add(body);
                    }
                }
            }

            //Read joints
            foreach (XMLFragmentElement jointElement in root.Elements)
            {
                if (jointElement.Name.ToLower() == "joints")
                {
                    foreach (XMLFragmentElement n in jointElement.Elements)
                    {
                        Joint joint;

                        if (n.Name.ToLower() != "joint")
                            throw new Exception();

                        JointType type = (JointType)Enum.Parse(typeof(JointType), n.Attributes[0].Value, true);

                        int bodyAIndex = -1, bodyBIndex = -1;
                        bool collideConnected = false;
                        object userData = null;

                        foreach (XMLFragmentElement sn in n.Elements)
                        {
                            switch (sn.Name.ToLower())
                            {
                                case "bodya":
                                    bodyAIndex = int.Parse(sn.Value);
                                    break;
                                case "bodyb":
                                    bodyBIndex = int.Parse(sn.Value);
                                    break;
                                case "collideconnected":
                                    collideConnected = bool.Parse(sn.Value);
                                    break;
                                case "userdata":
                                    userData = ReadSimpleType(sn, null, false);
                                    break;
                            }
                        }

                        Body bodyA = bodies[bodyAIndex];
                        Body bodyB = bodies[bodyBIndex];

                        switch (type)
                        {
                            //case JointType.FixedMouse:
                            //    joint = new FixedMouseJoint();
                            //    break;
                            //case JointType.FixedRevolute:
                            //    break;
                            //case JointType.FixedDistance:
                            //    break;
                            //case JointType.FixedLine:
                            //    break;
                            //case JointType.FixedPrismatic:
                            //    break;
                            //case JointType.FixedAngle:
                            //    break;
                            //case JointType.FixedFriction:
                            //    break;
                            case JointType.Distance:
                                joint = new DistanceJoint();
                                break;
                            case JointType.Friction:
                                joint = new FrictionJoint();
                                break;
                            case JointType.Wheel:
                                joint = new WheelJoint();
                                break;
                            case JointType.Prismatic:
                                joint = new PrismaticJoint();
                                break;
                            case JointType.Pulley:
                                joint = new PulleyJoint();
                                break;
                            case JointType.Revolute:
                                joint = new RevoluteJoint();
                                break;
                            case JointType.Weld:
                                joint = new WeldJoint();
                                break;
                            case JointType.Rope:
                                joint = new RopeJoint();
                                break;
                            case JointType.Angle:
                                joint = new AngleJoint();
                                break;
                            case JointType.Motor:
                                joint = new MotorJoint();
                                break;
                            case JointType.Gear:
                                throw new Exception("GearJoint is not supported.");
                            default:
                                throw new Exception("Invalid or unsupported joint.");
                        }

                        joint.CollideConnected = collideConnected;
                        joint.UserData = userData;
                        joint.BodyA = bodyA;
                        joint.BodyB = bodyB;
                        joints.Add(joint);
                        world.AddJoint(joint);

                        foreach (XMLFragmentElement sn in n.Elements)
                        {
                            // check for specific nodes
                            switch (type)
                            {
                                case JointType.Distance:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "dampingratio":
                                                ((DistanceJoint)joint).DampingRatio = float.Parse(sn.Value);
                                                break;
                                            case "frequencyhz":
                                                ((DistanceJoint)joint).Frequency = float.Parse(sn.Value);
                                                break;
                                            case "length":
                                                ((DistanceJoint)joint).Length = float.Parse(sn.Value);
                                                break;
                                            case "localanchora":
                                                ((DistanceJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((DistanceJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Friction:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "localanchora":
                                                ((FrictionJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((FrictionJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                            case "maxforce":
                                                ((FrictionJoint)joint).MaxForce = float.Parse(sn.Value);
                                                break;
                                            case "maxtorque":
                                                ((FrictionJoint)joint).MaxTorque = float.Parse(sn.Value);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Wheel:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "enablemotor":
                                                ((WheelJoint)joint).MotorEnabled = bool.Parse(sn.Value);
                                                break;
                                            case "localanchora":
                                                ((WheelJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((WheelJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                            case "motorspeed":
                                                ((WheelJoint)joint).MotorSpeed = float.Parse(sn.Value);
                                                break;
                                            case "dampingratio":
                                                ((WheelJoint)joint).DampingRatio = float.Parse(sn.Value);
                                                break;
                                            case "maxmotortorque":
                                                ((WheelJoint)joint).MaxMotorTorque = float.Parse(sn.Value);
                                                break;
                                            case "frequencyhz":
                                                ((WheelJoint)joint).Frequency = float.Parse(sn.Value);
                                                break;
                                            case "axis":
                                                ((WheelJoint)joint).Axis = ReadVector(sn);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Prismatic:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "enablelimit":
                                                ((PrismaticJoint)joint).LimitEnabled = bool.Parse(sn.Value);
                                                break;
                                            case "enablemotor":
                                                ((PrismaticJoint)joint).MotorEnabled = bool.Parse(sn.Value);
                                                break;
                                            case "localanchora":
                                                ((PrismaticJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((PrismaticJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                            case "axis":
                                                ((PrismaticJoint)joint).Axis = ReadVector(sn);
                                                break;
                                            case "maxmotorforce":
                                                ((PrismaticJoint)joint).MaxMotorForce = float.Parse(sn.Value);
                                                break;
                                            case "motorspeed":
                                                ((PrismaticJoint)joint).MotorSpeed = float.Parse(sn.Value);
                                                break;
                                            case "lowertranslation":
                                                ((PrismaticJoint)joint).LowerLimit = float.Parse(sn.Value);
                                                break;
                                            case "uppertranslation":
                                                ((PrismaticJoint)joint).UpperLimit = float.Parse(sn.Value);
                                                break;
                                            case "referenceangle":
                                                ((PrismaticJoint)joint).ReferenceAngle = float.Parse(sn.Value);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Pulley:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "worldanchora":
                                                ((PulleyJoint)joint).WorldAnchorA = ReadVector(sn);
                                                break;
                                            case "worldanchorb":
                                                ((PulleyJoint)joint).WorldAnchorB = ReadVector(sn);
                                                break;
                                            case "lengtha":
                                                ((PulleyJoint)joint).LengthA = float.Parse(sn.Value);
                                                break;
                                            case "lengthb":
                                                ((PulleyJoint)joint).LengthB = float.Parse(sn.Value);
                                                break;
                                            case "localanchora":
                                                ((PulleyJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((PulleyJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                            case "ratio":
                                                ((PulleyJoint)joint).Ratio = float.Parse(sn.Value);
                                                break;
                                            case "constant":
                                                ((PulleyJoint)joint).Constant = float.Parse(sn.Value);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Revolute:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "enablelimit":
                                                ((RevoluteJoint)joint).LimitEnabled = bool.Parse(sn.Value);
                                                break;
                                            case "enablemotor":
                                                ((RevoluteJoint)joint).MotorEnabled = bool.Parse(sn.Value);
                                                break;
                                            case "localanchora":
                                                ((RevoluteJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((RevoluteJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                            case "maxmotortorque":
                                                ((RevoluteJoint)joint).MaxMotorTorque = float.Parse(sn.Value);
                                                break;
                                            case "motorspeed":
                                                ((RevoluteJoint)joint).MotorSpeed = float.Parse(sn.Value);
                                                break;
                                            case "lowerangle":
                                                ((RevoluteJoint)joint).LowerLimit = float.Parse(sn.Value);
                                                break;
                                            case "upperangle":
                                                ((RevoluteJoint)joint).UpperLimit = float.Parse(sn.Value);
                                                break;
                                            case "referenceangle":
                                                ((RevoluteJoint)joint).ReferenceAngle = float.Parse(sn.Value);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Weld:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "localanchora":
                                                ((WeldJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((WeldJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Rope:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "localanchora":
                                                ((RopeJoint)joint).LocalAnchorA = ReadVector(sn);
                                                break;
                                            case "localanchorb":
                                                ((RopeJoint)joint).LocalAnchorB = ReadVector(sn);
                                                break;
                                            case "maxlength":
                                                ((RopeJoint)joint).MaxLength = float.Parse(sn.Value);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Gear:
                                    throw new Exception("Gear joint is unsupported");
                                case JointType.Angle:
                                    {
                                        switch (sn.Name.ToLower())
                                        {
                                            case "biasfactor":
                                                ((AngleJoint)joint).BiasFactor = float.Parse(sn.Value);
                                                break;
                                            case "maximpulse":
                                                ((AngleJoint)joint).MaxImpulse = float.Parse(sn.Value);
                                                break;
                                            case "softness":
                                                ((AngleJoint)joint).Softness = float.Parse(sn.Value);
                                                break;
                                            case "targetangle":
                                                ((AngleJoint)joint).TargetAngle = float.Parse(sn.Value);
                                                break;
                                        }
                                    }
                                    break;
                                case JointType.Motor:
                                    switch (sn.Name.ToLower())
                                    {
                                        case "angularoffset":
                                            ((MotorJoint)joint).AngularOffset = float.Parse(sn.Value);
                                            break;
                                        case "linearoffset":
                                            ((MotorJoint)joint).LinearOffset = ReadVector(sn);
                                            break;
                                        case "maxforce":
                                            ((MotorJoint)joint).MaxForce = float.Parse(sn.Value);
                                            break;
                                        case "maxtorque":
                                            ((MotorJoint)joint).MaxTorque = float.Parse(sn.Value);
                                            break;
                                        case "correctionfactor":
                                            ((MotorJoint)joint).CorrectionFactor = float.Parse(sn.Value);
                                            break;
                                    }
                                    break;
                            }
                        }
                    }
                }
            }

            world.ProcessChanges();
        }

        private static Vector2 ReadVector(XMLFragmentElement node)
        {
            string[] values = node.Value.Split(' ');
            return new Vector2(float.Parse(values[0]), float.Parse(values[1]));
        }

        private static object ReadSimpleType(XMLFragmentElement node, Type type, bool outer)
        {
            if (type == null)
                return ReadSimpleType(node.Elements[1], Type.GetType(node.Elements[0].Value), outer);

            XmlSerializer serializer = new XmlSerializer(type);
            XmlSerializerNamespaces xmlnsEmpty = new XmlSerializerNamespaces();
            xmlnsEmpty.Add("", "");

            using (MemoryStream stream = new MemoryStream())
            {
                StreamWriter writer = new StreamWriter(stream);
                {
                    writer.Write((outer) ? node.OuterXml : node.InnerXml);
                    writer.Flush();
                    stream.Position = 0;
                }
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.ConformanceLevel = ConformanceLevel.Fragment;

                return serializer.Deserialize(XmlReader.Create(stream, settings));
            }
        }
    }

    #region XMLFragment

    internal class XMLFragmentAttribute
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    internal class XMLFragmentElement
    {
        private List<XMLFragmentAttribute> _attributes = new List<XMLFragmentAttribute>();
        private List<XMLFragmentElement> _elements = new List<XMLFragmentElement>();

        public IList<XMLFragmentElement> Elements
        {
            get { return _elements; }
        }

        public IList<XMLFragmentAttribute> Attributes
        {
            get { return _attributes; }
        }

        public string Name { get; set; }
        public string Value { get; set; }
        public string OuterXml { get; set; }
        public string InnerXml { get; set; }
    }

    internal class XMLFragmentException : Exception
    {
        public XMLFragmentException(string message)
            : base(message)
        {
        }
    }

    internal class FileBuffer
    {
        public FileBuffer(Stream stream)
        {
            using (StreamReader sr = new StreamReader(stream))
                Buffer = sr.ReadToEnd();

            Position = 0;
        }

        public string Buffer { get; set; }

        public int Position { get; set; }

        private int Length
        {
            get { return Buffer.Length; }
        }

        public char Next
        {
            get
            {
                char c = Buffer[Position];
                Position++;
                return c;
            }
        }

        public bool EndOfBuffer
        {
            get { return Position == Length; }
        }
    }

    internal class XMLFragmentParser
    {
        private static List<char> _punctuation = new List<char> { '/', '<', '>', '=' };
        private FileBuffer _buffer;
        private XMLFragmentElement _rootNode;

        public XMLFragmentParser(Stream stream)
        {
            Load(stream);
        }

        public XMLFragmentParser(string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                Load(fs);
        }

        public XMLFragmentElement RootNode
        {
            get { return _rootNode; }
        }

        public void Load(Stream stream)
        {
            _buffer = new FileBuffer(stream);
        }

        public static XMLFragmentElement LoadFromStream(Stream stream)
        {
            XMLFragmentParser x = new XMLFragmentParser(stream);
            x.Parse();
            return x.RootNode;
        }

        private string NextToken()
        {
            string str = "";
            bool _done = false;

            while (true)
            {
                char c = _buffer.Next;

                if (_punctuation.Contains(c))
                {
                    if (str != "")
                    {
                        _buffer.Position--;
                        break;
                    }

                    _done = true;
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (str != "")
                        break;
                    else
                        continue;
                }

                str += c;

                if (_done)
                    break;
            }

            str = TrimControl(str);

            // Trim quotes from start and end
            if (str[0] == '\"')
                str = str.Remove(0, 1);

            if (str[str.Length - 1] == '\"')
                str = str.Remove(str.Length - 1, 1);

            return str;
        }

        private string PeekToken()
        {
            int oldPos = _buffer.Position;
            string str = NextToken();
            _buffer.Position = oldPos;
            return str;
        }

        private string ReadUntil(char c)
        {
            string str = "";

            while (true)
            {
                char ch = _buffer.Next;

                if (ch == c)
                {
                    _buffer.Position--;
                    break;
                }

                str += ch;
            }

            // Trim quotes from start and end
            if (str[0] == '\"')
                str = str.Remove(0, 1);

            if (str[str.Length - 1] == '\"')
                str = str.Remove(str.Length - 1, 1);

            return str;
        }

        private string TrimControl(string str)
        {
            string newStr = str;

            // Trim control characters
            int i = 0;
            while (true)
            {
                if (i == newStr.Length)
                    break;

                if (char.IsControl(newStr[i]))
                    newStr = newStr.Remove(i, 1);
                else
                    i++;
            }

            return newStr;
        }

        private string TrimTags(string outer)
        {
            int start = outer.IndexOf('>') + 1;
            int end = outer.LastIndexOf('<');

            return TrimControl(outer.Substring(start, end - start));
        }

        public XMLFragmentElement TryParseNode()
        {
            if (_buffer.EndOfBuffer)
                return null;

            int startOuterXml = _buffer.Position;
            string token = NextToken();

            if (token != "<")
                throw new XMLFragmentException("Expected \"<\", got " + token);

            XMLFragmentElement element = new XMLFragmentElement();
            element.Name = NextToken();

            while (true)
            {
                token = NextToken();

                if (token == ">")
                    break;
                else if (token == "/") // quick-exit case
                {
                    NextToken();

                    element.OuterXml =
                        TrimControl(_buffer.Buffer.Substring(startOuterXml, _buffer.Position - startOuterXml)).Trim();
                    element.InnerXml = "";

                    return element;
                }
                else
                {
                    XMLFragmentAttribute attribute = new XMLFragmentAttribute();
                    attribute.Name = token;
                    if ((token = NextToken()) != "=")
                        throw new XMLFragmentException("Expected \"=\", got " + token);
                    attribute.Value = NextToken();

                    element.Attributes.Add(attribute);
                }
            }

            while (true)
            {
                int oldPos = _buffer.Position; // for restoration below
                token = NextToken();

                if (token == "<")
                {
                    token = PeekToken();

                    if (token == "/") // finish element
                    {
                        NextToken(); // skip the / again
                        token = NextToken();
                        NextToken(); // skip >

                        element.OuterXml = TrimControl(_buffer.Buffer.Substring(startOuterXml, _buffer.Position - startOuterXml)).Trim();
                        element.InnerXml = TrimTags(element.OuterXml);

                        if (token != element.Name)
                            throw new XMLFragmentException("Mismatched element pairs: \"" + element.Name + "\" vs \"" +
                                                           token + "\"");

                        break;
                    }
                    else
                    {
                        _buffer.Position = oldPos;
                        element.Elements.Add(TryParseNode());
                    }
                }
                else
                {
                    // value, probably
                    _buffer.Position = oldPos;
                    element.Value = ReadUntil('<');
                }
            }

            return element;
        }

        private void Parse()
        {
            _rootNode = TryParseNode();

            if (_rootNode == null)
                throw new XMLFragmentException("Unable to load root node");
        }
    }

    #endregion
}