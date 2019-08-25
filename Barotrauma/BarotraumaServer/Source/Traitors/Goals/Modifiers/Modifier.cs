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
            public override IEnumerable<string> StatusTextValues => new [] { InfoText, TextManager.FormatServerMessage(StatusValueTextId) };

            public override IEnumerable<string> InfoTextKeys => Goal.InfoTextKeys;
            public override IEnumerable<string> InfoTextValues => Goal.InfoTextValues;

            public override IEnumerable<string> CompletedTextKeys => Goal.CompletedTextKeys;
            public override IEnumerable<string> CompletedTextValues => Goal.CompletedTextValues;

            protected internal override string GetStatusText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => Goal.GetStatusText(traitor, textId, keys, values);
            protected internal override string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => Goal.GetInfoText(traitor, textId, keys, values);
            protected internal override string GetCompletedText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => Goal.GetCompletedText(traitor, textId, keys, values);

            public override string StatusText => GetStatusText(Traitor, StatusTextId, StatusTextKeys, StatusTextValues);
            public override string InfoText => GetInfoText(Traitor, InfoTextId, InfoTextKeys, InfoTextValues);
            public override string CompletedText => CompletedTextId != null ? GetCompletedText(Traitor, CompletedTextId, CompletedTextKeys, CompletedTextValues) : StatusText;

            public override bool IsCompleted => Goal.IsCompleted;
            public override bool IsStarted => base.IsStarted && Goal.IsStarted;
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