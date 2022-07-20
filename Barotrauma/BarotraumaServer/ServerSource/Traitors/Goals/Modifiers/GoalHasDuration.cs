using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalHasDuration : Modifier
        {
            private readonly float requiredDuration;
            private readonly bool countTotalDuration;
            private readonly string durationInfoTextId;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[duration]" });

            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { requiredDuration.ToString(CultureInfo.InvariantCulture) });

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values)
            {
                var infoText = base.GetInfoText(traitor, textId, keys, values);
                return !string.IsNullOrEmpty(durationInfoTextId) && !infoText.Contains("[duration]") ? TextManager.FormatServerMessage(durationInfoTextId,
                    ("[infotext]", infoText), ("[duration]", requiredDuration.ToString(CultureInfo.InvariantCulture))) : infoText;
            }

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private float remainingDuration = float.NaN;

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                if (Goal.IsCompleted)
                {
                    if (!float.IsNaN(remainingDuration))
                    {
                        remainingDuration -= deltaTime;
                    }
                    else
                    {
                        remainingDuration = requiredDuration;
                    }
                    isCompleted |= remainingDuration <= 0.0f;
                }
                else if (!countTotalDuration)
                {
                    remainingDuration = float.NaN;
                }
            }

            public GoalHasDuration(Goal goal, float requiredDuration, bool countTotalDuration, string durationInfoTextId) : base(goal)
            {
                this.requiredDuration = requiredDuration;
                this.countTotalDuration = countTotalDuration;
                this.durationInfoTextId = durationInfoTextId;
            }
        }
    }
}

