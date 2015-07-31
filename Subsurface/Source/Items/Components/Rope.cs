using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Items.Components
{
    class Rope : ItemComponent
    {
        PhysicsBody[] ropeBodies;
        RevoluteJoint[] ropeJoints;

        DistanceJoint gunJoint;

        float pullForce;

        Sprite sprite;

        float reload;

        float prevDir;

        float sectionLength;

        Item projectile;

        Vector2 projectileAnchor;

        private Vector2 BarrelPos
        {
            get
            {
                Vector2 barrelPos = Vector2.Zero;

                //RangedWeapon weapon = item.GetComponent<RangedWeapon>();
                //if (weapon != null) barrelPos = weapon.barrelPos;
                
                return barrelPos;
            }
        }

        private Vector2 TransformedBarrelPos
        {
            get
            {
                Vector2 barrelPos = Vector2.Zero;

                RangedWeapon weapon = item.GetComponent<RangedWeapon>();
                if (weapon != null) barrelPos = weapon.TransformedBarrelPos;

                return barrelPos;
            }
        }
             

        public Rope(Item item, XElement element)
            : base(item, element)
        {
            string spritePath = ToolBox.GetAttributeString(element, "sprite", "");
            if (spritePath == "") DebugConsole.ThrowError("Sprite "+spritePath+" in "+element+" not found!");

            float length = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "length", 200.0f));

            pullForce = ToolBox.GetAttributeFloat(element, "pullforce", 10.0f);

            projectileAnchor = Vector2.Zero;
            projectileAnchor.X = ToolBox.GetAttributeFloat(element, "projectileanchorx", 0.0f);
            projectileAnchor.Y = ToolBox.GetAttributeFloat(element, "projectileanchory", 0.0f);
            projectileAnchor = ConvertUnits.ToSimUnits(projectileAnchor);

            characterUsable = ToolBox.GetAttributeBool(element, "characterusable", false);
                            
            sprite = new Sprite(spritePath, new Vector2(0.5f,0.5f));
            sectionLength = ConvertUnits.ToSimUnits(sprite.size.X);


            Path ropePath = new Path();
            ropePath.Add(item.body.Position);
            ropePath.Add(item.body.Position + new Vector2(length, 0.0f));
            ropePath.Closed = false;

            Vertices box = PolygonTools.CreateRectangle(sectionLength, 0.05f);
            PolygonShape shape = new PolygonShape(box, 5);
            
            List<Body>ropeList = PathManager.EvenlyDistributeShapesAlongPath(Game1.World, ropePath, shape, BodyType.Dynamic, (int)(length/sectionLength));
            
            ropeBodies = new PhysicsBody[ropeList.Count()];
            for (int i = 0; i<ropeBodies.Length; i++)
            {
                ropeList[i].Mass = 0.01f;
                ropeList[i].Enabled = false;
                //only collide with the map
                ropeList[i].CollisionCategories = Physics.CollisionMisc;
                ropeList[i].CollidesWith = Physics.CollisionWall;

                ropeBodies[i] = new PhysicsBody(ropeList[i]);
            }

            List<RevoluteJoint> joints = PathManager.AttachBodiesWithRevoluteJoint(Game1.World, ropeList, 
                new Vector2(-sectionLength/2, 0.0f), new Vector2(sectionLength/2, 0.0f), false, false);

            ropeJoints = new RevoluteJoint[joints.Count+1];
            //ropeJoints[0] = JointFactory.CreateRevoluteJoint(Game1.world, item.body, ropeList[0], new Vector2(0f, -0.0f)); 
            for (int i = 0; i < joints.Count; i++)
            {
                var distanceJoint = JointFactory.CreateDistanceJoint(Game1.World, ropeList[i], ropeList[i + 1]);

                distanceJoint.Length = sectionLength;
                distanceJoint.DampingRatio = 1.0f;
                ropeJoints[i] = joints[i];
            }

        }

        public override void SecondaryUse(float deltaTime, Character character = null)
        {
            if (reload > 0.0f) return;

            bool first = true;
            for (int i = 0; i < ropeBodies.Length - 1; i++)
            {
                if (ropeBodies[i].UserData == null || (bool)ropeBodies[i].UserData) continue;

                if (first)
                {
                    Vector2 dist = gunJoint.WorldAnchorA - ropeJoints[i].WorldAnchorA;
                    float length = dist.Length();

                    if (gunJoint.Length < 0.011 && length*0.5f<sectionLength)
                    {
                        NextSection(i);
                    }
                    else
                    {
                        gunJoint.Length = Math.Max(gunJoint.Length-0.01f,0.01f);
                        gunJoint.Frequency = 30;
                        gunJoint.DampingRatio = 0.05f;
                        //gunJoint.MotorEnabled = true;
                        //gunJoint.MotorSpeed = -150.0f;
                        //ropeBodies[i + 1].ApplyForce(dist / length * pullForce);
                        //ropeJoints[0].LocalAnchorA = new Vector2(ropeJoints[0].LocalAnchorA.X-0.05f,ropeJoints[0].LocalAnchorA.Y);
                        //ropeBodies[i].SmoothRotate(item.body.Rotation);
                    }

                    first = false;
                }
                else
                {
                    //Vector2 dist = ropeBodies[i].Position - ropeBodies[i + 1].Position;
                    //float length = dist.Length();

                    //ropeBodies[i + 1].ApplyForce(dist / length * pullForce * 0.1f);
                }
            }           
        }

        private void NextSection(int i)
        {
            gunJoint.Length = sectionLength;
            ropeBodies[i].UserData = true;
            ropeBodies[i].Enabled = false;

            //if (ropeJoints[0] != null) Game1.world.RemoveJoint(ropeJoints[0]);
            //ropeJoints[0] = JointFactory.CreateRevoluteJoint(Game1.world, 
            //    item.body.FarseerBody, ropeBodies[i + 1].FarseerBody, 
            //    BarrelPos, new Vector2(-sectionLength / 2, 0.0f));

            AttachGunJoint(ropeBodies[i + 1].FarseerBody);

            if (i == ropeBodies.Length - 2)
            {
                item.Combine(projectile);
                ropeBodies[ropeBodies.Length - 1].Enabled = false;
                isActive = false;
            }
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (reload>0.0f) reload -= deltaTime;

            //for (int i = 0; i < ropeBodies.Length - 1; i++)
            //{
            //    if (ropeBodies[i].UserData == null || (bool)ropeBodies[i].UserData == true) continue;
            //    ropeBodies[i].SmoothRotate(item.body.Rotation);
            //}

            int len = 1;
            for (int i = 0; i < ropeBodies.Length - 1; i++)
            {
                if (ropeBodies[i].UserData == null || (bool)ropeBodies[i].UserData) continue;
                len++;
            }

            if (Vector2.Distance(TransformedBarrelPos, projectile.SimPosition)>len*sectionLength)
            {
                Vector2 stopForce = projectile.SimPosition - ropeBodies[ropeBodies.Length-1].Position;
                stopForce = Vector2.Normalize(stopForce);

                float dotProduct = Vector2.Dot(stopForce, Vector2.Normalize(projectile.body.LinearVelocity));

                if (dotProduct<0)
                    projectile.body.ApplyLinearImpulse(-stopForce*dotProduct * projectile.body.LinearVelocity.Length() * projectile.body.Mass);
            }

            if (item.body.Dir!=prevDir)
            {
                gunJoint.LocalAnchorA =
                    new Vector2(
                        -gunJoint.LocalAnchorA.X,
                       BarrelPos.Y);

                prevDir = -prevDir;
            }

            if (!projectile.body.Enabled || !item.body.Enabled)
            {
                //attempt to recontain the projectile in the launcher
                //eq automatically reload a spear into a speargun when picking the spear up
                if (!projectile.body.Enabled) item.Combine(projectile);

                foreach (PhysicsBody b in ropeBodies)
                {
                    b.Enabled = false;
                }
                foreach (var joint in ropeJoints)
                {
                    if (joint != null) joint.Enabled = false;
                }
                isActive = false;
            }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            base.Draw(spriteBatch);

            if (!isActive) return;

            RevoluteJoint firstJoint = null;

            for (int i = 0; i<ropeBodies.Length-1; i++)
            {
                if (!ropeBodies[i].Enabled) continue;

                if (firstJoint==null) firstJoint = ropeJoints[i];
                
                DrawSection(spriteBatch, ropeJoints[i].WorldAnchorA, ropeJoints[i+1].WorldAnchorA, i);
            }

            if (gunJoint == null || firstJoint==null) return;

            DrawSection(spriteBatch, gunJoint.WorldAnchorA, firstJoint.WorldAnchorA, 0);

        }

        private void DrawSection(SpriteBatch spriteBatch, Vector2 start, Vector2 end, int i)
        {
            start.Y = -start.Y;
            end.Y = -end.Y;

            spriteBatch.Draw(sprite.Texture,
                ConvertUnits.ToDisplayUnits(start), null, Color.White,
                MathUtils.VectorToAngle(end - start),
                new Vector2(0.0f, sprite.size.Y / 2.0f),
                new Vector2((ConvertUnits.ToDisplayUnits(Vector2.Distance(start, end))) / sprite.Texture.Width, 1.0f),
                SpriteEffects.None,
                sprite.Depth + i*0.00001f);
        }
        
        public void Attach(Item projectile)
        {
            reload = 0.5f;
            isActive = true;

            this.projectile = projectile;
            //Projectile projectileComponent = projectile.GetComponent<Projectile>();

            foreach (PhysicsBody b in ropeBodies)
            {
                b.SetTransform(item.body.Position, 0.0f);
                b.UserData = false;
                b.Enabled = true;
            }

            foreach (var joint in ropeJoints)
            {
                if (joint!=null) joint.Enabled = true;                
            }

            ropeBodies[ropeBodies.Length - 1].SetTransform(projectile.body.Position, projectile.body.Rotation);

            //attach projectile to the last section of the rope
            if (ropeJoints[ropeJoints.Length-1] != null) Game1.World.RemoveJoint(ropeJoints[ropeJoints.Length-1]);
            ropeJoints[ropeJoints.Length - 1] = JointFactory.CreateRevoluteJoint(Game1.World, 
                projectile.body.FarseerBody, ropeBodies[ropeBodies.Length - 1].FarseerBody, 
                projectileAnchor, new Vector2(sectionLength / 2, 0.0f));

            AttachGunJoint(ropeBodies[0].FarseerBody);

            prevDir = item.body.Dir;
        }

        private void AttachGunJoint(Body body)
        {
            float rotation = (item.body.Dir == -1.0f) ? item.body.Rotation - MathHelper.Pi : item.body.Rotation;
            body.SetTransform(TransformedBarrelPos, rotation);
            //Vector2 axis = new Vector2((float)Math.Cos(rotation), (float)Math.Sin(rotation));

            if (gunJoint != null) Game1.World.RemoveJoint(gunJoint);
            gunJoint = new DistanceJoint(item.body.FarseerBody, body, BarrelPos,
                new Vector2(sectionLength / 2, 0.0f));

            gunJoint.Length = sectionLength;

            //gunJoint.LocalAnchorA = BarrelPos;
            //gunJoint.LocalAnchorB = new Vector2(sectionLength / 2, 0.0f);
            //gunJoint.UpperLimit = sectionLength;
            //gunJoint.LowerLimit = 0.0f;
            //gunJoint.LimitEnabled = true;
            //gunJoint.ReferenceAngle = 0.0f;

            Game1.World.AddJoint(gunJoint);
        }

    }
}
