using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalIsOptional : Modifier
        {
            private const string GoalIsOptionalInfoTextId = "TraitorGoalIsOptionalInfoText";

            public override string StatusValueTextId => (base.IsStarted && !base.CanBeCompleted) ? "failed" : base.StatusValueTextId;

            public override IEnumerable<string> StatusTextValues
            {
                get {
                    var values = base.StatusTextValues.ToArray();
                    values[1] = TextManager.GetServerMessage(StatusValueTextId);
                    return values;
                }
            }

            public override bool IsCompleted => base.IsCompleted || (base.IsStarted && !base.CanBeCompleted);
            public override bool CanBeCompleted => true;

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => TextManager.FormatServerMessage(GoalIsOptionalInfoTextId, new[]
            {
                "[infotext]"
            }, new[]
            {
                base.GetInfoText(traitor, textId, keys, values)
            });

            public GoalIsOptional(Goal goal) : base(goal)
            {
            }
        }
    }
}