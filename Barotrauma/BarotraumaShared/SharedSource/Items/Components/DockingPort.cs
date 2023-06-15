﻿using Barotrauma.IO;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
#if CLIENT
using Barotrauma.Lights;
#endif

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        public enum DirectionType
        {
            None,
            Top,
            Bottom,
            Left,
            Right
        }

        private static readonly List<DockingPort> list = new List<DockingPort>();
        public static IEnumerable<DockingPort> List
        {
            get { return list; }
        }

        private Sprite overlaySprite;
        private float dockingState;
        private Joint joint;

        private readonly Hull[] hulls = new Hull[2];
        private Gap gap;
        private Body[] bodies;
        private Fixture outsideBlocker;
        private Body doorBody;

        private float dockingCooldown;

        private bool docked;
        private bool obstructedWayPointsDisabled;

        private float forceLockTimer;
        //if the submarine isn't in the correct position to lock within this time after docking has been activated,
        //force the sub to the correct position
        const float ForceLockDelay = 1.0f;

        public int DockingDir { get; set; }

        [Serialize("32.0,32.0", IsPropertySaveable.No, description: "How close the docking port has to be to another port to dock.")]
        public Vector2 DistanceTolerance { get; set; }

        [Serialize(32.0f, IsPropertySaveable.No, description: "How close together the docking ports are forced when docked.")]
        public float DockedDistance
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.No, description: "Is the port horizontal.")]
        public bool IsHorizontal
        {
            get;
            set;
        }

        [Editable, Serialize(false, IsPropertySaveable.Yes, description: "If set to true, this docking port is used when spawning the submarine docked to an outpost (if possible).")]
        public bool MainDockingPort
        {
            get;
            set;
        }

        [Editable, Serialize(true, IsPropertySaveable.No, description: "Should the OnUse StatusEffects trigger when docking (on vanilla docking ports these effects emit particles and play a sound).)")]
        public bool ApplyEffectsOnDocking
        {
            get;
            set;
        }

        [Editable, Serialize(DirectionType.None, IsPropertySaveable.No, description: "Which direction the port is allowed to dock in. For example, \"Top\" would mean the port can dock to another port above it.\n" +
             "Normally there's no need to touch this setting, but if you notice the docking position is incorrect (for example due to some unusual docking port configuration without hulls or doors), you can use this to enforce the direction.")]
        public DirectionType ForceDockingDirection { get; set; }

        public DockingPort DockingTarget { get; private set; }

        /// <summary>
        /// Can be used by status effects
        /// </summary>
        public bool AtStartExit => Item.Submarine is { AtStartExit: true};
        public bool AtEndExit => Item.Submarine is { AtEndExit: true };

        public Door Door { get; private set; }

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
                    if (DockingTarget == null) { AttemptDock(); }
                    if (DockingTarget == null) { return; }

                    docked = true;
                }
                else if (docked && !value)
                {
                    Undock();
                }
            }
        }

        public bool IsLocked
        {
            get { return joint is WeldJoint || DockingTarget?.joint is WeldJoint; }
        }

        public bool AnotherPortInProximity => FindAdjacentPort() != null;

        /// <summary>
        /// Automatically cleared after docking -> no need to unregister
        /// </summary>
        public event Action OnDocked;

        /// <summary>
        /// Automatically cleared after undocking -> no need to unregister
        /// </summary>
        public event Action OnUnDocked;

        private bool outpostAutoDockingPromptShown;

        enum AllowOutpostAutoDocking
        {
            Ask, Yes, No
        }
        private AllowOutpostAutoDocking allowOutpostAutoDocking = AllowOutpostAutoDocking.Ask;

        public DockingPort(Item item, ContentXElement element)
            : base(item, element)
        {
            // isOpen = false;
            foreach (var subElement in element.Elements())
            {
                string texturePath = subElement.GetAttributeString("texture", "");
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        overlaySprite = new Sprite(subElement, texturePath.Contains("/") ? "" : Path.GetDirectoryName(item.Prefab.FilePath));
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
                if (IsHorizontal) 
                { 
                    DockingDir = 0; 
                    DockingDir = GetDir(DockingTarget);  
                    DockingTarget.DockingDir = -DockingDir;
                }

                //undock and redock to recreate the hulls, gaps and physics bodies
                var prevDockingTarget = DockingTarget;
                Undock(applyEffects: false);
                Dock(prevDockingTarget);
                Lock(isNetworkMessage: true, applyEffects: false);
            }
        }

        public override void FlipY(bool relativeToSub)
        {
            FlipX(relativeToSub);
        }

        private DockingPort FindAdjacentPort()
        {
            float closestDist = float.MaxValue;
            DockingPort closestPort = null;
            foreach (DockingPort port in list)
            {
                if (port == this || port.item.Submarine == item.Submarine || port.IsHorizontal != IsHorizontal) { continue; }
                float xDist = Math.Abs(port.item.WorldPosition.X - item.WorldPosition.X);
                if (xDist > DistanceTolerance.X) { continue; }
                float yDist = Math.Abs(port.item.WorldPosition.Y - item.WorldPosition.Y);
                if (yDist > DistanceTolerance.Y) { continue; }

                float dist = xDist + yDist;
                //disfavor non-interactable ports
                if (port.item.NonInteractable) { dist *= 2; }
                if (dist < closestDist)
                {
                    closestPort = port;
                    closestDist = dist;
                }
            }
            return closestPort;
        }

        private void AttemptDock()
        {
            var adjacentPort = FindAdjacentPort();
            if (adjacentPort != null) { Dock(adjacentPort); }
        }

        public void Dock(DockingPort target)
        {
            if (item.Submarine.DockedTo.Contains(target.item.Submarine)) { return; }

            forceLockTimer = 0.0f;
            dockingCooldown = 0.1f;

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

            if (!item.linkedTo.Contains(target.item)) { item.linkedTo.Add(target.item); }
            if (!target.item.linkedTo.Contains(item)) { target.item.linkedTo.Add(item); }

            if (!target.item.Submarine.DockedTo.Contains(item.Submarine))
            {
                target.item.Submarine.ConnectedDockingPorts.Add(item.Submarine, target);
                target.item.Submarine.RefreshConnectedSubs();
            }
            if (!item.Submarine.DockedTo.Contains(target.item.Submarine))
            {
                item.Submarine.ConnectedDockingPorts.Add(target.item.Submarine, this);
                item.Submarine.RefreshConnectedSubs();
            }

            DockingTarget = target;
            DockingTarget.DockingTarget = this;

            docked = true;
            DockingTarget.Docked = true;

            if (Character.Controlled != null &&
                (Character.Controlled.Submarine == DockingTarget.item.Submarine || Character.Controlled.Submarine == item.Submarine))
            {
                GameMain.GameScreen.Cam.Shake = Vector2.Distance(DockingTarget.item.Submarine.Velocity, item.Submarine.Velocity);
            }

            DockingDir = GetDir(DockingTarget);
            DockingTarget.DockingDir = -DockingDir;
           
            CreateJoint(false);

#if SERVER
            if (GameMain.Server != null && (!item.Submarine?.Loading ?? true))
            {
                item.CreateServerEvent(this);
            }
#endif

            OnDocked?.Invoke();
            OnDocked = null;
        }

        public void Lock(bool isNetworkMessage, bool applyEffects = true)
        {
#if CLIENT
            if (GameMain.Client != null && !isNetworkMessage) { return; }
#endif

            if (DockingTarget == null)
            {
                DebugConsole.ThrowError("Error - attempted to lock a docking port that's not connected to anything");
                return;
            }

            if (joint == null)
            {
                string errorMsg = "Error while locking a docking port (joint between submarines doesn't exist)." +
                    " Submarine: " + (item.Submarine?.Info.Name ?? "null") +
                    ", target submarine: " + (DockingTarget.item.Submarine?.Info.Name ?? "null");
                GameAnalyticsManager.AddErrorEventOnce("DockingPort.Lock:JointNotCreated", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }

            if (joint is not WeldJoint)
            {
                DockingDir = GetDir(DockingTarget);
                DockingTarget.DockingDir = -DockingDir;

                if (applyEffects && ApplyEffectsOnDocking)
                {
                    ApplyStatusEffects(ActionType.OnUse, 1.0f);
                }

                Vector2 jointDiff = joint.WorldAnchorB - joint.WorldAnchorA;
                if (item.Submarine.PhysicsBody.Mass < DockingTarget.item.Submarine.PhysicsBody.Mass ||
                    DockingTarget.item.Submarine.Info.IsOutpost)
                {
                    item.Submarine.SubBody.SetPosition(item.Submarine.SubBody.Position + ConvertUnits.ToDisplayUnits(jointDiff));
                }
                else if (DockingTarget.item.Submarine.PhysicsBody.Mass < item.Submarine.PhysicsBody.Mass ||
                   item.Submarine.Info.IsOutpost)
                {
                    DockingTarget.item.Submarine.SubBody.SetPosition(DockingTarget.item.Submarine.SubBody.Position - ConvertUnits.ToDisplayUnits(jointDiff));
                }

                ConnectWireBetweenPorts();
                CreateJoint(true);

#if SERVER
                if (GameMain.Server != null && (!item.Submarine?.Loading ?? true))
                {
                    item.CreateServerEvent(this);
                }
#else
                if (GameMain.Client != null && GameMain.Client.MidRoundSyncing && 
                    (item.Submarine == Submarine.MainSub || DockingTarget.item.Submarine == Submarine.MainSub))
                {
                    Screen.Selected.Cam.Position = Submarine.MainSub.WorldPosition;
                }
#endif
            }


            List<MapEntity> removedEntities = item.linkedTo.Where(e => e.Removed).ToList();
            foreach (MapEntity removed in removedEntities) { item.linkedTo.Remove(removed); }
            
            if (!item.linkedTo.Any(e => e is Hull) && !DockingTarget.item.linkedTo.Any(e => e is Hull))
            {
                CreateHulls();
            }

            if (Door != null && DockingTarget.Door != null)
            {
                WayPoint myWayPoint = WayPoint.WayPointList.Find(wp => Door.LinkedGap == wp.ConnectedGap);
                WayPoint targetWayPoint = WayPoint.WayPointList.Find(wp => DockingTarget.Door.LinkedGap == wp.ConnectedGap);

                if (myWayPoint != null && targetWayPoint != null)
                {
                    myWayPoint.FindHull();
                    targetWayPoint.FindHull();
                    myWayPoint.ConnectTo(targetWayPoint);
                }
            }
        }


        private void CreateJoint(bool useWeldJoint)
        {
            if (joint != null)
            {
                GameMain.World.Remove(joint);
                joint = null;
            }

            Vector2 offset = IsHorizontal ?
                Vector2.UnitX * DockingDir :
                Vector2.UnitY * DockingDir;
            offset *= DockedDistance * 0.5f * item.Scale;

            Vector2 pos1 = item.WorldPosition + offset;
            Vector2 pos2 = DockingTarget.item.WorldPosition - offset;

            if (useWeldJoint)
            {
                joint = JointFactory.CreateWeldJoint(GameMain.World,
                    item.Submarine.PhysicsBody.FarseerBody, DockingTarget.item.Submarine.PhysicsBody.FarseerBody,
                    ConvertUnits.ToSimUnits(pos1), ConvertUnits.ToSimUnits(pos2), true);

                ((WeldJoint)joint).FrequencyHz = 1.0f;
                joint.CollideConnected = false;
            }
            else
            {
                var distanceJoint = JointFactory.CreateDistanceJoint(GameMain.World,
                    item.Submarine.PhysicsBody.FarseerBody, DockingTarget.item.Submarine.PhysicsBody.FarseerBody,
                    ConvertUnits.ToSimUnits(pos1), ConvertUnits.ToSimUnits(pos2), true);

                distanceJoint.Length = 0.01f;
                distanceJoint.Frequency = 1.0f;
                distanceJoint.DampingRatio = 0.8f;

                joint = distanceJoint;
                joint.CollideConnected = true;
            }
        }

        public int GetDir(DockingPort dockingTarget = null)
        {
            int forcedDockingDir = GetForcedDockingDir();
            if (forcedDockingDir != 0) { return forcedDockingDir; }
            if (dockingTarget != null)
            {
                forcedDockingDir = -dockingTarget.GetForcedDockingDir();
                if (forcedDockingDir != 0) { return forcedDockingDir; }
            }

            if (DockingDir != 0) { return DockingDir; }

            if (Door != null && Door.LinkedGap.linkedTo.Count > 0)
            {
                Hull refHull = null;
                float largestHullSize = 0.0f;
                foreach (MapEntity linked in Door.LinkedGap.linkedTo)
                {
                    if (!(linked is Hull hull)) { continue; }
                    if (hull.Volume > largestHullSize)
                    {
                        refHull = hull;
                        largestHullSize = hull.Volume;
                    }
                }
                if (refHull != null)
                {
                    return IsHorizontal ?
                        Math.Sign(Door.Item.WorldPosition.X - refHull.WorldPosition.X) :
                        Math.Sign(Door.Item.WorldPosition.Y - refHull.WorldPosition.Y);
                }
            }
            if (dockingTarget?.Door?.LinkedGap != null && dockingTarget.Door.LinkedGap.linkedTo.Count > 0)
            {
                Hull refHull = null;
                float largestHullSize = 0.0f;
                foreach (MapEntity linked in dockingTarget.Door.LinkedGap.linkedTo)
                {
                    if (!(linked is Hull hull)) { continue; }
                    if (hull.Volume > largestHullSize)
                    {
                        refHull = hull;
                        largestHullSize = hull.Volume;
                    }
                }
                if (refHull != null)
                {
                    return IsHorizontal ?
                        Math.Sign(refHull.WorldPosition.X - dockingTarget.Door.Item.WorldPosition.X) :
                        Math.Sign(refHull.WorldPosition.Y - dockingTarget.Door.Item.WorldPosition.Y);
                }                
            }
            if (dockingTarget != null)
            {
                int dir = IsHorizontal ?
                    Math.Sign(dockingTarget.item.WorldPosition.X - item.WorldPosition.X) :
                    Math.Sign(dockingTarget.item.WorldPosition.Y - item.WorldPosition.Y);
                if (dir != 0) { return dir; }
            }
            if (item.Submarine != null)
            {
                return IsHorizontal ?
                    Math.Sign(item.WorldPosition.X - item.Submarine.WorldPosition.X) :
                    Math.Sign(item.WorldPosition.Y - item.Submarine.WorldPosition.Y);
            }

            return 0;
        }

        private int GetForcedDockingDir()
        {
            switch (ForceDockingDirection)
            {
                case DirectionType.Left:
                    return -1;
                case DirectionType.Right:
                    return 1;
                case DirectionType.Top:
                    return 1;
                case DirectionType.Bottom:
                    return -1;
            }
            return 0;
        }

        private void ConnectWireBetweenPorts()
        {
            Wire wire = item.GetComponent<Wire>();
            if (wire == null) { return; }

            wire.Locked = true;
            wire.Hidden = true;

            if (Item.Connections == null) { return; }

            var powerConnection = Item.Connections.Find(c => c.IsPower);
            if (powerConnection == null) { return; }

            if (DockingTarget == null || DockingTarget.item.Connections == null) { return; }
            var recipient = DockingTarget.item.Connections.Find(c => c.IsPower);
            if (recipient == null) { return; }

            wire.RemoveConnection(item);
            wire.RemoveConnection(DockingTarget.item);

            powerConnection.TryAddLink(wire);
            wire.TryConnect(powerConnection, addNode: false);
            recipient.TryAddLink(wire);
            wire.TryConnect(recipient, addNode: false);

            //Flag connections to be updated
            Powered.ChangedConnections.Add(powerConnection);
            Powered.ChangedConnections.Add(recipient);
        }

        private void CreateDoorBody()
        {
            if (doorBody != null)
            {
                GameMain.World.Remove(doorBody);
                doorBody = null;
            }

            Vector2 position = ConvertUnits.ToSimUnits(item.Position + (DockingTarget.Door.Item.WorldPosition - item.WorldPosition));
            if (!MathUtils.IsValid(position))
            {
                string errorMsg =
                    "Attempted to create a door body at an invalid position (item pos: " + item.Position
                    + ", item world pos: " + item.WorldPosition
                    + ", docking target world pos: " + DockingTarget.Door.Item.WorldPosition + ")\n" + Environment.StackTrace.CleanupStackTrace();

                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "DockingPort.CreateDoorBody:InvalidPosition",
                    GameAnalyticsManager.ErrorSeverity.Error,
                    errorMsg);
                position = Vector2.Zero;
            }

            System.Diagnostics.Debug.Assert(doorBody == null);

            doorBody = GameMain.World.CreateRectangle(
                DockingTarget.Door.Body.Width,
                DockingTarget.Door.Body.Height,
                1.0f,
                position);
            doorBody.UserData = DockingTarget.Door;
            doorBody.CollisionCategories = Physics.CollisionWall;
            doorBody.BodyType = BodyType.Static;
        }

        private void CreateHulls()
        {            
            var hullRects = new Rectangle[] { item.WorldRect, DockingTarget.item.WorldRect };
            var subs = new Submarine[] { item.Submarine, DockingTarget.item.Submarine };

            bodies = new Body[4];
            RemoveConvexHulls();

            if (DockingTarget.Door != null)
            {
                CreateDoorBody();
            }

            if (Door != null)
            {
                DockingTarget.CreateDoorBody();
            }

            if (IsHorizontal)
            {
                if (hullRects[0].Center.X > hullRects[1].Center.X)
                {
                    hullRects = new Rectangle[] { DockingTarget.item.WorldRect, item.WorldRect };
                    subs = new Submarine[] { DockingTarget.item.Submarine, item.Submarine };
                }

                int scaledDockedDistance = (int)(DockedDistance / 2 * item.Scale);
                hullRects[0] = new Rectangle(hullRects[0].Center.X, hullRects[0].Y, scaledDockedDistance, hullRects[0].Height);
                hullRects[1] = new Rectangle(hullRects[1].Center.X - scaledDockedDistance, hullRects[1].Y, scaledDockedDistance, hullRects[1].Height);

                //expand hulls if needed, so there's no empty space between the sub's hulls and docking port hulls
                int leftSubRightSide = int.MinValue, rightSubLeftSide = int.MaxValue;
                foreach (Hull hull in Hull.HullList)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (hull.Submarine != subs[i]) { continue; }
                        if (hull.WorldRect.Y - 5 < hullRects[i].Y - hullRects[i].Height) { continue; }
                        if (hull.WorldRect.Y - hull.WorldRect.Height + 5 > hullRects[i].Y) { continue; }

                        if (i == 0) //left hull
                        {
                            if (hull.WorldPosition.X > hullRects[0].Center.X) { continue; }
                            leftSubRightSide = Math.Max(hull.WorldRect.Right, leftSubRightSide);
                        }
                        else //upper hull
                        {
                            if (hull.WorldPosition.X < hullRects[1].Center.X) { continue; }
                            rightSubLeftSide = Math.Min(hull.WorldRect.X, rightSubLeftSide);
                        }
                    }
                }

                if (leftSubRightSide == int.MinValue || rightSubLeftSide == int.MaxValue)
                {
                    DebugConsole.NewMessage("Creating hulls between docking ports failed. Could not find a hull next to the docking port.");
                    return;
                }

                //expand left hull to the rightmost hull of the sub at the left side
                //(unless the difference is more than 100 units - if the distance is very large 
                //there's something wrong with the positioning of the docking ports or submarine hulls)
                int leftHullDiff = (hullRects[0].X - leftSubRightSide) + 5;
                if (leftHullDiff > 0)
                {
                    if (leftHullDiff > 100)
                    {
                        DebugConsole.NewMessage("Creating hulls between docking ports failed. The leftmost docking port seems to be very far from any hulls in the left-side submarine.");
                        return;
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
                        DebugConsole.NewMessage("Creating hulls between docking ports failed. The rightmost docking port seems to be very far from any hulls in the right-side submarine.");
                        return;
                    }
                    else
                    {
                        hullRects[1].Width += rightHullDiff;
                    }
                }

                int expand = 5;
                for (int i = 0; i < 2; i++)
                {
                    hullRects[i].X -= expand;
                    hullRects[i].Width += expand * 2;
                    hullRects[i].Location -= MathUtils.ToPoint(subs[i].WorldPosition - subs[i].HiddenSubPosition);
                    hulls[i] = new Hull(hullRects[i], subs[i])
                    {
                        RoomName = IsHorizontal ? "entityname.dockingport" : "entityname.dockinghatch"
                    };
                    hulls[i].AddToGrid(subs[i]);
                    hulls[i].FreeID();

                    for (int j = 0; j < 2; j++)
                    {
                        bodies[i + j * 2] = GameMain.World.CreateEdge(
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X, hullRects[i].Y - hullRects[i].Height * j)),
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].Right, hullRects[i].Y - hullRects[i].Height * j)),
                            BodyType.Static);
                    }
                }
#if CLIENT
                for (int i = 0; i < 2; i++)
                {
                    convexHulls[i] =
                        new ConvexHull(new Rectangle(
                            new Point((int)item.Position.X, item.Rect.Y - item.Rect.Height * i),
                            new Point((int)(DockingTarget.item.WorldPosition.X - item.WorldPosition.X), 0)), IsHorizontal, item);                    
                }
#endif

                if (rightHullDiff <= 100 && hulls[0].Submarine != null)
                {
                    outsideBlocker = hulls[0].Submarine.PhysicsBody.FarseerBody.CreateRectangle(
                        ConvertUnits.ToSimUnits(hullRects[0].Width + hullRects[1].Width),
                        ConvertUnits.ToSimUnits(hullRects[0].Height),
                        density: 0.0f,
                        offset: ConvertUnits.ToSimUnits(new Vector2(hullRects[0].Right, hullRects[0].Y - hullRects[0].Height / 2) - hulls[0].Submarine.HiddenSubPosition),
                        Physics.CollisionWall,
                        Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionCharacter | Physics.CollisionItemBlocking | Physics.CollisionProjectile);
                    outsideBlocker.UserData = this;
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

                int scaledDockedDistance = (int)(DockedDistance / 2 * item.Scale);
                hullRects[0] = new Rectangle(hullRects[0].X, hullRects[0].Y - hullRects[0].Height / 2 + scaledDockedDistance, hullRects[0].Width, scaledDockedDistance);
                hullRects[1] = new Rectangle(hullRects[1].X, hullRects[1].Y - hullRects[1].Height / 2, hullRects[1].Width, scaledDockedDistance);

                //expand hulls if needed, so there's no empty space between the sub's hulls and docking port hulls
                int upperSubBottom = int.MaxValue, lowerSubTop = int.MinValue;
                foreach (Hull hull in Hull.HullList)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        if (hull.Submarine != subs[i]) { continue; }
                        if (hull.WorldRect.Right - 5 < hullRects[i].X) { continue; }
                        if (hull.WorldRect.X + 5 > hullRects[i].Right) { continue; }

                        if (i == 0) //lower hull
                        {
                            if (hull.WorldPosition.Y > hullRects[i].Y - hullRects[i].Height / 2) { continue; }
                            lowerSubTop = Math.Max(hull.WorldRect.Y, lowerSubTop);
                        }
                        else //upper hull
                        {
                            if (hull.WorldPosition.Y < hullRects[i].Y - hullRects[i].Height / 2) { continue; }
                            upperSubBottom = Math.Min(hull.WorldRect.Y - hull.WorldRect.Height, upperSubBottom);
                        }
                    }
                }

                if (upperSubBottom == int.MaxValue || lowerSubTop == int.MinValue)
                {
                    DebugConsole.NewMessage("Creating hulls between docking ports failed. Could not find a hull next to the docking port.");
                    return;
                }

                //expand lower hull to the topmost hull of the lower sub 
                //(unless the difference is more than 100 units - if the distance is very large 
                //there's something wrong with the positioning of the docking ports or submarine hulls)
                int lowerHullDiff = ((hullRects[0].Y - hullRects[0].Height) - lowerSubTop) + 5;
                if (lowerHullDiff > 0)
                {
                    if (lowerHullDiff > 100)
                    {
                        DebugConsole.NewMessage("Creating hulls between docking ports failed. The lower docking port seems to be very far from any hulls in the lower submarine.");
                        return;
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
                        DebugConsole.NewMessage("Creating hulls between docking ports failed. The upper docking port seems to be very far from any hulls in the upper submarine.");
                        return;
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
                    DebugConsole.NewMessage("Creating hulls between docking ports failed. The upper hull seems to be very far from the lower hull.");
                    return;
                }
                else if (midHullDiff > 0)
                {
                    hullRects[0].Height += midHullDiff / 2 + 1;
                    hullRects[1].Y -= midHullDiff / 2 + 1;
                    hullRects[1].Height += midHullDiff / 2 + 1;
                }

                int expand = 5;
                for (int i = 0; i < 2; i++)
                {
                    hullRects[i].Y += expand;
                    hullRects[i].Height += expand * 2;
                    hullRects[i].Location -= MathUtils.ToPoint(subs[i].WorldPosition - subs[i].HiddenSubPosition);
                    hulls[i] = new Hull(hullRects[i], subs[i])
                    {
                        RoomName = IsHorizontal ? "entityname.dockingport" : "entityname.dockinghatch",
                        AvoidStaying = true
                    };
                    hulls[i].AddToGrid(subs[i]);
                    hulls[i].FreeID();

                    for (int j = 0; j < 2; j++)
                    {
                        bodies[i + j * 2] = GameMain.World.CreateEdge(
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X + hullRects[i].Width * j, hullRects[i].Y)),
                            ConvertUnits.ToSimUnits(new Vector2(hullRects[i].X + hullRects[i].Width * j, hullRects[i].Y - hullRects[i].Height)),
                            BodyType.Static);
                    }
                }
#if CLIENT
                for (int i = 0; i < 2; i++)
                {
                    convexHulls[i] =
                        new ConvexHull(new Rectangle(
                            new Point(item.Rect.X + item.Rect.Width * i, (int)item.Position.Y), 
                            new Point(0, (int)(DockingTarget.item.WorldPosition.Y - item.WorldPosition.Y))), IsHorizontal, item);
                }
#endif

                if (midHullDiff <= 100 && hulls[0].Submarine != null)
                {
                    outsideBlocker = hulls[0].Submarine.PhysicsBody.FarseerBody.CreateRectangle(
                        ConvertUnits.ToSimUnits(hullRects[0].Width),
                        ConvertUnits.ToSimUnits(hullRects[0].Height + hullRects[1].Height),
                        density: 0.0f,
                        offset: ConvertUnits.ToSimUnits(new Vector2(hullRects[0].Center.X, hullRects[0].Y) - hulls[0].Submarine.HiddenSubPosition),
                        Physics.CollisionWall,
                        Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionCharacter | Physics.CollisionItemBlocking | Physics.CollisionProjectile);
                    outsideBlocker.UserData = this;
                }

                gap = new Gap(new Rectangle(hullRects[0].X, hullRects[0].Y + 2, hullRects[0].Width, 4), false, subs[0]);
            }

            LinkHullsToGaps();

            Item.UpdateHulls();

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
                if (body == null) { continue; }
                body.BodyType = BodyType.Static;
                body.Friction = 0.5f;
            }
        }

        partial void RemoveConvexHulls();

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
                if (hulls[0].WorldRect.X > hulls[1].WorldRect.X)
                {
                    var temp = hulls[0];
                    hulls[0] = hulls[1];
                    hulls[1] = temp;
                }
                gap.linkedTo.Add(hulls[0]);
                gap.linkedTo.Add(hulls[1]);
            }
            else
            {
                if (hulls[0].WorldRect.Y < hulls[1].WorldRect.Y)
                {
                    var temp = hulls[0];
                    hulls[0] = hulls[1];
                    hulls[1] = temp;
                }
                gap.linkedTo.Add(hulls[0]);
                gap.linkedTo.Add(hulls[1]);
            }

            for (int i = 0; i < 2; i++)
            {
                Gap doorGap = i == 0 ? Door?.LinkedGap : DockingTarget?.Door?.LinkedGap;
                if (doorGap == null) { continue; }
                doorGap.DisableHullRechecks = true;
                if (doorGap.linkedTo.Count >= 2) { continue; }

                if (IsHorizontal)
                {
                    if (doorGap.WorldPosition.X < gap.WorldPosition.X)
                    {
                        if (!doorGap.linkedTo.Contains(hulls[0])) { doorGap.linkedTo.Add(hulls[0]); }
                    }
                    else
                    {
                        if (!doorGap.linkedTo.Contains(hulls[1])) { doorGap.linkedTo.Add(hulls[1]); }
                    }
                    //make sure the left hull is linked to the gap first (gap logic assumes that the first hull is the one to the left)
                    if (doorGap.linkedTo.Count > 1 && doorGap.linkedTo[0].WorldRect.X > doorGap.linkedTo[1].WorldRect.X)
                    {
                        var temp = doorGap.linkedTo[0];
                        doorGap.linkedTo[0] = doorGap.linkedTo[1];
                        doorGap.linkedTo[1] = temp;
                    }
                }
                else
                {
                    if (doorGap.WorldPosition.Y > gap.WorldPosition.Y)
                    {
                        if (!doorGap.linkedTo.Contains(hulls[0])) { doorGap.linkedTo.Add(hulls[0]); }
                    }
                    else
                    {
                        if (!doorGap.linkedTo.Contains(hulls[1])) { doorGap.linkedTo.Add(hulls[1]); }
                    }
                    //make sure the upper hull is linked to the gap first (gap logic assumes that the first hull is above the second one)
                    if (doorGap.linkedTo.Count > 1 && doorGap.linkedTo[0].WorldRect.Y < doorGap.linkedTo[1].WorldRect.Y)
                    {
                        var temp = doorGap.linkedTo[0];
                        doorGap.linkedTo[0] = doorGap.linkedTo[1];
                        doorGap.linkedTo[1] = temp;
                    }
                }                
            }
        }

        public void Undock(bool applyEffects = true)
        {
            if (DockingTarget == null || !docked) { return; }
            
            forceLockTimer = 0.0f;
            dockingCooldown = 0.1f;

            if (applyEffects)
            {
                ApplyStatusEffects(ActionType.OnSecondaryUse, 1.0f);
            }

            DockingTarget.item.Submarine.ConnectedDockingPorts.Remove(item.Submarine);
            DockingTarget.item.Submarine.RefreshConnectedSubs();
            item.Submarine.ConnectedDockingPorts.Remove(DockingTarget.item.Submarine);
            item.Submarine.RefreshConnectedSubs();

            if (Door != null && DockingTarget.Door != null)
            {
                WayPoint myWayPoint = WayPoint.WayPointList.Find(wp => Door.LinkedGap == wp.ConnectedGap);
                WayPoint targetWayPoint = WayPoint.WayPointList.Find(wp => DockingTarget.Door.LinkedGap == wp.ConnectedGap);

                if (myWayPoint != null && targetWayPoint != null)
                {
                    myWayPoint.FindHull();
                    if (myWayPoint.linkedTo.Contains(targetWayPoint))
                    {
                        myWayPoint.linkedTo.Remove(targetWayPoint);
                        myWayPoint.OnLinksChanged?.Invoke(myWayPoint);
                    }
                    targetWayPoint.FindHull();
                    if (targetWayPoint.linkedTo.Contains(myWayPoint))
                    {
                        targetWayPoint.linkedTo.Remove(myWayPoint);
                        targetWayPoint.OnLinksChanged?.Invoke(targetWayPoint);
                    }
                }
            }
            
            item.linkedTo.Clear();

            docked = false;

            Item.Submarine.EnableObstructedWaypoints(DockingTarget.Item.Submarine);
            obstructedWayPointsDisabled = false;
            Item.Submarine.RefreshOutdoorNodes();

            DockingTarget.Undock();
            DockingTarget = null;

            //Flag power connection
            Connection powerConnection = Item.Connections.Find(c => c.IsPower);
            if (powerConnection != null)
            {
                Powered.ChangedConnections.Add(powerConnection);
            }

            if (doorBody != null)
            {
                GameMain.World.Remove(doorBody);
                doorBody = null;
            }

            var wire = item.GetComponent<Wire>();
            wire?.Drop(null);

            if (joint != null)
            {
                GameMain.World.Remove(joint);
                joint = null;
            }
            
            hulls[0]?.Remove(); hulls[0] = null;
            hulls[1]?.Remove(); hulls[1] = null;

            RemoveConvexHulls();

            if (gap != null)
            {
                gap.Remove();
                gap = null;
            }
            
            if (bodies != null)
            {
                foreach (Body body in bodies)
                {
                    if (body == null) { continue; }
                    GameMain.World.Remove(body);
                }
                bodies = null;
            }

            outsideBlocker?.Body.Remove(outsideBlocker);
            outsideBlocker = null;

#if SERVER
            if (GameMain.Server != null && (!item.Submarine?.Loading ?? true))
            {
                item.CreateServerEvent(this);
            }
#elif CLIENT
            autodockingVerification?.Close();
            autodockingVerification = null;
#endif
            OnUnDocked?.Invoke();
            OnUnDocked = null;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            dockingCooldown -= deltaTime;
            if (DockingTarget == null)
            {
                dockingState = MathHelper.Lerp(dockingState, 0.0f, deltaTime * 10.0f);
                if (dockingState < 0.01f) { docked = false; }
                item.SendSignal("0", "state_out");
                item.SendSignal(AnotherPortInProximity ? "1" : "0", "proximity_sensor");
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
                    dockingState = MathHelper.Lerp(dockingState, 0.5f, deltaTime * 10.0f);

                    forceLockTimer += deltaTime;

                    Vector2 jointDiff = joint.WorldAnchorB - joint.WorldAnchorA;

                    if (jointDiff.LengthSquared() > 0.04f * 0.04f && forceLockTimer < ForceLockDelay)
                    {
                        float totalMass = item.Submarine.PhysicsBody.Mass + DockingTarget.item.Submarine.PhysicsBody.Mass;
                        float massRatio1, massRatio2;
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
                        Lock(isNetworkMessage: false);
                    }
                }
                else
                {
                    if (DockingTarget.Door != null && doorBody != null)
                    {
                        doorBody.Enabled = DockingTarget.Door.Body.Enabled && !(DockingTarget.Door.Body.FarseerBody.FixtureList.FirstOrDefault()?.IsSensor ?? false);                        
                    }
                    dockingState = MathHelper.Lerp(dockingState, 1.0f, deltaTime * 10.0f);
                }

                item.SendSignal(IsLocked ? "1" : "0", "state_out");
            }
            if (!obstructedWayPointsDisabled && dockingState >= 0.99f)
            {
                Item.Submarine.DisableObstructedWayPoints(DockingTarget?.Item.Submarine);
                Item.Submarine.RefreshOutdoorNodes();
                obstructedWayPointsDisabled = true;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            list.Remove(this);
            hulls[0]?.Remove(); hulls[0] = null;
            hulls[1]?.Remove(); hulls[1] = null;
            gap?.Remove(); gap = null;
            RemoveConvexHulls();

            overlaySprite?.Remove();
            overlaySprite = null;
        }

        private bool initialized = false;
        private void InitializeLinks()
        {
            if (initialized) { return; }
            initialized = true;

            float maxXDist = (item.Prefab.Sprite.size.X * item.Prefab.Scale) / 2;
            float closestYDist = (item.Prefab.Sprite.size.Y * item.Prefab.Scale) / 2;
            foreach (Item it in Item.ItemList)
            {
                if (it.Submarine != item.Submarine) { continue; }

                var doorComponent = it.GetComponent<Door>();
                if (doorComponent == null || doorComponent.IsHorizontal == IsHorizontal) { continue; }

                float yDist = Math.Abs(it.Position.Y - item.Position.Y);
                if (item.linkedTo.Contains(it))
                {
                    // If there's a door linked to the docking port, always treat it close enough.
                    yDist = Math.Min(closestYDist, yDist);
                }
                else if (Math.Abs(it.Position.X - item.Position.X) > maxXDist)
                {
                    // Too far left/right
                    continue;
                }

                if (yDist <= closestYDist)
                {
                    Door = doorComponent;
                    closestYDist = yDist;
                }
            }

            if (!item.linkedTo.Any()) { return; }

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

            Wire wire = item.GetComponent<Wire>();
            if (wire != null)
            {
                wire.Locked = true;
                wire.Hidden = true;
                if (wire.Connections.Contains(null))
                {
                    wire.Drop(null);
                }                
            }

            if (!item.linkedTo.Any()) { return; }

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

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
#if CLIENT
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) 
            { 
                return; 
            }
            if (GameMain.GameSession?.Campaign != null && !CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMap))
            {
                return;
            }
#endif

            if (dockingCooldown > 0.0f) { return; }

            bool wasDocked = docked;
            DockingPort prevDockingTarget = DockingTarget;

            bool newDockedState = wasDocked;
            switch (connection.Name)
            {
                case "toggle":
                    if (signal.value != "0")
                    {
                        newDockedState = !docked;
                    }
                    break;
                case "set_active":
                case "set_state":
                    newDockedState = signal.value != "0";
                    break;
            }

            if (newDockedState != wasDocked)
            {
                bool tryingToToggleOutpostDocking = docked ? 
                    DockingTarget?.Item?.Submarine?.Info?.IsOutpost ?? false :
                    FindAdjacentPort()?.Item?.Submarine?.Info?.IsOutpost ?? false;
                //trying to dock/undock from an outpost and the signal was sent by some automated system instead of a character
                // -> ask if the player really wants to dock/undock to prevent a softlock if someone's wired the docking port
                //    in a way that makes always makes it dock/undock immediately at the start of the roun
                if (GameMain.NetworkMember != null && tryingToToggleOutpostDocking && signal.sender == null)
                {
                    if (allowOutpostAutoDocking == AllowOutpostAutoDocking.Ask)
                    {
#if CLIENT
                        if (!outpostAutoDockingPromptShown)
                        {
                            autodockingVerification = new GUIMessageBox(string.Empty,
                                 TextManager.Get(newDockedState ? "autodockverification" : "autoundockverification"),
                                 new LocalizedString[] { TextManager.Get("Yes"), TextManager.Get("No") });
                            autodockingVerification.Buttons[0].OnClicked += (btn, userdata) =>
                            {
                                autodockingVerification?.Close();
                                autodockingVerification = null;
                                if (item.Removed || GameMain.Client == null) { return false; }
                                allowOutpostAutoDocking = AllowOutpostAutoDocking.Yes;
                                item.CreateClientEvent(this);
                                return true;
                            };
                            autodockingVerification.Buttons[1].OnClicked += (btn, userdata) =>
                            {
                                autodockingVerification?.Close();
                                autodockingVerification = null;
                                if (item.Removed || GameMain.Client == null) { return false; }
                                allowOutpostAutoDocking = AllowOutpostAutoDocking.No;
                                item.CreateClientEvent(this);
                                return true;
                            };
                        }
#endif
                        outpostAutoDockingPromptShown = true;
                        return;
                    }
                    else if (allowOutpostAutoDocking == AllowOutpostAutoDocking.No)
                    {
                        return;
                    }
                }

                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

                Docked = newDockedState;                
            }

#if SERVER
            if (signal.sender != null && docked != wasDocked)
            {
                if (docked)
                {
                    if (item.Submarine != null && DockingTarget?.item?.Submarine != null)
                        GameServer.Log(GameServer.CharacterLogName(signal.sender) + " docked " + item.Submarine.Info.Name + " to " + DockingTarget.item.Submarine.Info.Name, ServerLog.MessageType.ItemInteraction);
                }
                else
                {
                    if (item.Submarine != null && prevDockingTarget?.item?.Submarine != null)
                        GameServer.Log(GameServer.CharacterLogName(signal.sender) + " undocked " + item.Submarine.Info.Name + " from " + prevDockingTarget.item.Submarine.Info.Name, ServerLog.MessageType.ItemInteraction);
                }
            }
#endif
        }
    }
}
