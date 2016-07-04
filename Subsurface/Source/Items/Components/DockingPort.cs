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
        public static List<DockingPort> list = new List<DockingPort>();
        
        private Sprite overlaySprite;
        
        private Vector2 distanceTolerance;

        private DockingPort dockingTarget;

        private float dockingState;
        private int dockingDir;

        private Joint joint;

        private Hull[] hulls;

        private Body[] bodies;

        private Gap gap;

        private bool docked;

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

        public bool Docked
        {
            get
            {
                return docked;
            }
            set
            {
                if (!docked && value)
                {
                    if (dockingTarget == null) AttemptDock();
                    if (dockingTarget == null) return;

                    docked = true;
                }
                else if (docked && !value)
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

            IsActive = true;

            list.Add(this);
        }

        private DockingPort FindAdjacentPort()
        {
            foreach (DockingPort port in list)
            {
                if (port == this || port.item.Submarine == item.Submarine) continue;

                if (Math.Abs(port.item.WorldPosition.X - item.WorldPosition.X) > distanceTolerance.X) continue;
                if (Math.Abs(port.item.WorldPosition.Y - item.WorldPosition.Y) > distanceTolerance.Y) continue;

                return port;
            }

            return null;
        }

        private void AttemptDock()
        {
            var adjacentPort = FindAdjacentPort();
            if (adjacentPort != null) Dock(adjacentPort);
        }

        public void Dock(DockingPort target)
        {
            if (dockingTarget != null)
            {
                Undock();
            }

            PlaySound(ActionType.OnUse, item.WorldPosition);

            item.linkedTo.Add(target.item);

            if (!target.item.Submarine.DockedTo.Contains(item.Submarine)) target.item.Submarine.DockedTo.Add(item.Submarine);
            if (!item.Submarine.DockedTo.Contains(target.item.Submarine)) item.Submarine.DockedTo.Add(target.item.Submarine);

            dockingTarget = target;
            dockingTarget.dockingTarget = this;

            docked = true;
            dockingTarget.Docked = true;

            if (Character.Controlled != null &&
                (Character.Controlled.Submarine == dockingTarget.item.Submarine || Character.Controlled.Submarine == item.Submarine))
            {
                GameMain.GameScreen.Cam.Shake = Vector2.Distance(dockingTarget.item.Submarine.Velocity, item.Submarine.Velocity);
            }

            dockingDir = IsHorizontal ? 
                Math.Sign(dockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                Math.Sign(item.WorldPosition.Y - dockingTarget.item.WorldPosition.Y);
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
                    }
                }

                gap = new Gap(new Rectangle(hullRects[0].Right - 2, hullRects[0].Y, 4, hullRects[0].Height), true, subs[0]);

                gap.linkedTo.Clear();
                if (hulls[0].WorldRect.X < hulls[1].WorldRect.X)
                {
                    gap.linkedTo.Add(hulls[0]);
                    gap.linkedTo.Add(hulls[1]);
                }
                else
                {
                    gap.linkedTo.Add(hulls[1]);
                    gap.linkedTo.Add(hulls[0]);
                }


            }
            else
            {
                if (hullRects[0].Center.Y > hullRects[1].Center.Y)
                {
                    hullRects = new Rectangle[] { dockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { dockingTarget.item.Submarine, item.Submarine };
                }

                hullRects[0] = new Rectangle(hullRects[0].X, hullRects[0].Y + (int)(-hullRects[0].Height+DockedDistance)/2, hullRects[0].Width, ((int)DockedDistance / 2));
                hullRects[1] = new Rectangle(hullRects[1].X, hullRects[1].Y - hullRects[1].Height/2, hullRects[1].Width, ((int)DockedDistance / 2));

                for (int i = 0; i < 2; i++)
                {
                    hullRects[i].Location -= (subs[i].WorldPosition - subs[i].HiddenSubPosition).ToPoint();
                    hulls[i] = new Hull(MapEntityPrefab.list.Find(m => m.Name == "Hull"), hullRects[i], subs[i]);
                    hulls[i].AddToGrid(subs[i]);

                    //for (int j = 0; j < 2; j++)
                    //{
                    //    bodies[i + j * 2] = BodyFactory.CreateEdge(GameMain.World,
                    //        ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X + hullRects[i].Width * j, hullRects[i].Y)),
                    //        ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X + hullRects[i].Width * j, hullRects[i].Y-hullRects[i].Height)));
                    //}
                }

                gap = new Gap(new Rectangle(hullRects[0].X, hullRects[0].Y+2, hullRects[0].Width, 4), false, subs[0]);

                gap.linkedTo.Clear();
                if (hulls[0].WorldRect.Y > hulls[1].WorldRect.Y)
                {
                    gap.linkedTo.Add(hulls[0]);
                    gap.linkedTo.Add(hulls[1]);
                }
                else
                {
                    gap.linkedTo.Add(hulls[1]);
                    gap.linkedTo.Add(hulls[0]);
                }
            }


            foreach (Body body in bodies)
            {
                if (body == null) continue;
                body.BodyType = BodyType.Static;
                body.Friction = 0.5f;

                body.CollisionCategories = Physics.CollisionWall;
            }


        }

        private void Undock()
        {
            if (dockingTarget == null || !docked) return;

            PlaySound(ActionType.OnUse, item.WorldPosition);

            dockingTarget.item.Submarine.DockedTo.Remove(item.Submarine);
            item.Submarine.DockedTo.Remove(dockingTarget.item.Submarine);

            item.linkedTo.Clear();

            docked = false;

            dockingTarget.Undock();
            dockingTarget = null;

            if (joint != null)
            {
                GameMain.World.RemoveJoint(joint);
                joint = null;
            }

            if (hulls != null)
            {
                hulls[0].Remove();
                hulls[1].Remove();
                hulls = null;
            }

            if (gap != null)
            {
                gap.Remove();
                gap = null;
            }

            if (bodies!=null)
            {
                foreach (Body body in bodies)
                {
                    if (body == null) continue;
                    GameMain.World.RemoveBody(body);
                }
                bodies = null;
            }

        }

        public override void Update(float deltaTime, Camera cam)
        {


            if (dockingTarget == null)
            {
                dockingState = MathHelper.Lerp(dockingState, 0.0f, deltaTime * 10.0f);
                if (dockingState < 0.01f) docked = false;

                item.SendSignal(0, "0", "state_out");

                item.SendSignal(0, (FindAdjacentPort() != null) ? "1" : "0", "proximity_sensor");

            }
            else
            {
                if (joint is DistanceJoint)
                {
                    item.SendSignal(0, "0", "state_out");

                    if (Vector2.Distance(joint.WorldAnchorA, joint.WorldAnchorB) < 0.05f)
                    {
                        GameMain.World.RemoveJoint(joint);
                        
                        PlaySound(ActionType.OnSecondaryUse, item.WorldPosition);

                        CreateJoint(true);
                        CreateHull();
                    }
                    dockingState = MathHelper.Lerp(dockingState, 0.5f, deltaTime * 10.0f);
                }
                else
                {
                    item.SendSignal(0, "1", "state_out");

                    dockingState = MathHelper.Lerp(dockingState, 1.0f, deltaTime * 10.0f);
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (dockingState == 0.0f) return;
            
            Vector2 drawPos = item.DrawPosition;
            drawPos.Y = -drawPos.Y;

            var rect = overlaySprite.SourceRect;
            
            if (IsHorizontal)
            {
                drawPos.Y -= rect.Height / 2;

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
                drawPos.X -= rect.Width / 2;

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

        public override void OnMapLoaded()
        {
            if (!item.linkedTo.Any()) return;

            Item linkedItem = item.linkedTo.First() as Item;
            if (linkedItem == null) return;

            DockingPort port = linkedItem.GetComponent<DockingPort>();
            if (port != null) Dock(port);
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power = 0.0f)
        {
            switch (connection.Name)
            {
                case "toggle":
                    Docked = !docked;
                    break;
                case "set_active":
                case "set_state":
                    Docked = signal != "0";
                    break;
            }
        }
    }
}
