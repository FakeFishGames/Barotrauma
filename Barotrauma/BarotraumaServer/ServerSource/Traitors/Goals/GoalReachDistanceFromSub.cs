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
                        Submarine ownSub = null;

                        for (int i = 0; i < Submarine.MainSubs.Length; i++)
                        {
                            if (Submarine.MainSubs[i] != null && Submarine.MainSubs[i].TeamID == traitor.Character.TeamID)
                            {
                                ownSub = Submarine.MainSubs[i];
                                break;
                            }
                        }

                        if (ownSub == null) return false;                        

                        var characterPosition = traitor.Character.WorldPosition;
                        var submarinePosition = ownSub.WorldPosition;
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
