using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using FarseerPhysics;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class IndoorsSteeringManager : SteeringManager
    {
        private PathFinder pathFinder;
        private SteeringPath currentPath;

        private bool canOpenDoors;

        private Character character;

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

        public IndoorsSteeringManager(ISteerable host, bool canOpenDoors)
            : base(host)
        {
            pathFinder = new PathFinder(WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path), true);
            pathFinder.GetNodePenalty = GetNodePenalty;

            this.canOpenDoors = canOpenDoors;

            character = (host as AIController).Character;

            findPathTimer = Rand.Range(0.0f, 1.0f);
        }

        public override void Update(float speed = 1)
        {
            base.Update(speed);

            findPathTimer -= 1.0f / 60.0f;
        }

        public void SetPath(SteeringPath path)
        {
            currentPath = path;
            if (path.Nodes.Any()) currentTarget = path.Nodes[path.Nodes.Count - 1].SimPosition;
            findPathTimer = 1.0f;
        }


        protected override Vector2 DoSteeringSeek(Vector2 target, float speed = 1)
        {
            //find a new path if one hasn't been found yet or the target is different from the current target
            if (currentPath == null || Vector2.Distance(target, currentTarget)>1.0f || findPathTimer < -5.0f)
            {
                if (findPathTimer > 0.0f) return Vector2.Zero;

                currentTarget = target;
                currentPath = pathFinder.FindPath(host.SimPosition, target);
                
                findPathTimer = Rand.Range(1.0f,1.2f);

                return DiffToCurrentNode();
            }
                        
            Vector2 diff = DiffToCurrentNode();
            
            if (diff == Vector2.Zero) return -host.Steering;
            
            return (diff == Vector2.Zero) ? Vector2.Zero : Vector2.Normalize(diff)*speed;            
        }

        private Vector2 DiffToCurrentNode()
        {
            if (currentPath == null) return Vector2.Zero;

            if (canOpenDoors) CheckDoorsInPath();

            float allowedDistance = character.AnimController.InWater ? 1.0f : 0.6f;
            if (currentPath.CurrentNode!=null && currentPath.CurrentNode.SimPosition.Y > character.SimPosition.Y+1.0f) allowedDistance*=0.5f;

            currentPath.CheckProgress(host.SimPosition, allowedDistance);

            if (currentPath.CurrentNode == null) return Vector2.Zero;

            //if (currentPath.CurrentNode.SimPosition.Y > character.SimPosition.Y+1.0f && character.AnimController.Stairs == null)
            //{
            //    return currentPath.PrevNode.SimPosition - host.SimPosition;
            //}

            return currentPath.CurrentNode.SimPosition - host.SimPosition;
        }

        private void CheckDoorsInPath()
        {
            for (int i = 0; i < 2; i++)
            {
                WayPoint node = i == 0 ? currentPath.CurrentNode : currentPath.PrevNode;

                if (node == null || node.ConnectedGap == null || node.ConnectedGap.ConnectedDoor == null) continue;

                var door = node.ConnectedGap.ConnectedDoor;

                bool open = currentPath.CurrentNode != null &&
                    Math.Sign(door.Item.SimPosition.X - host.SimPosition.X) == Math.Sign(currentPath.CurrentNode.SimPosition.X - host.SimPosition.X);

                //toggle the door if it's the previous node and open, or if it's current node and closed
                if (door.IsOpen != open)
                {
                    var buttons = door.Item.GetConnectedComponents<Controller>();

                    foreach (Controller controller in buttons)
                    {
                        if (Vector2.Distance(controller.Item.SimPosition, character.SimPosition) > controller.Item.PickDistance * 2.0f) continue;

                        controller.Item.Pick(character, false, true);
                        break;
                    }
                }
            }
        }

        private float? GetNodePenalty(PathNode node, PathNode nextNode)
        {
            if (character == null) return 0.0f;
            if (nextNode.Waypoint.ConnectedGap != null)
            {
                if (nextNode.Waypoint.ConnectedGap.Open > 0.9f) return 0.0f;
                if (nextNode.Waypoint.ConnectedGap.ConnectedDoor == null) return 100.0f;

                if (!canOpenDoors) return null;

                var doorButtons = nextNode.Waypoint.ConnectedGap.ConnectedDoor.Item.GetConnectedComponents<Controller>();
                if (!doorButtons.Any()) return null;

                foreach (Controller button in doorButtons)
                {
                    if (Math.Sign(button.Item.Position.X - nextNode.Waypoint.Position.X) !=
                        Math.Sign(node.Position.X - nextNode.Position.X)) continue;

                    if (!button.HasRequiredItems(character, false)) return null;
                }
            }

            if (node.Waypoint!=null && node.Waypoint.CurrentHull!=null)
            {
                var hull = node.Waypoint.CurrentHull;

                float penalty = hull.FireSources.Any() ? 1000.0f : 0.0f;

                if (character.NeedsAir && hull.Volume / hull.Rect.Width > 100.0f) penalty += 500.0f;
                if (character.PressureProtection < 10.0f && hull.Volume > hull.FullVolume) penalty += 1000.0f;
            }

            return 0.0f;
        }


    }
    
}
