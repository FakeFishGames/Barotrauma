using System;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalReachDistanceFromSub : Goal
        {
            private readonly float requiredDistance;
            private readonly float requiredDistanceSqr;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[distance]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { $"{requiredDistance:2}" });

            public override bool IsCompleted
            {
                get
                {
                    if (Traitor == null || Traitor.Character == null || Traitor.Character.Submarine == null)
                    {
                        return false;
                    }
                    var characterPosition = Traitor.Character.WorldPosition;
                    var submarinePosition = Traitor.Character.Submarine.WorldPosition;
                    var distance = Vector2.DistanceSquared(characterPosition, submarinePosition);
                    Console.WriteLine("DISTANCE: " + distance + " >= " + requiredDistanceSqr);
                    return distance >= requiredDistanceSqr;
                }
            }

            public GoalReachDistanceFromSub(float requiredDistance) : base()
            {
                InfoTextId = "TraitorGoalReachDistanceFromSub"; /* TODO(xxx) */
                this.requiredDistance = requiredDistance;
                requiredDistanceSqr = requiredDistance * requiredDistance;
            }
        }
    }
}
