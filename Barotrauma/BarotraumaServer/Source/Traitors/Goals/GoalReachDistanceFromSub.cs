using System;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalReachDistanceFromSub : Goal
        {
            private readonly float requiredDistance;
            private readonly float requiredDistanceSqr;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[distance]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { $"{requiredDistance:0.00}" });

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
                    return distance >= requiredDistanceSqr;
                }
            }

            public GoalReachDistanceFromSub(float requiredDistance) : base()
            {
                InfoTextId = "TraitorGoalReachDistanceFromSub";
                this.requiredDistance = requiredDistance;
                requiredDistanceSqr = requiredDistance * requiredDistance;
            }
        }
    }
}
