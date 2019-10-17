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
            private float requiredDistanceInMeters;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[distance]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { $"{requiredDistanceInMeters:0.00}" });

            public override bool IsCompleted
            {
                get
                {
                    return Traitors.Any(traitor =>
                    {
                        if (traitor.Character?.Submarine == null)
                        {
                            return false;
                        }
                        var characterPosition = traitor.Character.WorldPosition;
                        var submarinePosition = traitor.Character.Submarine.WorldPosition;
                        var distance = Vector2.DistanceSquared(characterPosition, submarinePosition);
                        return distance >= requiredDistanceSqr;
                    });
                }
            }

            public GoalReachDistanceFromSub(float requiredDistance) : base()
            {
                InfoTextId = "TraitorGoalReachDistanceFromSub";
                requiredDistanceInMeters = requiredDistance;
                this.requiredDistance = requiredDistance / Physics.DisplayToRealWorldRatio;
                requiredDistanceSqr = this.requiredDistance * this.requiredDistance;
            }
        }
    }
}
