using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalIsOptional : Modifier
        {
            private readonly string optionalInfoTextId;

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

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values)
            {
                var infoText = base.GetInfoText(traitor, textId, keys, values);
                return !string.IsNullOrEmpty(optionalInfoTextId) ? TextManager.FormatServerMessage(optionalInfoTextId, new[] { "[infotext]" }, new[] { infoText }) : infoText;
            }

            public GoalIsOptional(Goal goal, string optionalInfoTextId) : base(goal)
            {
                this.optionalInfoTextId = optionalInfoTextId;
            }
        }
    }
}