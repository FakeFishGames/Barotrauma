using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Server;

namespace Barotrauma
{
    partial class Traitor
    {
        public abstract class Goal
        {
            public Traitor Traitor { get; private set; }
            public TraitorMission Mission { get; internal set; }

            public virtual string StatusTextId { get; set; } = "TraitorGoalStatusTextFormat";

            public virtual string InfoTextId { get; set; } = null;

            public virtual string CompletedTextId { get; set; } = null;

            public virtual string StatusValueTextId => IsCompleted ? "complete" : "inprogress";

            public virtual IEnumerable<string> StatusTextKeys => new [] { "[infotext]", "[status]" };
            public virtual IEnumerable<string> StatusTextValues => new [] { InfoText, TextManager.FormatServerMessage(StatusValueTextId) };

            public virtual IEnumerable<string> InfoTextKeys => new string[] { };
            public virtual IEnumerable<string> InfoTextValues => new string[] { };

            public virtual IEnumerable<string> CompletedTextKeys => new string[] { };
            public virtual IEnumerable<string> CompletedTextValues => new string[] { };

            protected virtual string FormatText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => TextManager.FormatServerMessageWithGenderPronouns(traitor?.Character?.Info?.Gender ?? Gender.None, textId, keys, values);

            protected internal virtual string GetStatusText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => FormatText(traitor, textId, keys, values);
            protected internal virtual string GetInfoText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => FormatText(traitor, textId, keys, values);
            protected internal virtual string GetCompletedText(Traitor traitor, string textId, IEnumerable<string> keys, IEnumerable<string> values) => FormatText(traitor, textId, keys, values);

            public virtual string StatusText => GetStatusText(Traitor, StatusTextId, StatusTextKeys, StatusTextValues);
            public virtual string InfoText => GetInfoText(Traitor, InfoTextId, InfoTextKeys, InfoTextValues);

            public virtual string CompletedText => CompletedTextId != null ? GetCompletedText(Traitor, CompletedTextId, CompletedTextKeys, CompletedTextValues) : StatusText;

            public abstract bool IsCompleted { get; }
            public virtual bool IsStarted => Traitor != null;
            public virtual bool CanBeCompleted => !(Traitor?.Character?.IsDead ?? true);

            public virtual bool IsEnemy(Character character) => false;

            public virtual bool Start(Traitor traitor)
            {
                Traitor = traitor;
                return true;
            }

            public virtual void Update(float deltaTime)
            {
            }

            protected Goal()
            {
            }
        }
    }
}
