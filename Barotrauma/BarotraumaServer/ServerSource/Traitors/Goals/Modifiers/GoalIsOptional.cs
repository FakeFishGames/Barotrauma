using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalIsOptional : Modifier
        {
            private readonly string optionalInfoTextId;

            public override string StatusValueTextId => (Traitors.Any(IsStarted) && !base.CanBeCompleted(Traitors)) ? "failed" : base.StatusValueTextId;

            public override IEnumerable<string> StatusTextValues(Traitor traitor)
            {
                var values = base.StatusTextValues(traitor).ToArray();
                values[1] = TextManager.FormatServerMessage(StatusValueTextId);
                return values;
            }

            public override bool IsCompleted => base.IsCompleted || (Traitors.Any(IsStarted) && !base.CanBeCompleted(Traitors));
            public override bool CanBeCompleted(ICollection<Traitor> traitors) => true;

            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values)
            {
                var infoText = base.GetInfoText(traitor, textId, keys, values);
                return !string.IsNullOrEmpty(optionalInfoTextId) ? TextManager.FormatServerMessage(optionalInfoTextId, ("[infotext]", infoText)) : infoText;
            }

            public GoalIsOptional(Goal goal, string optionalInfoTextId) : base(goal)
            {
                this.optionalInfoTextId = optionalInfoTextId;
            }
        }
    }
}