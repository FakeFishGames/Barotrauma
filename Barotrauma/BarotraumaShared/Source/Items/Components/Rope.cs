using FarseerPhysics;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class Rope : ItemComponent, IDrawableComponent
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
            string spritePath = element.GetAttributeString("sprite", "");
            if (spritePath == "") DebugConsole.ThrowError("Sprite "+spritePath+" in "+element+" not found!");

            float length = ConvertUnits.ToSimUnits(element.GetAttributeFloat("length", 200.0f));

            pullForce = element.GetAttributeFloat("pullforce", 10.0f);

            projectileAnchor = Vector2.Zero;
            projectileAnchor.X = element.GetAttributeFloat("projectileanchorx", 0.0f);
            projectileAnchor.Y = element.GetAttributeFloat("projectileanchory", 0.0f);
            projectileAnchor = ConvertUnits.ToSimUnits(projectileAnchor);
   
            sprite = new Sprite(spritePath, new Vector2(0.5f,0.5f));
            sectionLength = ConvertUnits.ToSimUnits(sprite.size.X);
            
            Path ropePath = new Path();
            ropePath.Add(item.body.SimPosition);
            ropePath.Add(item.body.SimPosition + new Vector2(length, 0.0f));
            ropePath.Closed = false;

            Vertices box = PolygonTools.CreateRectangle(sectionLength, 0.05f);
            PolygonShape shape = new PolygonShape(box, 5);
            
            List<Body>ropeList = PathManager.EvenlyDistributeShapesAlongPath(GameMain.World, ropePath, shape, BodyType.Dynamic, (int)(length/sectionLength));
            
            ropeBodies = new PhysicsBody[ropeList.Count];
            for (int i = 0; i<ropeBodies.Length; i++)
            {
                ropeList[i].Mass = 0.01f;
                ropeList[i].Enabled = false;
                //only collide with the map
                ropeList[i].CollisionCategories = Physics.CollisionItem;
                ropeList[i].CollidesWith = Physics.CollisionWall;

                //ropeBodies[i] = new PhysicsBody(ropeList[i]);
            }

            List<RevoluteJoint> joints = PathManager.AttachBodiesWithRevoluteJoint(GameMain.World, ropeList, 
                new Vector2(-sectionLength/2, 0.0f), new Vector2(sectionLength/2, 0.0f), false, false);

            ropeJoints = new RevoluteJoint[joints.Count+1];
            //ropeJoints[0] = JointFactory.CreateRevoluteJoint(Game1.world, item.body, ropeList[0], new Vector2(0f, -0.0f)); 
            for (int i = 0; i < joints.Count; i++)
            {
                var distanceJoint = JointFactory.CreateDistanceJoint(GameMain.World, ropeList[i], ropeList[i + 1]);

                distanceJoint.Length = sectionLength;
                distanceJoint.DampingRatio = 1.0f;
                ropeJoints[i] = joints[i];
            }

        }

        public override bool SecondaryUse(float deltaTime, Character character = null)
        {
            if (reload > 0.0f) return false;

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
            return true;
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
                item.Combine(projectile, user: null);
                ropeBodies[ropeBodies.Length - 1].Enabled = false;
                IsActive = false;
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
                Vector2 stopForce = projectile.SimPosition - ropeBodies[ropeBodies.Length-1].SimPosition;
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
                if (!projectile.body.Enabled) item.Combine(projectile, user: null);

                foreach (PhysicsBody b in ropeBodies)
                {
                    b.Enabled = false;
                }
                foreach (var joint in ropeJoints)
                {
                    if (joint != null) joint.Enabled = false;
                }
                IsActive = false;
            }
        }

        public void Attach(Item projectile)
        {
            reload = 0.5f;
            IsActive = true;

            this.projectile = projectile;
            //Projectile projectileComponent = projectile.GetComponent<Projectile>();

            foreach (PhysicsBody b in ropeBodies)
            {
                b.SetTransform(item.body.SimPosition, 0.0f);
                b.UserData = false;
                b.Enabled = true;
            }

            foreach (var joint in ropeJoints)
            {
                if (joint!=null) joint.Enabled = true;                
            }

            ropeBodies[ropeBodies.Length - 1].SetTransform(projectile.body.SimPosition, projectile.body.Rotation);

            //attach projectile to the last section of the rope
            if (ropeJoints[ropeJoints.Length-1] != null) GameMain.World.RemoveJoint(ropeJoints[ropeJoints.Length-1]);
            ropeJoints[ropeJoints.Length - 1] = JointFactory.CreateRevoluteJoint(GameMain.World, 
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

            if (gunJoint != null) GameMain.World.RemoveJoint(gunJoint);
            gunJoint = new DistanceJoint(item.body.FarseerBody, body, BarrelPos,
                new Vector2(sectionLength / 2, 0.0f));

            gunJoint.Length = sectionLength;

            //gunJoint.LocalAnchorA = BarrelPos;
            //gunJoint.LocalAnchorB = new Vector2(sectionLength / 2, 0.0f);
            //gunJoint.UpperLimit = sectionLength;
            //gunJoint.LowerLimit = 0.0f;
            //gunJoint.LimitEnabled = true;
            //gunJoint.ReferenceAngle = 0.0f;

            GameMain.World.AddJoint(gunJoint);
        }

    }
}
