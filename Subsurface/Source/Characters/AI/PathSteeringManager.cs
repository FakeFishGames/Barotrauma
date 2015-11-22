using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using FarseerPhysics;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class PathSteeringManager : SteeringManager
    {
        private PathFinder pathFinder;
        private SteeringPath currentPath;

        private Character character;

        private List<Controller> openableButtons;

        public SteeringPath CurrentPath
        {
            get { return currentPath; }
        }

        public PathFinder PathFinder
        {
            get { return pathFinder; }
        }

        private Vector2 currentTarget;

        private float findPathTimer;

        public PathSteeringManager(ISteerable host)
            : base(host)
        {
            pathFinder = new PathFinder(WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path), true);
            pathFinder.GetNodePriority = GetNodePriority;

            character = (host as AIController).Character;

            openableButtons = new List<Controller>();
        }

        public override void Update(float speed = 1)
        {
            base.Update(speed);

            findPathTimer -= 1.0f / 60.0f;
        }


        protected override Vector2 DoSteeringSeek(Vector2 target, float speed = 1)
        {
            //find a new path if one hasn't been found yet or the target is different from the current target
            if (currentPath == null || Vector2.DistanceSquared(target, currentTarget)>10.0f)
            {
                if (findPathTimer > 0.0f) return Vector2.Zero;

                currentTarget = target;
                currentPath = pathFinder.FindPath(host.SimPosition, ConvertUnits.ToSimUnits(target));

                findPathTimer = 1.0f;

                return DiffToCurrentNode();
            }


            //if (pathSteering == null || pathSteering.CurrentPath == null || pathSteering.CurrentPath.CurrentNode == null) return;

            //if (currentPath.CurrentNode.ConnectedGap != null && currentPath.CurrentNode.ConnectedGap.Open < 0.9f)
            //{
                foreach (Controller controller in openableButtons)
                {
                    if (Vector2.Distance(controller.Item.SimPosition, character.SimPosition) > controller.Item.PickDistance) continue;

                    controller.Item.Pick(character, false, true);
                }
            //}


            
            Vector2 diff = DiffToCurrentNode();
            
            if (diff == Vector2.Zero) return -host.Steering;

            return (diff == Vector2.Zero) ? Vector2.Zero : Vector2.Normalize(diff)*speed;            
        }

        private Vector2 DiffToCurrentNode()
        {
            if (currentPath == null) return Vector2.Zero;

            currentPath.CheckProgress(host.SimPosition, 0.45f);

            if (currentPath.CurrentNode == null) return Vector2.Zero;

            return currentPath.CurrentNode.SimPosition - host.SimPosition;
        }

        private float GetNodePriority(PathNode node, PathNode nextNode)
        {
            if (character==null) return 0.0f;
            if (nextNode.Waypoint.ConnectedGap!=null)
            {
                if (nextNode.Waypoint.ConnectedGap.Open > 0.9f) return 0.0f;
                if (nextNode.Waypoint.ConnectedGap.ConnectedDoor == null) return 100.0f;

                var doorButtons = GetDoorButtons(nextNode.Waypoint.ConnectedGap.ConnectedDoor);
                foreach (Controller button in doorButtons)
                {
                    if (Math.Sign(button.Item.Position.X - nextNode.Waypoint.Position.X) !=
                        Math.Sign(node.Position.X - nextNode.Position.X)) continue;

                    if (!button.HasRequiredItems(character, false)) return 1000.0f;
                }
            }

            return 0.0f;
        }

        private List<Controller> GetDoorButtons(Door door)
        {
            if (door == null) return new List<Controller>();
            ConnectionPanel connectionPanel =  door.Item.GetComponent<ConnectionPanel>();

            List<Controller> doorButtons = new List<Controller>();

            foreach (Connection c in connectionPanel.Connections)
            {
                foreach (Wire w in c.Wires)
                {
                    if (w == null) continue;
                    var otherConnection = w.OtherConnection(c);

                    if (otherConnection.Item == door.Item || otherConnection == null) continue;

                    var controller = otherConnection.Item.GetComponent<Controller>();
                    if (controller != null)
                    {
                        doorButtons.Add(controller);
                        if (!openableButtons.Contains(controller)) openableButtons.Add(controller);
                    }
                }
            }

            return doorButtons;
        }
    }
    
}
