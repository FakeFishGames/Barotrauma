using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{

    class DockingPort : ItemComponent, IDrawableComponent
    {
        private static List<DockingPort> list = new List<DockingPort>();
        
        private Sprite overlaySprite;

        private Vector2 distanceTolerance;

        private DockingPort dockingTarget;

        private float dockingState;

        private Joint joint;

        private int dockingDir;

        private Hull[] hulls;

        private Body[] bodies;

        private Gap gap;

        [HasDefaultValue("32.0,32.0", false)]
        public string DistanceTolerance
        {
            get { return ToolBox.Vector2ToString(distanceTolerance); }
            set { distanceTolerance = ToolBox.ParseToVector2(value); }
        }

        [HasDefaultValue(32.0f, false)]
        public float DockedDistance
        {
            get;
            set;
        }

        [HasDefaultValue(true, false)]
        public bool IsHorizontal
        {
            get;
            set;
        }

        public override bool IsActive
        {
            get
            {
                return base.IsActive;
            }
            set
            {
                if (!IsActive && value)
                {
                    if (dockingTarget == null) AttemptDock();
                    if (dockingTarget == null) return;

                    base.IsActive = value;
                }
                else if (IsActive && !value)
                {
                    Undock();
                }

                //base.IsActive = value;
            }
        }

        public DockingPort(Item item, XElement element)
            : base(item, element)
        {
            // isOpen = false;
            foreach (XElement subElement in element.Elements())
            {
                string texturePath = ToolBox.GetAttributeString(subElement, "texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        overlaySprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                }
            }

            list.Add(this);
        }

        private void AttemptDock()
        {
            foreach (DockingPort port in list)
            {
                if (port == this || port.item.Submarine == item.Submarine) continue;

                if (Math.Abs(port.item.WorldPosition.X - item.WorldPosition.X) > distanceTolerance.X) continue;
                if (Math.Abs(port.item.WorldPosition.Y - item.WorldPosition.Y) > distanceTolerance.Y) continue;

                Dock(port);
                return;

            }
        }

        private void Dock(DockingPort target)
        {
            if (dockingTarget!=null)
            {
                Undock();
            }

            dockingTarget = target;
            dockingTarget.dockingTarget = this;
            dockingTarget.IsActive = true;

            dockingDir = Math.Sign(dockingTarget.item.WorldPosition.X - item.WorldPosition.X);
            dockingTarget.dockingDir = -dockingDir;
            
            CreateJoint(false);
        }


        private void CreateJoint(bool useWeldJoint)
        {
            Vector2 offset = (IsHorizontal ?
                Vector2.UnitX * Math.Sign(dockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                Vector2.UnitY * Math.Sign(dockingTarget.item.WorldPosition.Y - item.WorldPosition.Y));
            offset *= DockedDistance * 0.5f;

            Vector2 pos1 = item.WorldPosition + offset;

            Vector2 pos2 = dockingTarget.item.WorldPosition - offset;

            if (useWeldJoint)
            {
                joint = JointFactory.CreateWeldJoint(GameMain.World,
                    item.Submarine.SubBody.Body, dockingTarget.item.Submarine.SubBody.Body,
                    ConvertUnits.ToSimUnits(pos1), FarseerPhysics.ConvertUnits.ToSimUnits(pos2), true);

                ((WeldJoint)joint).FrequencyHz = 1.0f;
            }
            else
            {
                var distanceJoint = JointFactory.CreateDistanceJoint(GameMain.World,
                    item.Submarine.SubBody.Body, dockingTarget.item.Submarine.SubBody.Body,
                    ConvertUnits.ToSimUnits(pos1), FarseerPhysics.ConvertUnits.ToSimUnits(pos2), true);

                distanceJoint.Length = 0.01f;
                distanceJoint.Frequency = 1.0f;
                distanceJoint.DampingRatio = 0.8f;

                joint = distanceJoint;
            }


            joint.CollideConnected = true;
        }

        private void CreateHull()
        {
            var hullRects = new Rectangle[] { item.WorldRect, dockingTarget.item.WorldRect };
            var subs = new Submarine[] { item.Submarine, dockingTarget.item.Submarine };

            hulls = new Hull[2];
            bodies = new Body[4];
            if (IsHorizontal)
            {
                if (hullRects[0].Center.X > hullRects[1].Center.X)
                {
                    hullRects = new Rectangle[] { dockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { dockingTarget.item.Submarine,item.Submarine };
                }


                hullRects[0] = new Rectangle(hullRects[0].Center.X, hullRects[0].Y, ((int)DockedDistance / 2), hullRects[0].Height);
                hullRects[1] = new Rectangle(hullRects[1].Center.X - ((int)DockedDistance / 2), hullRects[1].Y, ((int)DockedDistance / 2), hullRects[1].Height);

                

                for (int i = 0; i < 2;i++ )
                {
                    hullRects[i].Location -= (subs[i].WorldPosition - subs[i].HiddenSubPosition).ToPoint();
                    hulls[i] = new Hull(MapEntityPrefab.list.Find(m => m.Name == "Hull"), hullRects[i], subs[i]);
                    hulls[i].AddToGrid(subs[i]);


                    for (int j = 0; j < 2; j++)
                    {
                        bodies[i + j * 2] = BodyFactory.CreateEdge(GameMain.World,
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X, hullRects[i].Y - hullRects[i].Height * j)),
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].Right, hullRects[i].Y - hullRects[i].Height * j)));

                        //bodies[i + j * 2] = BodyFactory.CreateRectangle(GameMain.World, ConvertUnits.ToSimUnits(hullRects[i].Width), 0.1f, 5.0f);
                        //bodies[i + j * 2].SetTransform(ConvertUnits.ToSimUnits(new Vector2(hullRects[i].Center.X, hullRects[i].Y - (hullRects[i].Height+5) * j)), 0.0f);
                    }
                }

                gap = new Gap(new Rectangle(hullRects[0].Right-2, hullRects[0].Y, 4, hullRects[0].Height), true, item.Submarine);
                gap.linkedTo.Clear();
                gap.linkedTo.Add(hulls[0]);
                gap.linkedTo.Add(hulls[1]);

                    //var hullRect1 = new Rectangle(hullRects.Min(h => h.Center.X), hullRect.Y, ((int)DockedDistance / 2), hullRects[0].Height);
                    //var hullRect2 = new Rectangle(hullRects.Max(h => h.Center.X), hullRect.Y, ((int)DockedDistance / 2), hullRects[0].Height);

                    //var sub1 = hullRect.Center.X < targetRect.Center.X ? item.Submarine : dockingTarget.item.Submarine;
                    //var sub2 = hullRect.Center.X > targetRect.Center.X ? item.Submarine : dockingTarget.item.Submarine;

                //    hullRect1.Location -= (sub1.WorldPosition - sub1.HiddenSubPosition).ToPoint();
                //hulls[0] = new Hull(MapEntityPrefab.list.Find(m => m.Name == "Hull"), hullRect1, sub1);
                //hulls[0].AddToGrid(sub1);

                //hullRect2.Location -= (sub2.WorldPosition - sub2.HiddenSubPosition).ToPoint();
                //hulls[1] = new Hull(MapEntityPrefab.list.Find(m => m.Name == "Hull"), hullRect2, sub2);
                //hulls[1].AddToGrid(sub2);


            }
            else
            {
                //hullRect = new Rectangle(hullRect.X,
                //    Math.Max(hullRect.Y - hullRect.Height / 2, targetRect.Y - targetRect.Height / 2), hullRect.Width, (int)DockedDistance);
            }

            foreach (Body body in bodies)
            {
                body.BodyType = BodyType.Static;
                body.Friction = 0.5f;

                body.CollisionCategories = Physics.CollisionWall;
            }


        }

        private void Undock()
        {
            if (dockingTarget == null) return;

            dockingTarget.dockingTarget = null;
            dockingTarget.IsActive = false;

            dockingTarget = null;

            GameMain.World.RemoveJoint(joint);
            joint = null;

            hulls[0].Remove();
            hulls[1].Remove();

            gap.Remove();
            gap = null;

            foreach (Body body in bodies)
            {
                GameMain.World.RemoveBody(body);
            }
            bodies = null;


            
            //foreach (Gap g in hulls[0].ConnectedGaps)
            //{
            //    g.Remove();
            //}

            //foreach (Gap g in hulls[1].ConnectedGaps)
            //{
            //    g.Remove();
            //}


            hulls = null;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (dockingTarget==null)
            {
                dockingState = MathHelper.Lerp(dockingState, 0.0f, deltaTime * 10.0f);
                if (dockingState < 0.01f) base.IsActive = false;
            }
            else
            {
                if (joint is DistanceJoint && Vector2.Distance(joint.WorldAnchorA, joint.WorldAnchorB) < 0.05f)
                {
                    GameMain.World.RemoveJoint(joint);

                    CreateJoint(true);
                    CreateHull();
                }

                dockingState = MathHelper.Lerp(dockingState, 1.0f, deltaTime * 10.0f);
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (dockingState == 0.0f) return;
            
            Vector2 drawPos = item.DrawPosition;
            drawPos.Y = -drawPos.Y;

            var rect = overlaySprite.SourceRect;
            drawPos.Y -= rect.Height / 2;

            if (IsHorizontal)
            {
                if (dockingDir == 1)
                {
                    spriteBatch.Draw(overlaySprite.Texture, 
                        drawPos,
                        new Rectangle(
                            rect.Center.X + (int)(rect.Width / 2 * (1.0f - dockingState)), rect.Y,
                            (int)(rect.Width / 2 * dockingState), rect.Height), Color.White);
        
                }
                else
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos - Vector2.UnitX * (rect.Width / 2 * dockingState),
                        new Rectangle(
                            rect.X, rect.Y,
                            (int)(rect.Width / 2 * dockingState), rect.Height), Color.Red);
                }              
            }
            else
            {
                if (dockingDir == 1)
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos,
                        new Rectangle(
                            rect.X, rect.Y + rect.Height/2 + (int)(rect.Height / 2 * (1.0f - dockingState)),
                            rect.Width, (int)(rect.Height / 2 * dockingState)), Color.White);

                }
                else
                {
                    spriteBatch.Draw(overlaySprite.Texture,
                        drawPos - Vector2.UnitY * (rect.Height / 2 * dockingState),
                        new Rectangle(
                            rect.X, rect.Y,
                            rect.Width, (int)(rect.Height / 2 * dockingState)), Color.Red);
                }      
            }
        }

        protected override void RemoveComponentSpecific()
        {
            list.Remove(this);
        }
    }
}
