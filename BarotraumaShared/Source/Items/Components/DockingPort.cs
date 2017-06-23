using Barotrauma.Networking;
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
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        public static List<DockingPort> list = new List<DockingPort>();
        
        private Sprite overlaySprite;
        
        private Vector2 distanceTolerance;

        private DockingPort dockingTarget;

        private float dockingState;
        private int dockingDir;

        private Joint joint;

        private Hull[] hulls;
        private ushort?[] hullIds;

        private Door door;

        private Body[] bodies;

        private Body doorBody;

        private Gap gap;
        private ushort? gapId;

        private bool docked;

        public int DockingDir
        {
            get { return dockingDir; }
            set { dockingDir = value; }
        }

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
                string texturePath = ToolBox.GetAttributeString(subElement, "texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        overlaySprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.ConfigFile));
                        break;
                }
            }
            
            IsActive = true;

            hullIds = new ushort?[2];

            list.Add(this);
        }

        public override void FlipX()
        {
            base.FlipX();

            if (dockingTarget != null)
            {
                if (joint != null)
                {
                    CreateJoint(joint is WeldJoint);
                    LinkHullsToGap();
                }
                else if (dockingTarget.joint != null)
                {
                    if (!GameMain.World.BodyList.Contains(dockingTarget.joint.BodyA) ||
                        !GameMain.World.BodyList.Contains(dockingTarget.joint.BodyB))
                    {
                        dockingTarget.CreateJoint(dockingTarget.joint is WeldJoint);
                    }
                    dockingTarget.LinkHullsToGap();
                }
            }
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
            

            foreach (WayPoint wp in WayPoint.WayPointList)
            {
                if (wp.Submarine != item.Submarine || wp.SpawnType != SpawnType.Path) continue;

                if (!Submarine.RectContains(item.Rect, wp.Position)) continue;

                foreach (WayPoint wp2 in WayPoint.WayPointList)
                {
                    if (wp2.Submarine != dockingTarget.item.Submarine || wp2.SpawnType != SpawnType.Path) continue;

                    if (!Submarine.RectContains(dockingTarget.item.Rect, wp2.Position)) continue;

                    wp.linkedTo.Add(wp2);
                    wp2.linkedTo.Add(wp);
                }
            }
            
            CreateJoint(false);

            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
        }

        public void Lock(bool isNetworkMessage)
        {
            if (GameMain.Client != null && !isNetworkMessage) return;

            if (dockingTarget==null)
            {
                DebugConsole.ThrowError("Error - attempted to lock a docking port that's not connected to anything");
                return;
            }
            else if (joint is WeldJoint)
            {
                //DebugConsole.ThrowError("Error - attempted to lock a docking port that's already locked");
                return;
            }

            dockingDir = IsHorizontal ?
                Math.Sign(dockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                Math.Sign(dockingTarget.item.WorldPosition.Y - item.WorldPosition.Y);
            dockingTarget.dockingDir = -dockingDir;

#if CLIENT
            PlaySound(ActionType.OnSecondaryUse, item.WorldPosition);
#endif

            ConnectWireBetweenPorts();

            CreateJoint(true);

            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }

            if (!item.linkedTo.Any(e => e is Hull) && !dockingTarget.item.linkedTo.Any(e => e is Hull))
            {
                CreateHull();
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
            doorBody = BodyFactory.CreateRectangle(GameMain.World,
                dockingTarget.door.Body.width,
                dockingTarget.door.Body.height,
                1.0f,
                dockingTarget.door);

            doorBody.CollisionCategories = Physics.CollisionWall;
            doorBody.BodyType = BodyType.Static;
            doorBody.SetTransform(
                ConvertUnits.ToSimUnits(item.Position + (dockingTarget.door.Item.WorldPosition - item.WorldPosition)),
                0.0f);
        }

        private void CreateHull()
        {
            var hullRects = new Rectangle[] { item.WorldRect, dockingTarget.item.WorldRect };
            var subs = new Submarine[] { item.Submarine, dockingTarget.item.Submarine };

            hulls = new Hull[2];
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

                for (int i = 0; i < 2; i++)
                {
                    hullRects[i].Location -= MathUtils.ToPoint((subs[i].WorldPosition - subs[i].HiddenSubPosition));
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
                if (gapId != null) gap.ID = (ushort)gapId;

                LinkHullsToGap();
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
                    hullRects[i].Location -= MathUtils.ToPoint((subs[i].WorldPosition - subs[i].HiddenSubPosition));
                    hulls[i] = new Hull(MapEntityPrefab.list.Find(m => m.Name == "Hull"), hullRects[i], subs[i]);
                    hulls[i].AddToGrid(subs[i]);

                    if (hullIds[i] != null) hulls[i].ID = (ushort)hullIds[i];
                }

                gap = new Gap(new Rectangle(hullRects[0].X, hullRects[0].Y+2, hullRects[0].Width, 4), false, subs[0]);
                if (gapId != null) gap.ID = (ushort)gapId;

                LinkHullsToGap();
            }

            item.linkedTo.Add(hulls[0]);
            item.linkedTo.Add(hulls[1]);

            hullIds[0] = hulls[0].ID;
            hullIds[1] = hulls[1].ID;

            gap.DisableHullRechecks = true;
            gapId = gap.ID;

            item.linkedTo.Add(gap);

            foreach (Body body in bodies)
            {
                if (body == null) continue;
                body.BodyType = BodyType.Static;
                body.Friction = 0.5f;

                body.CollisionCategories = Physics.CollisionWall;
            }
        }

        private void LinkHullsToGap()
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
        }

        public void Undock()
        {
            if (dockingTarget == null || !docked) return;

#if CLIENT
            PlaySound(ActionType.OnUse, item.WorldPosition);
#endif

            dockingTarget.item.Submarine.DockedTo.Remove(item.Submarine);
            item.Submarine.DockedTo.Remove(dockingTarget.item.Submarine);
            
            //remove all waypoint links between this sub and the dockingtarget
            foreach (WayPoint wp in WayPoint.WayPointList)
            {
                if (wp.Submarine != item.Submarine || wp.SpawnType != SpawnType.Path) continue;

                for (int i = wp.linkedTo.Count - 1; i >= 0; i--)
                {
                    var wp2 = wp.linkedTo[i] as WayPoint;
                    if (wp2 == null) continue;

                    if (wp.Submarine == dockingTarget.item.Submarine)
                    {
                        wp.linkedTo.RemoveAt(i);
                        wp2.linkedTo.Remove(wp);
                    }
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

            hullIds[0] = null;
            hullIds[1] = null;

            gapId = null;
            
            if (bodies!=null)
            {
                foreach (Body body in bodies)
                {
                    if (body == null) continue;
                    GameMain.World.RemoveBody(body);
                }
                bodies = null;
            }

            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
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

        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f)
        {
            if (GameMain.Client != null) return;

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

            if (sender != null && docked != wasDocked)
            {
                if (docked)
                {
                    if (item.Submarine != null && dockingTarget?.item?.Submarine != null)
                        GameServer.Log(sender.Name + " docked " + item.Submarine.Name + " to " + dockingTarget.item.Submarine.Name, ServerLog.MessageType.ItemInteraction);
                }
                else
                {
                    if (item.Submarine != null && prevDockingTarget?.item?.Submarine != null)
                        GameServer.Log(sender.Name + " undocked " + item.Submarine.Name + " from " + prevDockingTarget.item.Submarine.Name, ServerLog.MessageType.ItemInteraction);
                }
            }
        }

        public void ServerWrite(Lidgren.Network.NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(docked);

            if (docked)
            {
                msg.Write(dockingTarget.item.ID);

                if (hullIds[0] != null && hullIds[1] != null && gapId != null)
                {
                    msg.Write(true);
                    msg.Write((ushort)hullIds[0]);
                    msg.Write((ushort)hullIds[1]);
                    msg.Write((ushort)gapId);
                }
                else
                {
                    msg.Write(false);
                }
            }
        }

        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            bool isDocked = msg.ReadBoolean();

            if (isDocked)
            {
                ushort dockingTargetID = msg.ReadUInt16();

                bool isLocked = msg.ReadBoolean();

                if (isLocked)
                {
                    hullIds[0] = msg.ReadUInt16();
                    hullIds[1] = msg.ReadUInt16();
                    gapId = msg.ReadUInt16();
                }

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

                    hulls[0].ID = (ushort)hullIds[0];
                    hulls[1].ID = (ushort)hullIds[1];
                    gap.ID = (ushort)gapId;
                }
            }
            else
            {
                Undock();
            }
        }

    }
}
