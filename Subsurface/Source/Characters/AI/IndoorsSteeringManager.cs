using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class IndoorsSteeringManager : SteeringManager
    {
        private PathFinder pathFinder;
        private SteeringPath currentPath;

        private bool canOpenDoors;

        private Character character;

        private Vector2 currentTarget;

        private float findPathTimer;

        public SteeringPath CurrentPath
        {
            get { return currentPath; }
        }

        public PathFinder PathFinder
        {
            get { return pathFinder; }
        }

        public Vector2 CurrentTarget
        {
            get { return currentTarget; }
        }

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
                Vector2 pos = host.SimPosition;
                if (character != null && character.Submarine == null)
                {
                    var targetHull = Hull.FindHull(FarseerPhysics.ConvertUnits.ToDisplayUnits(target), null, false);
                    if (targetHull!=null && targetHull.Submarine != null)
                    {
                        pos -= targetHull.SimPosition;
                    }
                }   

                currentPath = pathFinder.FindPath(pos, target);

                findPathTimer = Rand.Range(1.0f,1.2f);

                return DiffToCurrentNode();
            }
                        
            Vector2 diff = DiffToCurrentNode();
            
            if (diff == Vector2.Zero) return -host.Steering;

            return Vector2.Normalize(diff) * speed;          
        }

        private Vector2 DiffToCurrentNode()
        {
            if (currentPath == null || currentPath.Finished) return Vector2.Zero;

            if (currentPath.Finished)
            {
                Vector2 pos2 = host.SimPosition;
                if (character != null && character.Submarine == null && CurrentPath.Nodes.Last().Submarine != null)
                {
                    //todo: take multiple subs into account
                    pos2 -= CurrentPath.Nodes.Last().Submarine.SimPosition;
                }   
                return currentTarget-pos2;
            }

            if (canOpenDoors && !character.LockHands) CheckDoorsInPath();

            float allowedDistance = character.AnimController.InWater ? 1.0f : 0.6f;
            if (currentPath.CurrentNode!=null && currentPath.CurrentNode.SimPosition.Y > character.SimPosition.Y+1.0f) allowedDistance*=0.5f;

            Vector2 pos = host.SimPosition;

            if (character != null && currentPath.CurrentNode != null)
            {
                if (CurrentPath.CurrentNode.Submarine != null)
                {
                    if (character.Submarine == null)
                    {
                        pos -= CurrentPath.CurrentNode.Submarine.SimPosition;
                    }
                    else if (character.Submarine != currentPath.CurrentNode.Submarine)
                    {
                        pos -= FarseerPhysics.ConvertUnits.ToSimUnits(currentPath.CurrentNode.Submarine.Position-character.Submarine.Position);
                    }
                }

            }

            if (currentPath.CurrentNode!= null && currentPath.CurrentNode.Ladders!=null)
            {
                if (character.SelectedConstruction != currentPath.CurrentNode.Ladders.Item && currentPath.CurrentNode.Ladders.Item.IsInsideTrigger(character.WorldPosition))
                {
                    currentPath.CurrentNode.Ladders.Item.Pick(character, false, true);
                }
            }

            currentPath.CheckProgress(pos, allowedDistance);

            if (currentPath.CurrentNode == null) return Vector2.Zero;

            var hull = character.AnimController.CurrentHull;

            if (character.AnimController.Anim == AnimController.Animation.Climbing)
            {
                float x = currentPath.CurrentNode.SimPosition.X - pos.X;
                float y = (currentPath.CurrentNode.SimPosition.Y) - pos.Y;
                
                if (Math.Abs(x) < Math.Abs(y) * 10.0f)                    
                {
                    x = 0.0f;                    
                }
                else if (character.AnimController.LowestLimb != null && hull != null)
                {
                    if (character.AnimController.LowestLimb.Position.Y < hull.Rect.Y - hull.Rect.Height + 10.0f) x = 0.0f;
                }

                character.AnimController.IgnorePlatforms = false;
                return new Vector2(x,y);
            }

            return currentPath.CurrentNode.SimPosition - pos;
        }

        private void CheckDoorsInPath()
        {
            for (int i = 0; i < 2; i++)
            {


                WayPoint node = null;
                WayPoint nextNode = null;
                
                if (i==0)
                {
                    node = currentPath.CurrentNode;
                    nextNode = currentPath.NextNode;
                }
                else
                {
                    node = currentPath.PrevNode;
                    nextNode = currentPath.CurrentNode;
                }

                if (node == null || node.ConnectedGap == null || node.ConnectedGap.ConnectedDoor == null) continue;

                if (nextNode == null) continue;

                var door = node.ConnectedGap.ConnectedDoor;

                bool shouldBeOpen = false;

                if (door.LinkedGap.isHorizontal)
                {
                    int currentDir = Math.Sign(nextNode.WorldPosition.X - door.Item.WorldPosition.X);

                    shouldBeOpen = (door.Item.WorldPosition.X - character.WorldPosition.X) * currentDir > -50.0f;
                }
                else
                {
                    int currentDir = Math.Sign(nextNode.WorldPosition.Y - door.Item.WorldPosition.Y);

                    shouldBeOpen = (door.Item.WorldPosition.Y - character.WorldPosition.Y) * currentDir > -80.0f;
                }
                

                //toggle the door if it's the previous node and open, or if it's current node and closed
                if (door.IsOpen != shouldBeOpen)
                {
                    var buttons = door.Item.GetConnectedComponents<Controller>(true);

                    Controller closestButton = null;
                    float closestDist = 0.0f;

                    foreach (Controller controller in buttons)
                    {
                        float dist = Vector2.Distance(controller.Item.WorldPosition, character.WorldPosition);
                        if (dist > controller.Item.PickDistance * 2.0f) continue;

                        if (dist < closestDist || closestButton == null)
                        {
                            closestButton = controller;
                            closestDist = dist;
                        }
                    }

                    if (closestButton != null)
                    {
                        closestButton.Item.Pick(character, false, true);
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

                if (!canOpenDoors || character.LockHands) return null;

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
