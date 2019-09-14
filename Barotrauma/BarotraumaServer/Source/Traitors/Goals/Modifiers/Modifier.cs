using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public abstract class Modifier : Goal
        {
            protected Goal Goal { get; }

            public override string StatusValueTextId => Goal.StatusValueTextId;

            public override string StatusTextId
            {
                get => Goal.StatusTextId;
                set => Goal.StatusTextId = value;
            }

            public override string InfoTextId
            {
                get => Goal.InfoTextId;
                set => Goal.InfoTextId = value;
            }

            public override string CompletedTextId
            {
                get => Goal.CompletedTextId;
                set => Goal.CompletedTextId = value;
            }

            public override IEnumerable<string> StatusTextKeys => Goal.StatusTextKeys;
            public override IEnumerable<string> StatusTextValues(Traitor traitor) => new [] { InfoText(traitor), TextManager.FormatServerMessage(StatusValueTextId) };

            public override IEnumerable<string> InfoTextKeys => Goal.InfoTextKeys;
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => Goal.InfoTextValues(traitor);

            public override IEnumerable<string> CompletedTextKeys => Goal.CompletedTextKeys;
            public override IEnumerable<string> CompletedTextValues(Traitor traitor) => Goal.CompletedTextValues(traitor);

            protected internal override string GetStatusText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => Goal.GetStatusText(traitor, textId, keys, values);
            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => Goal.GetInfoText(traitor, textId, keys, values);
            protected internal override string GetCompletedText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => Goal.GetCompletedText(traitor, textId, keys, values);

            public override string StatusText(Traitor traitor) => GetStatusText(traitor, StatusTextId, StatusTextKeys, StatusTextValues(traitor));
            public override string InfoText(Traitor traitor) => GetInfoText(traitor, InfoTextId, InfoTextKeys, InfoTextValues(traitor));
            public override string CompletedText(Traitor traitor) => CompletedTextId != null ? GetCompletedText(traitor, CompletedTextId, CompletedTextKeys, CompletedTextValues(traitor)) : StatusText(traitor);

            public override bool IsCompleted => Goal.IsCompleted;
            public override bool IsStarted(Traitor traitor) => base.IsStarted(traitor) && Goal.IsStarted(traitor);
            public override bool CanBeCompleted => base.CanBeCompleted && Goal.CanBeCompleted;

            public override bool IsEnemy(Character character) => base.IsEnemy(character) || Goal.IsEnemy(character);

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                Goal.Update(deltaTime);
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                if (!Goal.Start(traitor))
                {
                    return false;
                }
                return true;
            }

            protected Modifier(Goal goal) : base()
            {
                Goal = goal;
            }
        }
    }
}