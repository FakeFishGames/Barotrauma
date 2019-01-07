using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        private static List<DockingPort> list = new List<DockingPort>();
        public static IEnumerable<DockingPort> List
        {
            get { return list; }
        }

        private Sprite overlaySprite;

        private Vector2 distanceTolerance;

        private DockingPort dockingTarget;

        private float dockingState;
        private int dockingDir;

        private Joint joint;

        private readonly Hull[] hulls = new Hull[2];
        private Gap gap;

        private Door door;

        private Body[] bodies;
        private Body doorBody;

        private bool docked;

        public int DockingDir
        {
            get { return dockingDir; }
            set { dockingDir = value; }
        }

        [Serialize("32.0,32.0", false)]
        public Vector2 DistanceTolerance
        {
            get { return distanceTolerance; }
            set { distanceTolerance = value; }
        }

        [Serialize(32.0f, false)]
        public float DockedDistance
        {
            get;
            set;
        }

        [Serialize(true, false)]
        public bool IsHorizontal
        {
            get;
            set;
        }

        public DockingPort DockingTarget
        {
            get { return dockingTarget; }
            set { dockingTarget = value; }
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
                string texturePath = subElement.GetAttributeString("texture", "");
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

        public override void FlipX(bool relativeToSub)
        {
            if (dockingTarget != null)
            {
                if (joint != null)
                {
                    CreateJoint(joint is WeldJoint);
                    LinkHullsToGaps();
                }
                else if (dockingTarget.joint != null)
                {
                    if (!GameMain.World.BodyList.Contains(dockingTarget.joint.BodyA) ||
                        !GameMain.World.BodyList.Contains(dockingTarget.joint.BodyB))
                    {
                        dockingTarget.CreateJoint(dockingTarget.joint is WeldJoint);
                    }
                    dockingTarget.LinkHullsToGaps();
                }
            }
        }

        public override void FlipY(bool relativeToSub)
        {
            FlipX(relativeToSub);
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
            if (item.Submarine.DockedTo.Contains(target.item.Submarine)) return;
                        
            if (dockingTarget != null)
            {
                Undock();
            }

            if (target.item.Submarine == item.Submarine)
            {
                DebugConsole.ThrowError("Error - tried to dock a submarine to itself");
                dockingTarget = null;
                return;
            }

#if CLIENT
            PlaySound(ActionType.OnUse, item.WorldPosition);
#endif

            if (!item.linkedTo.Contains(target.item)) item.linkedTo.Add(target.item);
            if (!target.item.linkedTo.Contains(item)) target.item.linkedTo.Add(item);

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
                Math.Sign(dockingTarget.item.WorldPosition.Y - item.WorldPosition.Y);
            dockingTarget.dockingDir = -dockingDir;

            if (door != null && dockingTarget.door != null)
            {
                WayPoint myWayPoint = WayPoint.WayPointList.Find(wp => door.LinkedGap == wp.ConnectedGap);
                WayPoint targetWayPoint = WayPoint.WayPointList.Find(wp => dockingTarget.door.LinkedGap == wp.ConnectedGap);

                if (myWayPoint != null && targetWayPoint != null)
                {
                    myWayPoint.linkedTo.Add(targetWayPoint);
                    targetWayPoint.linkedTo.Add(myWayPoint);
                }
            }
            
            CreateJoint(false);

#if SERVER
            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
#endif
        }

        public void Lock(bool isNetworkMessage)
        {
#if CLIENT
            if (GameMain.Client != null && !isNetworkMessage) return;
#endif

            if (dockingTarget == null)
            {
                DebugConsole.ThrowError("Error - attempted to lock a docking port that's not connected to anything");
                return;
            }

            if (!(joint is WeldJoint))
            {

                dockingDir = IsHorizontal ?
                    Math.Sign(dockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                    Math.Sign(dockingTarget.item.WorldPosition.Y - item.WorldPosition.Y);
                dockingTarget.dockingDir = -dockingDir;

#if CLIENT
                PlaySound(ActionType.OnSecondaryUse, item.WorldPosition);
#endif

                ConnectWireBetweenPorts();
                CreateJoint(true);

#if SERVER
                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                }
#endif
            }


            List<MapEntity> removedEntities = item.linkedTo.Where(e => e.Removed).ToList();
            foreach (MapEntity removed in removedEntities) item.linkedTo.Remove(removed);
            
            if (!item.linkedTo.Any(e => e is Hull) && !dockingTarget.item.linkedTo.Any(e => e is Hull))
            {
                CreateHulls();
            }
        }


        private void CreateJoint(bool useWeldJoint)
        {
            if (joint != null)
            {
                GameMain.World.RemoveJoint(joint);
                joint = null;
            }

            Vector2 offset = (IsHorizontal ?
                Vector2.UnitX * dockingDir :
                Vector2.UnitY * dockingDir);
            offset *= DockedDistance * 0.5f;
            
            Vector2 pos1 = item.WorldPosition + offset;

            Vector2 pos2 = dockingTarget.item.WorldPosition - offset;

            if (useWeldJoint)
            {
                joint = JointFactory.CreateWeldJoint(GameMain.World,
                    item.Submarine.PhysicsBody.FarseerBody, dockingTarget.item.Submarine.PhysicsBody.FarseerBody,
                    ConvertUnits.ToSimUnits(pos1), FarseerPhysics.ConvertUnits.ToSimUnits(pos2), true);

                ((WeldJoint)joint).FrequencyHz = 1.0f;
            }
            else
            {
                var distanceJoint = JointFactory.CreateDistanceJoint(GameMain.World,
                    item.Submarine.PhysicsBody.FarseerBody, dockingTarget.item.Submarine.PhysicsBody.FarseerBody,
                    ConvertUnits.ToSimUnits(pos1), FarseerPhysics.ConvertUnits.ToSimUnits(pos2), true);

                distanceJoint.Length = 0.01f;
                distanceJoint.Frequency = 1.0f;
                distanceJoint.DampingRatio = 0.8f;

                joint = distanceJoint;
            }


            joint.CollideConnected = true;
        }

        private void ConnectWireBetweenPorts()
        {
            Wire wire = item.GetComponent<Wire>();
            if (wire == null) return;

            wire.Hidden = true;
            wire.Locked = true;

            if (Item.Connections == null) return;

            var powerConnection = Item.Connections.Find(c => c.IsPower);
            if (powerConnection == null) return;

            if (dockingTarget == null || dockingTarget.item.Connections == null) return;
            var recipient = dockingTarget.item.Connections.Find(c => c.IsPower);
            if (recipient == null) return;

            wire.RemoveConnection(item);
            wire.RemoveConnection(dockingTarget.item);


            powerConnection.TryAddLink(wire);
            wire.Connect(powerConnection, false, false);
            recipient.TryAddLink(wire);
            wire.Connect(recipient, false, false);
        }

        private void CreateDoorBody()
        {
            if (doorBody != null)
            {
                GameMain.World.RemoveBody(doorBody);
                doorBody = null;
            }

            Vector2 position = ConvertUnits.ToSimUnits(item.Position + (dockingTarget.door.Item.WorldPosition - item.WorldPosition));
            if (!MathUtils.IsValid(position))
            {
                string errorMsg =
                    "Attempted to create a door body at an invalid position (item pos: " + item.Position
                    + ", item world pos: " + item.WorldPosition
                    + ", docking target world pos: " + DockingTarget.door.Item.WorldPosition + ")\n" + Environment.StackTrace;

                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "DockingPort.CreateDoorBody:InvalidPosition",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMsg);
                position = Vector2.Zero;
            }

            System.Diagnostics.Debug.Assert(doorBody == null);

            doorBody = BodyFactory.CreateRectangle(GameMain.World,
                dockingTarget.door.Body.width,
                dockingTarget.door.Body.height,
                1.0f,
                position,
                dockingTarget.door);

            doorBody.CollisionCategories = Physics.CollisionWall;
            doorBody.BodyType = BodyType.Static;
        }

        private void CreateHulls()
        {
            var hullRects = new Rectangle[] { item.WorldRect, dockingTarget.item.WorldRect };
            var subs = new Submarine[] { item.Submarine, dockingTarget.item.Submarine };

            bodies = new Body[4];

            if (dockingTarget.door != null)
            {
                CreateDoorBody();
            }

            if (door != null)
            {
                dockingTarget.CreateDoorBody();
            }
            
            if (IsHorizontal)
            {
                if (hullRects[0].Center.X > hullRects[1].Center.X)
                {
                    hullRects = new Rectangle[] { dockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { dockingTarget.item.Submarine,item.Submarine };
                }

                hullRects[0] = new Rectangle(hullRects[0].Center.X, hullRects[0].Y, ((int)DockedDistance / 2), hullRects[0].Height);
                hullRects[1] = new Rectangle(hullRects[1].Center.X - ((int)DockedDistance / 2), hullRects[1].Y, ((int)DockedDistance / 2), hullRects[1].Height);


                //expand hulls if needed, so there's no empty space between the sub's hulls and docking port hulls
                int leftSubRightSide = int.MinValue, rightSubLeftSide = int.MaxValue;
                foreach (Hull hull in Hull.hullList)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (hull.Submarine != subs[i]) continue;
                        if (hull.WorldRect.Y < hullRects[i].Y - hullRects[i].Height) continue;
                        if (hull.WorldRect.Y - hull.WorldRect.Height > hullRects[i].Y) continue;

                        if (i == 0) //left hull
                        {
                            leftSubRightSide = Math.Max(hull.WorldRect.Right, leftSubRightSide);
                        }
                        else //upper hull
                        {
                            rightSubLeftSide = Math.Min(hull.WorldRect.X, rightSubLeftSide);
                        }
                    }
                }


                //expand left hull to the rightmost hull of the sub at the left side
                //(unless the difference is more than 100 units - if the distance is very large 
                //there's something wrong with the positioning of the docking ports or submarine hulls)
                int leftHullDiff = hullRects[0].X - leftSubRightSide;
                if (leftHullDiff > 0)
                {
                    if (leftHullDiff > 100)
                    {
                        DebugConsole.ThrowError("Creating hulls between docking ports failed. The leftmost docking port seems to be very far from any hulls in the left-side submarine.");
                    }
                    else
                    {
                        hullRects[0].X -= leftHullDiff;
                        hullRects[0].Width += leftHullDiff;
                    }
                }

                int rightHullDiff = rightSubLeftSide - hullRects[1].Right;
                if (rightHullDiff > 0)
                {
                    if (rightHullDiff > 100)
                    {
                        DebugConsole.ThrowError("Creating hulls between docking ports failed. The rightmost docking port seems to be very far from any hulls in the right-side submarine.");
                    }
                    else
                    {
                        hullRects[1].Width += rightHullDiff;
                    }
                }


                for (int i = 0; i < 2; i++)
                {
                    hullRects[i].Location -= MathUtils.ToPoint((subs[i].WorldPosition - subs[i].HiddenSubPosition));
                    hulls[i] = new Hull(MapEntityPrefab.Find(null, "Hull"), hullRects[i], subs[i]);
                    hulls[i].AddToGrid(subs[i]);
                    hulls[i].FreeID();

                    for (int j = 0; j < 2; j++)
                    {
                        bodies[i + j * 2] = BodyFactory.CreateEdge(GameMain.World,
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X, hullRects[i].Y - hullRects[i].Height * j)),
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].Right, hullRects[i].Y - hullRects[i].Height * j)));
                    }
                }

                gap = new Gap(new Rectangle(hullRects[0].Right - 2, hullRects[0].Y, 4, hullRects[0].Height), true, subs[0]);
            }
            else
            {
                if (hullRects[0].Center.Y > hullRects[1].Center.Y)
                {
                    hullRects = new Rectangle[] { dockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { dockingTarget.item.Submarine, item.Submarine };
                }
                
                hullRects[0] = new Rectangle(hullRects[0].X, hullRects[0].Y + (int)(-hullRects[0].Height + DockedDistance) / 2, hullRects[0].Width, ((int)DockedDistance / 2));
                hullRects[1] = new Rectangle(hullRects[1].X, hullRects[1].Y - hullRects[1].Height / 2, hullRects[1].Width, ((int)DockedDistance / 2));

                //expand hulls if needed, so there's no empty space between the sub's hulls and docking port hulls
                int upperSubBottom = int.MaxValue, lowerSubTop = int.MinValue;
                foreach (Hull hull in Hull.hullList)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (hull.Submarine != subs[i]) continue;
                        if (hull.WorldRect.Right < hullRects[i].X) continue;
                        if (hull.WorldRect.X > hullRects[i].Right) continue;

                        if (i == 0) //lower hull
                        {
                            lowerSubTop = Math.Max(hull.WorldRect.Y, lowerSubTop);
                        }
                        else //upper hull
                        {
                            upperSubBottom = Math.Min(hull.WorldRect.Y - hull.WorldRect.Height, upperSubBottom);
                        }
                    }
                }

                //expand lower hull to the topmost hull of the lower sub 
                //(unless the difference is more than 100 units - if the distance is very large 
                //there's something wrong with the positioning of the docking ports or submarine hulls)
                int lowerHullDiff = (hullRects[0].Y - hullRects[0].Height) - lowerSubTop;
                if (lowerHullDiff > 0)
                {
                    if (lowerHullDiff > 100)
                    {
                        DebugConsole.ThrowError("Creating hulls between docking ports failed. The lower docking port seems to be very far from any hulls in the lower submarine.");
                    }
                    else
                    {
                        hullRects[0].Height += lowerHullDiff;
                    }
                }

                int upperHullDiff = upperSubBottom - hullRects[1].Y;
                if (upperHullDiff > 0)
                {
                    if (upperHullDiff > 100)
                    {
                        DebugConsole.ThrowError("Creating hulls between docking ports failed. The upper docking port seems to be very far from any hulls in the upper submarine.");
                    }
                    else
                    {
                        hullRects[1].Y += upperHullDiff;
                        hullRects[1].Height += upperHullDiff;
                    }
                }

                for (int i = 0; i < 2; i++)
                {
                    hullRects[i].Location -= MathUtils.ToPoint((subs[i].WorldPosition - subs[i].HiddenSubPosition));
                    hulls[i] = new Hull(MapEntityPrefab.Find(null, "hull"), hullRects[i], subs[i]);
                    hulls[i].AddToGrid(subs[i]);
                    hulls[i].FreeID();
                }

                gap = new Gap(new Rectangle(hullRects[0].X, hullRects[0].Y+2, hullRects[0].Width, 4), false, subs[0]);
            }

            LinkHullsToGaps();
            
            hulls[0].ShouldBeSaved = false;
            hulls[1].ShouldBeSaved = false;
            item.linkedTo.Add(hulls[0]);
            item.linkedTo.Add(hulls[1]);

            gap.FreeID();
            gap.DisableHullRechecks = true;
            gap.ShouldBeSaved = false;
            item.linkedTo.Add(gap);

            foreach (Body body in bodies)
            {
                if (body == null) continue;
                body.BodyType = BodyType.Static;
                body.Friction = 0.5f;

                body.CollisionCategories = Physics.CollisionWall;
            }
        }

        private void LinkHullsToGaps()
        {
            if (gap == null || hulls == null || hulls[0] == null || hulls[1] == null)
            {
#if DEBUG
                DebugConsole.ThrowError("Failed to link dockingport hulls to gap");
#endif
                return;
            }

            gap.linkedTo.Clear();

            if (IsHorizontal)
            {
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

            for (int i = 0; i < 2; i++)
            {
                Gap doorGap = i == 0 ? door?.LinkedGap : dockingTarget?.door?.LinkedGap;
                if (doorGap == null) continue;
                doorGap.DisableHullRechecks = true;
                if (doorGap.linkedTo.Count >= 2) continue;

                if (IsHorizontal)
                {
                    if (item.WorldPosition.X < dockingTarget.item.WorldPosition.X)
                    {
                        if (!doorGap.linkedTo.Contains(hulls[0])) doorGap.linkedTo.Add(hulls[0]);
                    }
                    else
                    {
                        if (!doorGap.linkedTo.Contains(hulls[1])) doorGap.linkedTo.Add(hulls[1]);
                    }
                }
                else
                {
                    if (item.WorldPosition.Y < dockingTarget.item.WorldPosition.Y)
                    {
                        if (!doorGap.linkedTo.Contains(hulls[0])) doorGap.linkedTo.Add(hulls[0]);
                    }
                    else
                    {
                        if (!doorGap.linkedTo.Contains(hulls[1])) doorGap.linkedTo.Add(hulls[1]);
                    }
                }                
            }
        }

        public void Undock()
        {
            if (dockingTarget == null || !docked) return;

#if CLIENT
            PlaySound(ActionType.OnUse, item.WorldPosition);
#endif

            dockingTarget.item.Submarine.DockedTo.Remove(item.Submarine);
            item.Submarine.DockedTo.Remove(dockingTarget.item.Submarine);

            if (door != null && dockingTarget.door != null)
            {
                WayPoint myWayPoint = WayPoint.WayPointList.Find(wp => door.LinkedGap == wp.ConnectedGap);
                WayPoint targetWayPoint = WayPoint.WayPointList.Find(wp => dockingTarget.door.LinkedGap == wp.ConnectedGap);

                if (myWayPoint != null && targetWayPoint != null)
                {
                    myWayPoint.linkedTo.Remove(targetWayPoint);
                    targetWayPoint.linkedTo.Remove(myWayPoint);
                }
            }
            
            item.linkedTo.Clear();

            docked = false;

            dockingTarget.Undock();
            dockingTarget = null;

            if (doorBody != null)
            {
                GameMain.World.RemoveBody(doorBody);
                doorBody = null;
            }

            var wire = item.GetComponent<Wire>();
            if (wire != null)
            {
                wire.Drop(null);
            }

            if (joint != null)
            {
                GameMain.World.RemoveJoint(joint);
                joint = null;
            }
            
            hulls[0]?.Remove(); hulls[0] = null;
            hulls[1]?.Remove(); hulls[1] = null;

            if (gap != null)
            {
                gap.Remove();
                gap = null;
            }
            
            if (bodies != null)
            {
                foreach (Body body in bodies)
                {
                    if (body == null) continue;
                    GameMain.World.RemoveBody(body);
                }
                bodies = null;
            }

#if SERVER
            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (dockingTarget == null)
            {
                dockingState = MathHelper.Lerp(dockingState, 0.0f, deltaTime * 10.0f);
                if (dockingState < 0.01f) docked = false;

                item.SendSignal(0, "0", "state_out", null);
                item.SendSignal(0, (FindAdjacentPort() != null) ? "1" : "0", "proximity_sensor", null);

            }
            else
            {
                if (!docked)
                {
                    Dock(dockingTarget);
                    if (dockingTarget == null) return;
                }

                if (joint is DistanceJoint)
                {
                    item.SendSignal(0, "0", "state_out", null);
                    dockingState = MathHelper.Lerp(dockingState, 0.5f, deltaTime * 10.0f);

                    if (Vector2.Distance(joint.WorldAnchorA, joint.WorldAnchorB) < 0.05f)
                    {
                        Lock(false);
                    }
                }
                else
                {
                    if (dockingTarget.door != null && doorBody != null)
                    {
                        doorBody.Enabled = dockingTarget.door.Body.Enabled;
                    }

                    item.SendSignal(0, "1", "state_out", null);

                    dockingState = MathHelper.Lerp(dockingState, 1.0f, deltaTime * 10.0f);
                }
            }
        }

        protected override void RemoveComponentSpecific()
        {
            list.Remove(this);
            hulls[0]?.Remove(); hulls[0] = null;
            hulls[1]?.Remove(); hulls[1] = null;
            gap?.Remove(); gap = null;
        }

        public override void OnMapLoaded()
        {
            foreach (Item it in Item.ItemList)
            {
                if (it.Submarine != item.Submarine) continue;

                var doorComponent = it.GetComponent<Door>();
                if (doorComponent == null) continue;

                if (Vector2.Distance(item.Position, doorComponent.Item.Position) < Submarine.GridSize.X)
                {
                    this.door = doorComponent;
                    break;
                }
            }

            if (!item.linkedTo.Any()) return;

            List<MapEntity> linked = new List<MapEntity>(item.linkedTo);
            foreach (MapEntity entity in linked)
            {
                var hull = entity as Hull;
                if (hull != null)
                {
                    hull.Remove();
                    item.linkedTo.Remove(hull);
                    continue;
                }

                var gap = entity as Gap;
                if (gap != null)
                {
                    gap.Remove();
                    continue;
                }

                Item linkedItem = entity as Item;
                if (linkedItem == null) continue;

                var dockingPort = linkedItem.GetComponent<DockingPort>();
                if (dockingPort != null)
                {
                    Dock(dockingPort);
                }
            }
        }

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
#if CLIENT
            if (GameMain.Client != null) return;
#endif

            bool wasDocked = docked;
            DockingPort prevDockingTarget = dockingTarget;

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

#if SERVER
            if (sender != null && docked != wasDocked)
            {
                if (docked)
                {
                    if (item.Submarine != null && dockingTarget?.item?.Submarine != null)
                        GameServer.Log(sender.LogName + " docked " + item.Submarine.Name + " to " + dockingTarget.item.Submarine.Name, ServerLog.MessageType.ItemInteraction);
                }
                else
                {
                    if (item.Submarine != null && prevDockingTarget?.item?.Submarine != null)
                        GameServer.Log(sender.LogName + " undocked " + item.Submarine.Name + " from " + prevDockingTarget.item.Submarine.Name, ServerLog.MessageType.ItemInteraction);
                }
            }
#endif
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(docked);

            if (docked)
            {
                msg.Write(dockingTarget.item.ID);                
                msg.Write(hulls != null && hulls[0] != null && hulls[1] != null && gap != null);
            }
        }

        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            bool isDocked = msg.ReadBoolean();

            for (int i = 0; i < 2; i++)
            {
                if (hulls[i] == null) continue;
                item.linkedTo.Remove(hulls[i]);
                hulls[i].Remove();
                hulls[i] = null;
            }

            if (gap != null)
            {
                item.linkedTo.Remove(gap);
                gap.Remove();
                gap = null;
            }

            if (isDocked)
            {
                ushort dockingTargetID = msg.ReadUInt16();

                bool isLocked = msg.ReadBoolean();
                
                Entity targetEntity = Entity.FindEntityByID(dockingTargetID);
                if (targetEntity == null || !(targetEntity is Item))
                {
                    DebugConsole.ThrowError("Invalid docking port network event (can't dock to " + targetEntity.ToString() + ")");
                    return;
                }

                dockingTarget = (targetEntity as Item).GetComponent<DockingPort>();
                if (dockingTarget == null)
                {
                    DebugConsole.ThrowError("Invalid docking port network event (" + targetEntity + " doesn't have a docking port component)");
                    return;
                }

                Dock(dockingTarget);

                if (isLocked)
                {
                    Lock(true);
                }
            }
            else
            {
                Undock();
            }
        }

    }
}
