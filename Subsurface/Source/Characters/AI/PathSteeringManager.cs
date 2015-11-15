using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using FarseerPhysics;

namespace Barotrauma
{
    class PathSteeringManager : SteeringManager
    {
        private PathFinder pathFinder;
        private SteeringPath currentPath;

        private Vector2 currentTarget;

        private float findPathTimer;

        public PathSteeringManager(ISteerable host)
            : base(host)
        {}

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
                currentPath = pathFinder.FindPath(ConvertUnits.ToDisplayUnits(host.SimPosition), target);

                findPathTimer = 1.0f;

                return DiffToCurrentNode();
            }
            
            Vector2 diff = DiffToCurrentNode();

            return (diff == Vector2.Zero) ? Vector2.Zero : Vector2.Normalize(diff)*speed;            
        }

        private Vector2 DiffToCurrentNode()
        {
            if (currentPath == null) return Vector2.Zero;

            currentPath.CheckProgress(host.SimPosition, 0.1f);

            if (currentPath.CurrentNode == null) return Vector2.Zero;

            return currentPath.CurrentNode.SimPosition - host.SimPosition;
        }
    }
}
