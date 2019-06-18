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
        private float dockingState;
        private Joint joint;

        private readonly Hull[] hulls = new Hull[2];
        private Gap gap;

        private Door door;

        private Body[] bodies;
        private Body doorBody;

        private bool docked;
        private bool obstructedWayPointsDisabled;

        private float forceLockTimer;
        //if the submarine isn't in the correct position to lock within this time after docking has been activated,
        //force the sub to the correct position
        const float ForceLockDelay = 1.0f;

        public int DockingDir { get; private set; }

        [Serialize("32.0,32.0", false)]
        public Vector2 DistanceTolerance { get; set; }

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

        public DockingPort DockingTarget { get; private set; }

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
                    if (DockingTarget == null) AttemptDock();
                    if (DockingTarget == null) return;

                    docked = true;
                }
                else if (docked && !value)
                {
                    Undock();
                }
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
            if (DockingTarget != null)
            {
                if (joint != null)
                {
                    CreateJoint(joint is WeldJoint);
                    LinkHullsToGaps();
                }
                else if (DockingTarget.joint != null)
                {
                    if (!GameMain.World.BodyList.Contains(DockingTarget.joint.BodyA) ||
                        !GameMain.World.BodyList.Contains(DockingTarget.joint.BodyB))
                    {
                        DockingTarget.CreateJoint(DockingTarget.joint is WeldJoint);
                    }
                    DockingTarget.LinkHullsToGaps();
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

                if (Math.Abs(port.item.WorldPosition.X - item.WorldPosition.X) > DistanceTolerance.X) continue;
                if (Math.Abs(port.item.WorldPosition.Y - item.WorldPosition.Y) > DistanceTolerance.Y) continue;

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

            forceLockTimer = 0.0f;

            if (DockingTarget != null)
            {
                Undock();
            }

            if (target.item.Submarine == item.Submarine)
            {
                DebugConsole.ThrowError("Error - tried to dock a submarine to itself");
                DockingTarget = null;
                return;
            }
            
            target.InitializeLinks();

            if (!item.linkedTo.Contains(target.item)) item.linkedTo.Add(target.item);
            if (!target.item.linkedTo.Contains(item)) target.item.linkedTo.Add(item);

            if (!target.item.Submarine.DockedTo.Contains(item.Submarine)) target.item.Submarine.DockedTo.Add(item.Submarine);
            if (!item.Submarine.DockedTo.Contains(target.item.Submarine)) item.Submarine.DockedTo.Add(target.item.Submarine);

            DockingTarget = target;
            DockingTarget.DockingTarget = this;

            docked = true;
            DockingTarget.Docked = true;

            if (Character.Controlled != null &&
                (Character.Controlled.Submarine == DockingTarget.item.Submarine || Character.Controlled.Submarine == item.Submarine))
            {
                GameMain.GameScreen.Cam.Shake = Vector2.Distance(DockingTarget.item.Submarine.Velocity, item.Submarine.Velocity);
            }

            DockingDir = IsHorizontal ? 
                Math.Sign(DockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                Math.Sign(DockingTarget.item.WorldPosition.Y - item.WorldPosition.Y);
            DockingTarget.DockingDir = -DockingDir;

            if (door != null && DockingTarget.door != null)
            {
                WayPoint myWayPoint = WayPoint.WayPointList.Find(wp => door.LinkedGap == wp.ConnectedGap);
                WayPoint targetWayPoint = WayPoint.WayPointList.Find(wp => DockingTarget.door.LinkedGap == wp.ConnectedGap);

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

        public void Lock(bool isNetworkMessage, bool forcePosition = false)
        {
#if CLIENT
            if (GameMain.Client != null && !isNetworkMessage) return;
#endif

            if (DockingTarget == null)
            {
                DebugConsole.ThrowError("Error - attempted to lock a docking port that's not connected to anything");
                return;
            }

            if (!(joint is WeldJoint))
            {
                DockingDir = IsHorizontal ?
                    Math.Sign(DockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                    Math.Sign(DockingTarget.item.WorldPosition.Y - item.WorldPosition.Y);
                DockingTarget.DockingDir = -DockingDir;

                ApplyStatusEffects(ActionType.OnUse, 1.0f);

                Vector2 jointDiff = joint.WorldAnchorB - joint.WorldAnchorA;
                if (item.Submarine.PhysicsBody.Mass < DockingTarget.item.Submarine.PhysicsBody.Mass ||
                    DockingTarget.item.Submarine.IsOutpost)
                {
                    item.Submarine.SubBody.SetPosition(item.Submarine.SubBody.Position + ConvertUnits.ToDisplayUnits(jointDiff));
                }
                else if (DockingTarget.item.Submarine.PhysicsBody.Mass < item.Submarine.PhysicsBody.Mass ||
                   item.Submarine.IsOutpost)
                {
                    DockingTarget.item.Submarine.SubBody.SetPosition(item.Submarine.SubBody.Position - ConvertUnits.ToDisplayUnits(jointDiff));
                }

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
            
            if (!item.linkedTo.Any(e => e is Hull) && !DockingTarget.item.linkedTo.Any(e => e is Hull))
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
                Vector2.UnitX * DockingDir :
                Vector2.UnitY * DockingDir);
            offset *= DockedDistance * 0.5f;
            
            Vector2 pos1 = item.WorldPosition + offset;

            Vector2 pos2 = DockingTarget.item.WorldPosition - offset;

            if (useWeldJoint)
            {
                joint = JointFactory.CreateWeldJoint(GameMain.World,
                    item.Submarine.PhysicsBody.FarseerBody, DockingTarget.item.Submarine.PhysicsBody.FarseerBody,
                    ConvertUnits.ToSimUnits(pos1), FarseerPhysics.ConvertUnits.ToSimUnits(pos2), true);

                ((WeldJoint)joint).FrequencyHz = 1.0f;
            }
            else
            {
                var distanceJoint = JointFactory.CreateDistanceJoint(GameMain.World,
                    item.Submarine.PhysicsBody.FarseerBody, DockingTarget.item.Submarine.PhysicsBody.FarseerBody,
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

            if (DockingTarget == null || DockingTarget.item.Connections == null) return;
            var recipient = DockingTarget.item.Connections.Find(c => c.IsPower);
            if (recipient == null) return;

            wire.RemoveConnection(item);
            wire.RemoveConnection(DockingTarget.item);


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

            Vector2 position = ConvertUnits.ToSimUnits(item.Position + (DockingTarget.door.Item.WorldPosition - item.WorldPosition));
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
                DockingTarget.door.Body.width,
                DockingTarget.door.Body.height,
                1.0f,
                position,
                DockingTarget.door);

            doorBody.CollisionCategories = Physics.CollisionWall;
            doorBody.BodyType = BodyType.Static;
        }

        private void CreateHulls()
        {
            var hullRects = new Rectangle[] { item.WorldRect, DockingTarget.item.WorldRect };
            var subs = new Submarine[] { item.Submarine, DockingTarget.item.Submarine };

            bodies = new Body[4];

            if (DockingTarget.door != null)
            {
                CreateDoorBody();
            }

            if (door != null)
            {
                DockingTarget.CreateDoorBody();
            }
            
            if (IsHorizontal)
            {
                if (hullRects[0].Center.X > hullRects[1].Center.X)
                {
                    hullRects = new Rectangle[] { DockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { DockingTarget.item.Submarine,item.Submarine };
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
                int leftHullDiff = (hullRects[0].X - leftSubRightSide) + 5;
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

                int rightHullDiff = (rightSubLeftSide - hullRects[1].Right) + 5;
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
                    hulls[i] = new Hull(MapEntityPrefab.Find(null, "hull"), hullRects[i], subs[i]);
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
                    hullRects = new Rectangle[] { DockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { DockingTarget.item.Submarine, item.Submarine };
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
                int lowerHullDiff = ((hullRects[0].Y - hullRects[0].Height) - lowerSubTop) + 5;
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

                int upperHullDiff = (upperSubBottom - hullRects[1].Y) + 5;
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

                //difference between the edges of the hulls (to avoid a gap between the hulls)
                //0 is lower
                int midHullDiff = ((hullRects[1].Y - hullRects[1].Height) - hullRects[0].Y) + 2;
                if (midHullDiff > 100)
                {
                    DebugConsole.ThrowError("Creating hulls between docking ports failed. The upper hull seems to be very far from the lower hull.");
                }
                else if (midHullDiff > 0)
                {
                    hullRects[0].Height += midHullDiff / 2 + 1;
                    hullRects[1].Y -= midHullDiff / 2 + 1;
                    hullRects[1].Height += midHullDiff / 2 + 1;
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
                Gap doorGap = i == 0 ? door?.LinkedGap : DockingTarget?.door?.LinkedGap;
                if (doorGap == null) continue;
                doorGap.DisableHullRechecks = true;
                if (doorGap.linkedTo.Count >= 2) continue;

                if (IsHorizontal)
                {
                    if (item.WorldPosition.X < DockingTarget.item.WorldPosition.X)
                    {
                        if (!doorGap.linkedTo.Contains(hulls[0])) doorGap.linkedTo.Add(hulls[0]);
                    }
                    else
                    {
                        if (!doorGap.linkedTo.Contains(hulls[1])) doorGap.linkedTo.Add(hulls[1]);
                    }
                    //make sure the left hull is linked to the gap first (gap logic assumes that the first hull is the one to the left)
                    if (doorGap.linkedTo.Count > 1 && doorGap.linkedTo[0].Rect.X > doorGap.linkedTo[1].Rect.X)
                    {
                        var temp = doorGap.linkedTo[0];
                        doorGap.linkedTo[0] = doorGap.linkedTo[1];
                        doorGap.linkedTo[1] = temp;
                    }
                }
                else
                {
                    if (item.WorldPosition.Y < DockingTarget.item.WorldPosition.Y)
                    {
                        if (!doorGap.linkedTo.Contains(hulls[0])) doorGap.linkedTo.Add(hulls[0]);
                    }
                    else
                    {
                        if (!doorGap.linkedTo.Contains(hulls[1])) doorGap.linkedTo.Add(hulls[1]);
                    }
                    //make sure the upper hull is linked to the gap first (gap logic assumes that the first hull is above the second one)
                    if (doorGap.linkedTo.Count > 1 && doorGap.linkedTo[0].Rect.Y < doorGap.linkedTo[1].Rect.Y)
                    {
                        var temp = doorGap.linkedTo[0];
                        doorGap.linkedTo[0] = doorGap.linkedTo[1];
                        doorGap.linkedTo[1] = temp;
                    }
                }                
            }
        }

        public void Undock()
        {
            if (DockingTarget == null || !docked) return;
            
            forceLockTimer = 0.0f;

            ApplyStatusEffects(ActionType.OnSecondaryUse, 1.0f);

            DockingTarget.item.Submarine.DockedTo.Remove(item.Submarine);
            item.Submarine.DockedTo.Remove(DockingTarget.item.Submarine);

            if (door != null && DockingTarget.door != null)
            {
                WayPoint myWayPoint = WayPoint.WayPointList.Find(wp => door.LinkedGap == wp.ConnectedGap);
                WayPoint targetWayPoint = WayPoint.WayPointList.Find(wp => DockingTarget.door.LinkedGap == wp.ConnectedGap);

                if (myWayPoint != null && targetWayPoint != null)
                {
                    myWayPoint.linkedTo.Remove(targetWayPoint);
                    targetWayPoint.linkedTo.Remove(myWayPoint);
                }
            }
            
            item.linkedTo.Clear();

            docked = false;

            DockingTarget.Undock();
            DockingTarget = null;

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

            Item.Submarine.EnableObstructedWaypoints();
            obstructedWayPointsDisabled = false;

#if SERVER
            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
#endif
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (DockingTarget == null)
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
                    Dock(DockingTarget);
                    if (DockingTarget == null) { return; }
                }

                if (joint is DistanceJoint)
                {
                    item.SendSignal(0, "0", "state_out", null);
                    dockingState = MathHelper.Lerp(dockingState, 0.5f, deltaTime * 10.0f);

                    forceLockTimer += deltaTime;

                    Vector2 jointDiff = joint.WorldAnchorB - joint.WorldAnchorA;

                    if (jointDiff.LengthSquared() > 0.04f * 0.04f && forceLockTimer < ForceLockDelay)
                    {
                        float totalMass = item.Submarine.PhysicsBody.Mass + DockingTarget.item.Submarine.PhysicsBody.Mass;
                        float massRatio1 = 1.0f;
                        float massRatio2 = 1.0f;

                        if (item.Submarine.PhysicsBody.BodyType != BodyType.Dynamic)
                        {
                            massRatio1 = 0.0f;
                            massRatio2 = 1.0f;
                        }
                        else if (DockingTarget.item.Submarine.PhysicsBody.BodyType != BodyType.Dynamic)
                        {
                            massRatio1 = 1.0f;
                            massRatio2 = 0.0f;
                        }
                        else
                        {
                            massRatio1 = DockingTarget.item.Submarine.PhysicsBody.Mass / totalMass;
                            massRatio2 = item.Submarine.PhysicsBody.Mass / totalMass;
                        }

                        Vector2 relativeVelocity = DockingTarget.item.Submarine.Velocity - item.Submarine.Velocity;
                        Vector2 desiredRelativeVelocity = Vector2.Normalize(jointDiff);

                        item.Submarine.Velocity += (relativeVelocity + desiredRelativeVelocity) * massRatio1;
                        DockingTarget.item.Submarine.Velocity += (-relativeVelocity - desiredRelativeVelocity) * massRatio2;
                    }
                    else
                    {
                        Lock(isNetworkMessage: false, forcePosition: true);
                    }
                }
                else
                {
                    if (DockingTarget.door != null && doorBody != null)
                    {
                        doorBody.Enabled = DockingTarget.door.Body.Enabled;
                    }

                    item.SendSignal(0, "1", "state_out", null);

                    dockingState = MathHelper.Lerp(dockingState, 1.0f, deltaTime * 10.0f);
                }
            }
            if (!obstructedWayPointsDisabled && dockingState >= 0.99f)
            {
                Item.Submarine.DisableObstructedWayPoints(DockingTarget?.Item.Submarine);
                obstructedWayPointsDisabled = true;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            list.Remove(this);
            hulls[0]?.Remove(); hulls[0] = null;
            hulls[1]?.Remove(); hulls[1] = null;
            gap?.Remove(); gap = null;
        }

        private bool initialized = false;
        private void InitializeLinks()
        {
            if (initialized) { return; }
            initialized = true;

            float closestDist = 30.0f * 30.0f;
            foreach (Item it in Item.ItemList)
            {
                if (it.Submarine != item.Submarine) continue;

                var doorComponent = it.GetComponent<Door>();
                if (doorComponent == null) continue;

                float distSqr = Vector2.Distance(item.Position, it.Position);
                if (distSqr < closestDist)
                {
                    door = doorComponent;
                    closestDist = distSqr;
                }
            }

            if (!item.linkedTo.Any()) return;

            List<MapEntity> linked = new List<MapEntity>(item.linkedTo);
            foreach (MapEntity entity in linked)
            {
                if (entity is Hull hull)
                {
                    hull.Remove();
                    item.linkedTo.Remove(hull);
                    continue;
                }

                if (entity is Gap gap)
                {
                    gap.Remove();
                    continue;
                }
            }
        }

        public override void OnMapLoaded()
        {
            InitializeLinks();

            if (!item.linkedTo.Any()) return;

            List<MapEntity> linked = new List<MapEntity>(item.linkedTo);
            foreach (MapEntity entity in linked)
            {
                if (!(entity is Item linkedItem)) { continue; }

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
            DockingPort prevDockingTarget = DockingTarget;

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
                    if (item.Submarine != null && DockingTarget?.item?.Submarine != null)
                        GameServer.Log(sender.LogName + " docked " + item.Submarine.Name + " to " + DockingTarget.item.Submarine.Name, ServerLog.MessageType.ItemInteraction);
                }
                else
                {
                    if (item.Submarine != null && prevDockingTarget?.item?.Submarine != null)
                        GameServer.Log(sender.LogName + " undocked " + item.Submarine.Name + " from " + prevDockingTarget.item.Submarine.Name, ServerLog.MessageType.ItemInteraction);
                }
            }
#endif
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(docked);

            if (docked)
            {
                msg.Write(DockingTarget.item.ID);                
                msg.Write(hulls != null && hulls[0] != null && hulls[1] != null && gap != null);
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
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

                DockingTarget = (targetEntity as Item).GetComponent<DockingPort>();
                if (DockingTarget == null)
                {
                    DebugConsole.ThrowError("Invalid docking port network event (" + targetEntity + " doesn't have a docking port component)");
                    return;
                }

                Dock(DockingTarget);

                if (isLocked)
                {
                    Lock(isNetworkMessage: true, forcePosition: true);
                }
            }
            else
            {
                Undock();
            }
        }
    }
}
